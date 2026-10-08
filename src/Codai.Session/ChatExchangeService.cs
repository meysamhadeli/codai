using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Chat;
using Codai.Core.Projects;
using Codai.Core.Prompting;
using Codai.Core.Tokens;

namespace Codai.Session;

/// <summary>
/// Runs a single exchange with the model: builds the prompt, streams the answer to the
/// user interface, records token usage and appends the turn to the history.
/// </summary>
/// <remarks>
/// When the model asks for the full contents of specific files, the user is asked to
/// confirm and the request is re-sent with that context attached. The retry runs
/// <em>after</em> the first response is complete — in the original Go implementation it
/// was evaluated against an empty buffer, so it could never fire.
/// </remarks>
public sealed class ChatExchangeService
{
    private readonly SessionOptions _options;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IChatCompletionService _chatCompletion;
    private readonly IChatHistory _history;
    private readonly ITokenTracker _tokenTracker;
    private readonly IRequestedContextResolver _requestedContextResolver;
    private readonly IMentionResolver _mentionResolver;
    private readonly IUserInterface _ui;

    /// <summary>Creates the service.</summary>
    public ChatExchangeService(
        SessionOptions options,
        IPromptBuilder promptBuilder,
        IChatCompletionService chatCompletion,
        IChatHistory history,
        ITokenTracker tokenTracker,
        IRequestedContextResolver requestedContextResolver,
        IMentionResolver mentionResolver,
        IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(promptBuilder);
        ArgumentNullException.ThrowIfNull(chatCompletion);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(tokenTracker);
        ArgumentNullException.ThrowIfNull(requestedContextResolver);
        ArgumentNullException.ThrowIfNull(mentionResolver);
        ArgumentNullException.ThrowIfNull(ui);

        _options = options;
        _promptBuilder = promptBuilder;
        _chatCompletion = chatCompletion;
        _history = history;
        _tokenTracker = tokenTracker;
        _requestedContextResolver = requestedContextResolver;
        _mentionResolver = mentionResolver;
        _ui = ui;
    }

    /// <summary>
    /// Sends one user request and returns the final response text. Exactly one history
    /// entry is added per request, whatever happens in between.
    /// </summary>
    /// <param name="context">The project summary the prompt is built from.</param>
    /// <param name="userInput">The request as the user typed it, mentions included.</param>
    /// <param name="instructions">
    /// Project instructions for this session, when the project ships any.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<string> SendAsync(
        ProjectContext context,
        string userInput,
        ProjectInstructions? instructions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(userInput);

        var extras = new PromptExtras
        {
            Instructions = instructions?.Content,
            AttachedContext = await ResolveMentionsAsync(userInput, cancellationToken)
                .ConfigureAwait(false),
        };

        var response = await CompleteAsync(context, userInput, extras, cancellationToken)
            .ConfigureAwait(false);

        response = await TryAddRequestedContextAsync(context, userInput, extras, response, cancellationToken)
            .ConfigureAwait(false);

        _history.Add(new ChatTurn(DateTimeOffset.UtcNow, userInput, response));

        return response;
    }

    /// <summary>
    /// Attaches the files the user referenced with <c>@path</c>. A mention that matches
    /// nothing is reported: staying quiet would hand the model a prompt without the
    /// context the user asked for, and the answer would look inexplicably wrong.
    /// </summary>
    private async Task<string?> ResolveMentionsAsync(
        string userInput,
        CancellationToken cancellationToken)
    {
        var resolution = await _mentionResolver
            .ResolveAsync(_options.ProjectRoot, userInput, cancellationToken)
            .ConfigureAwait(false);

        foreach (var mention in resolution.Unresolved)
        {
            _ui.WriteError($"No file matches '@{mention}'.");
        }

        if (resolution.Files.Count > 0)
        {
            _ui.WriteInfo($"📎 Attached {resolution.Files.Count} file(s): {string.Join(", ", resolution.Files)}");
        }

        return resolution.Context;
    }

    /// <summary>
    /// Confirms with the user and re-sends the request with the requested files attached
    /// when the response asked for them.
    /// </summary>
    private async Task<string> TryAddRequestedContextAsync(
        ProjectContext context,
        string userInput,
        PromptExtras extras,
        string response,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableRequestedContext || !RequestedContextParser.TryParse(response, out var paths))
        {
            return response;
        }

        _ui.WriteLine(string.Empty);

        var accepted = await _ui
            .ConfirmAsync(
                $"The model needs the full contents of {paths.Count} file(s). Add them and retry ? (y/n):",
                cancellationToken)
            .ConfigureAwait(false);

        if (!accepted)
        {
            _ui.WriteError("❌ Context rejected.");
            return response;
        }

        var requestedContext = await _requestedContextResolver
            .ResolveAsync(_options.ProjectRoot, paths, cancellationToken)
            .ConfigureAwait(false);

        if (requestedContext is null)
        {
            _ui.WriteError("🚫 None of the requested files could be read.");
            return response;
        }

        _ui.WriteSuccess("✔️ Context accepted!");

        return await CompleteAsync(context, userInput, extras with { RequestedContext = requestedContext }, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Streams one request, renders it and records its token usage.</summary>
    private async Task<string> CompleteAsync(
        ProjectContext context,
        string userInput,
        PromptExtras extras,
        CancellationToken cancellationToken)
    {
        var request = _promptBuilder.Build(context, _history.Turns, userInput, extras);

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
}
