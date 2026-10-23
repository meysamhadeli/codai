using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.Projects;
using Codai.Core.Prompting;
using Codai.Core.Tokens;
using Codai.Core.Tools;

namespace Codai.Application;

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
    private readonly AgentModeState _mode;
    private readonly ToolCallGovernor _governor;
    private readonly ToolAccessPolicy _access;
    private readonly AutoCompactor _autoCompactor;
    private readonly IUserInterface _ui;
    private readonly IAgentProgressSink _progress;
    private readonly ICostCalculator _costCalculator;
    private readonly AgentBudget _budget;
    private readonly RequestRuleGuidance? _ruleGuidance;

    /// <summary>Creates the service.</summary>
    public ChatExchangeService(
        SessionOptions options,
        IPromptBuilder promptBuilder,
        IChatCompletionService chatCompletion,
        IChatHistory history,
        ITokenTracker tokenTracker,
        IRequestedContextResolver requestedContextResolver,
        IMentionResolver mentionResolver,
        AgentModeState mode,
        ToolCallGovernor governor,
        ToolAccessPolicy access,
        AutoCompactor autoCompactor,
        IUserInterface ui,
        IAgentProgressSink? progress = null,
        ICostCalculator? costCalculator = null,
        AgentBudget? budget = null,
        RequestRuleGuidance? ruleGuidance = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(promptBuilder);
        ArgumentNullException.ThrowIfNull(chatCompletion);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(tokenTracker);
        ArgumentNullException.ThrowIfNull(requestedContextResolver);
        ArgumentNullException.ThrowIfNull(mentionResolver);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(governor);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(autoCompactor);
        ArgumentNullException.ThrowIfNull(ui);

        _options = options;
        _promptBuilder = promptBuilder;
        _chatCompletion = chatCompletion;
        _history = history;
        _tokenTracker = tokenTracker;
        _requestedContextResolver = requestedContextResolver;
        _mentionResolver = mentionResolver;
        _mode = mode;
        _governor = governor;
        _access = access;
        _autoCompactor = autoCompactor;
        _ui = ui;
        _progress = progress ?? NullAgentProgressSink.Instance;
        _costCalculator = costCalculator ?? NullCostCalculator.Instance;
        _budget = budget ?? new AgentBudget();
        _ruleGuidance = ruleGuidance;
    }

    /// <summary>
    /// Sends one user request and returns the final response text. Exactly one history
    /// entry is added per request, whatever happens in between.
    /// </summary>
    /// <param name="context">The project summary the prompt is built from.</param>
    /// <param name="userInput">The request as the user typed it, mentions included.</param>
    /// <param name="guidance">
    /// The project's instructions and skills, loaded once for the session.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<string> SendAsync(
        ProjectContext context,
        string userInput,
        ProjectGuidance? guidance = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(userInput);

        guidance ??= ProjectGuidance.Empty;

        // A new request is a new tool budget: the limits exist to stop one request running
        // away, not to ration the session.
        _governor.BeginRequest();

        // Before the prompt is built, because the history this may replace is the history
        // the prompt would otherwise contain. Nothing is waited for unless the conversation
        // has run out of room; below that, this only applies what the background finished
        // and starts the next summary.
        await _autoCompactor
            .BeforeRequestAsync(userInput, cancellationToken)
            .ConfigureAwait(false);

        // Resolved before the prompt is built: the files a request pulled in decide which
        // conditional project rules apply, and the same rendered context is attached.
        var mentions = await ResolveMentionsAsync(userInput, cancellationToken)
            .ConfigureAwait(false);
        _ruleGuidance?.BeginRequest(guidance.Rules, mentions.Files);

        var extras = new PromptExtras
        {
            // Read per request: the user may have changed it with :mode since the last one.
            Mode = _mode.Mode,
            Instructions = guidance.Instructions?.Content,
            Memory = guidance.Memory,
            Skills = guidance.Skills,
            ProjectRules = RuleSelector.Render(guidance.Rules, mentions.Files),
            AttachedContext = mentions.Context,
        };

        string response;
        try
        {
            response = await CompleteAsync(context, userInput, extras, cancellationToken)
                .ConfigureAwait(false);

            response = await TryAddRequestedContextAsync(context, userInput, extras, response, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _ruleGuidance?.EndRequest();
        }

        _history.Add(new ChatTurn(DateTimeOffset.UtcNow, userInput, response));

        // The turn is in the history now, which is the largest the conversation is going to
        // get before the next request. Anything that has to be summarised is started here so
        // that it is written while the user reads this answer, not before the next one is
        // sent.
        await _autoCompactor.AfterResponseAsync(cancellationToken).ConfigureAwait(false);

        return response;
    }

    /// <summary>
    /// Attaches the files the user referenced with <c>@path</c>. A mention that matches
    /// nothing is reported: staying quiet would hand the model a prompt without the
    /// context the user asked for, and the answer would look inexplicably wrong.
    /// </summary>
    private async Task<MentionResolution> ResolveMentionsAsync(
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

        return resolution;
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
    /// <remarks>
    /// A request the provider rejects as longer than the model's context window is worth one
    /// more attempt after the conversation has been shortened: the estimate that decided the
    /// request would fit was wrong, and the provider's own refusal is the only signal that
    /// says so. The retry happens at most once, and only when nothing was streamed — a
    /// rejection arrives before any content, so a failure after partial output is a different
    /// problem and must not be re-sent.
    /// </remarks>
    private async Task<string> CompleteAsync(
        ProjectContext context,
        string userInput,
        PromptExtras extras,
        CancellationToken cancellationToken)
    {
        var recovered = false;

        while (true)
        {
            var request = _promptBuilder.Build(context, _history.Turns, userInput,
                extras with { ProjectRules = _ruleGuidance?.RenderActivated() ?? extras.ProjectRules });

            // The request carries the tools the mode allows. Publishing them here is what
            // turns that list into a restriction the executor can enforce, rather than a
            // sentence in the prompt the model is trusted to respect.
            _access.BeginRequest(request.AllowedTools);

            var response = new StringBuilder();
            var usage = TokenUsage.Empty;
            var reasoning = false;
            _ui.WriteInfo("Thinking...");
            Publish(AgentProgressEventKind.ModelStarted, "model", userInput);

            try
            {
                await foreach (var update in _chatCompletion
                    .StreamAsync(request, cancellationToken)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (update.IsReasoning && !reasoning)
                    {
                        reasoning = true;
                        _ui.WriteInfo("Reasoning...");
                    }
                    if (!string.IsNullOrEmpty(update.ContentDelta))
                    {
                        reasoning = false;
                        response.Append(update.ContentDelta);
                        _ui.WriteMarkdown(update.ContentDelta);
                        Publish(AgentProgressEventKind.ModelDelta, "model", update.ContentDelta);
                    }

                    if (update.IsCompleted && update.Usage.HasValue)
                    {
                        usage = update.Usage;
                    }
                }
            }
            catch (ChatProviderException ex) when (
                !recovered && response.Length == 0 && ex.IsContextOverflow)
            {
                recovered = true;

                if (!await _autoCompactor
                    .RecoverFromOverflowAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw;
                }

                continue;
            }

            var cost = _costCalculator.Calculate(_options.Provider, _options.Model, usage);
            if (!_budget.TryConsume(usage, cost, out var budgetReason))
            {
                throw new ChatProviderException(budgetReason);
            }

            _tokenTracker.Record(usage);
            Publish(AgentProgressEventKind.ModelCompleted, "model", response.ToString());

            return response.ToString();
        }
    }

    private void Publish(AgentProgressEventKind kind, string description, string? message)
    {
        try
        {
            var notification = _progress.PublishAsync(new AgentProgressEvent(
                "session",
                description,
                kind,
                message,
                DateTimeOffset.UtcNow));

            if (!notification.IsCompletedSuccessfully)
            {
                _ = ObserveAsync(notification);
            }
        }
        catch
        {
        }
    }

    private static async Task ObserveAsync(ValueTask notification)
    {
        try
        {
            await notification.ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
