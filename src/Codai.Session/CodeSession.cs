using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Models;
using Codai.Core.Prompting;

namespace Codai.Session;

/// <summary>
/// The interactive coding session: it scans the project once, then answers user
/// requests in a loop, streaming responses, tracking tokens and applying accepted
/// file changes.
/// </summary>
/// <remarks>
/// The flow mirrors the original Go <c>codai code</c> command, with two deliberate
/// fixes: the "requested context" retry now runs <em>after</em> a response asks for
/// more files (in the original it was evaluated against an empty buffer and could
/// never fire), and the conversation history receives exactly one entry per user
/// request.
/// </remarks>
public sealed class CodeSession
{
    private readonly IProjectScanner _scanner;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IChatCompletionService _chatCompletion;
    private readonly IChatHistory _history;
    private readonly ITokenTracker _tokenTracker;
    private readonly ICodeChangeExtractor _codeChangeExtractor;
    private readonly IDiffApplier _diffApplier;
    private readonly IRequestedContextResolver _requestedContextResolver;
    private readonly IUserInterface _ui;
    private readonly ICostCalculator? _costCalculator;
    private readonly SessionOptions _options;

    public CodeSession(
        SessionOptions options,
        IProjectScanner scanner,
        IPromptBuilder promptBuilder,
        IChatCompletionService chatCompletion,
        IChatHistory history,
        ITokenTracker tokenTracker,
        ICodeChangeExtractor codeChangeExtractor,
        IDiffApplier diffApplier,
        IRequestedContextResolver requestedContextResolver,
        IUserInterface ui,
        ICostCalculator? costCalculator = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(promptBuilder);
        ArgumentNullException.ThrowIfNull(chatCompletion);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(tokenTracker);
        ArgumentNullException.ThrowIfNull(codeChangeExtractor);
        ArgumentNullException.ThrowIfNull(diffApplier);
        ArgumentNullException.ThrowIfNull(requestedContextResolver);
        ArgumentNullException.ThrowIfNull(ui);

        _options = options;
        _scanner = scanner;
        _promptBuilder = promptBuilder;
        _chatCompletion = chatCompletion;
        _history = history;
        _tokenTracker = tokenTracker;
        _codeChangeExtractor = codeChangeExtractor;
        _diffApplier = diffApplier;
        _requestedContextResolver = requestedContextResolver;
        _ui = ui;
        _costCalculator = costCalculator;
    }

    /// <summary>Runs the loop until the user exits, the input ends, or cancellation is requested.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        _ui.WriteLine($":help  Help for code subcommand");

        var context = await LoadContextAsync(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? userInput;

            try
            {
                userInput = await _ui.ReadInputAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // End of input (Ctrl+D / closed stdin) ends the session.
            if (userInput is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(userInput))
            {
                continue;
            }

            var command = SlashCommandParser.Parse(userInput);
            if (command != SlashCommand.None)
            {
                if (HandleCommand(command))
                {
                    break;
                }

                continue;
            }

            try
            {
                await HandleRequestAsync(context, userInput, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ChatProviderException ex)
            {
                _ui.WriteError(ex.Message);
                DisplayTokens();
            }
        }

        return 0;
    }

    /// <summary>
    /// Scans the project. A failure (for example a missing directory) is reported
    /// but does not abort the session: the model still answers without context.
    /// </summary>
    private async Task<ProjectContext> LoadContextAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _ui
                .RunWithProgressAsync<ProjectContext>(
                    "Loading Context...",
                    ct => _scanner.ScanAsync(_options.ProjectRoot, ct),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            _ui.WriteError(ex.Message);
            return ProjectContext.Empty;
        }
    }

    /// <summary>Handles a slash command. Returns true when the session should exit.</summary>
    private bool HandleCommand(SlashCommand command)
    {
        switch (command)
        {
            case SlashCommand.Help:
                _ui.WriteLine(SlashCommandParser.HelpText);
                return false;

            case SlashCommand.Clear:
                _ui.Clear();
                return false;

            case SlashCommand.Token:
                DisplayTokens();
                return false;

            case SlashCommand.ClearToken:
                _tokenTracker.Clear();
                return false;

            case SlashCommand.ClearHistory:
                _history.Clear();
                return false;

            case SlashCommand.Exit:
                return true;

            default:
                return false;
        }
    }

    private async Task HandleRequestAsync(
        ProjectContext context,
        string userInput,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(context, userInput, requestedContext: null, cancellationToken).ConfigureAwait(false);

        response = await TryAddRequestedContextAsync(context, userInput, response, cancellationToken)
            .ConfigureAwait(false);

        _history.Add(new ChatTurn(DateTimeOffset.UtcNow, userInput, response));

        var changes = _codeChangeExtractor.Extract(response);

        if (changes.Count == 0)
        {
            _ui.WriteLine(string.Empty);
            DisplayTokens();
            return;
        }

        _ui.WriteLine(string.Empty);

        foreach (var change in changes)
        {
            var accepted = await _ui
                .ConfirmAsync($"Do you want to accept the change for file {change.RelativePath} ? (y/n):", cancellationToken)
                .ConfigureAwait(false);

            if (!accepted)
            {
                _ui.WriteError($"❌ Changes to {change.RelativePath} rejected.");
                continue;
            }

            try
            {
                var result = await _diffApplier
                    .ApplyAsync(_options.EffectiveWorkingDirectory, change, cancellationToken)
                    .ConfigureAwait(false);

                _ui.WriteSuccess(result.Deleted
                    ? $"✔️ {change.RelativePath} deleted (the change resulted in an empty file)."
                    : $"✔️ Changes accepted for {change.RelativePath}!");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ui.WriteError($"Error applying changes: {ex.Message}");
            }
        }

        DisplayTokens();
    }

    /// <summary>
    /// When the model asks for the full contents of specific files, confirm with the
    /// user and re-send the request with that context attached.
    /// </summary>
    private async Task<string> TryAddRequestedContextAsync(
        ProjectContext context,
        string userInput,
        string response,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableRequestedContext || !RequestedContextParser.TryParse(response, out var paths))
        {
            return response;
        }

        _ui.WriteLine(string.Empty);

        var accepted = await _ui
            .ConfirmAsync($"The model needs the full contents of {paths.Count} file(s). Add them and retry ? (y/n):", cancellationToken)
            .ConfigureAwait(false);

        if (!accepted)
        {
            _ui.WriteError("❌ Context rejected.");
            return response;
        }

        var requestedContext = await _requestedContextResolver
            .ResolveAsync(_options.EffectiveWorkingDirectory, paths, cancellationToken)
            .ConfigureAwait(false);

        if (requestedContext is null)
        {
            _ui.WriteError("🚫 None of the requested files could be read.");
            return response;
        }

        _ui.WriteSuccess("✔️ Context accepted!");

        return await SendAsync(context, userInput, requestedContext, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Streams one request, renders it and records its token usage.</summary>
    private async Task<string> SendAsync(
        ProjectContext context,
        string userInput,
        string? requestedContext,
        CancellationToken cancellationToken)
    {
        var request = _promptBuilder.Build(context, _history.Turns, userInput, requestedContext);

        var response = new StringBuilder();
        var usage = TokenUsage.Empty;

        await foreach (var update in _chatCompletion
            .StreamAsync(request, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(update.ContentDelta))
            {
                response.Append(update.ContentDelta);
                _ui.WriteMarkdown(update.ContentDelta);
            }

            if (update.IsCompleted && update.Usage.HasValue)
            {
                usage = update.Usage;
            }
        }

        _tokenTracker.Record(usage);

        return response.ToString();
    }

    private void DisplayTokens()
    {
        var session = _tokenTracker.SessionUsage;
        var cost = _costCalculator?.Calculate(_options.Provider, _options.Model, session) ?? 0m;

        _ui.ShowTokens(_options.Provider, _options.Model, session, _tokenTracker.LastUsage, cost);
    }
}
