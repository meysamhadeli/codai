using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Chat;
using Codai.Core.Prompting;
using Codai.Core.Tokens;
using Codai.Core.Tools;
using Codai.Providers;
using Codai.Providers.Abstractions;

namespace Codai.Session;

/// <summary>
/// The default <see cref="ISubAgent"/>: a nested agent loop with a context window of its
/// own, a read-only tool set, and a budget separate from the request that started it.
/// </summary>
/// <remarks>
/// <para>
/// What it buys is the parent's window. An investigation that reads thirty files and greps a
/// dozen patterns costs the parent a paragraph instead of thirty file bodies.
/// </para>
/// <para>
/// Four decisions make that work, and each is a way of keeping the two loops from leaking
/// into each other:
/// </para>
/// <list type="bullet">
/// <item><b>Read-only tools only.</b> The set is derived from
/// <see cref="ITool.IsReadOnly"/>, so a sub-agent can never change anything, and delegation
/// cannot be used to do something the caller's mode would not allow. The executor it runs
/// through is built with the deny-all approver as well, so a mistake in that filter fails
/// closed rather than writing.</item>
/// <item><b>No nesting.</b> <see cref="SubAgent.ToolName"/> is excluded from the set, so a
/// sub-agent cannot start another one. Two levels would cost more than they explain.</item>
/// <item><b>A window of its own.</b> It gets no conversation history and, deliberately, no
/// project summary: the summary is what the parent already has, and copying it per run is
/// the cost this feature exists to avoid. The sub-agent finds what it needs with the
/// tools.</item>
/// <item><b>Its own budget.</b> A fresh <see cref="ToolCallGovernor"/> per run, and a
/// smaller one than a user request gets, because a sub-agent that needs sixty calls has
/// misunderstood the question.</item>
/// </list>
/// </remarks>
public sealed class SubAgentRunner : ISubAgent
{
    /// <summary>Upper bound on the report handed back to the caller.</summary>
    public const int DefaultMaxReportChars = 4_000;

    /// <summary>
    /// Tool calls one sub-agent run may make. Lower than a user request gets: the point of
    /// delegating is a bounded investigation, not a second session.
    /// </summary>
    public const int DefaultMaxToolCalls = 30;

    private readonly IChatClientFactory _clientFactory;
    private readonly ResolvedProviderOptions _providerOptions;
    private readonly IToolRegistry _registry;
    private readonly IFileSystem _fileSystem;
    private readonly ITokenTracker _tokenTracker;
    private readonly IUserInterface? _ui;
    private readonly string _projectRoot;
    private readonly int _maxReportChars;
    private readonly int _maxToolCalls;

    /// <summary>Creates the runner.</summary>
    /// <param name="clientFactory">Builds the sub-agent's own chat client.</param>
    /// <param name="providerOptions">The provider and model the session is using.</param>
    /// <param name="registry">Every tool the session offers; the read-only ones are used.</param>
    /// <param name="fileSystem">File access, so the run is testable without a disk.</param>
    /// <param name="tokenTracker">Records what the run cost, so it shows up in the totals.</param>
    /// <param name="ui">
    /// Where to report progress, or null in a host that registered no user interface. A
    /// run that shows nothing for a minute looks like a hang.
    /// </param>
    /// <param name="projectRoot">Directory the sub-agent's tools are bounded by.</param>
    /// <param name="maxReportChars">Upper bound on the report handed back.</param>
    /// <param name="maxToolCalls">Tool calls one run may make.</param>
    public SubAgentRunner(
        IChatClientFactory clientFactory,
        ResolvedProviderOptions providerOptions,
        IToolRegistry registry,
        IFileSystem fileSystem,
        ITokenTracker tokenTracker,
        IUserInterface? ui,
        string projectRoot,
        int maxReportChars = DefaultMaxReportChars,
        int maxToolCalls = DefaultMaxToolCalls)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(providerOptions);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(tokenTracker);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxReportChars);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxToolCalls);

        _clientFactory = clientFactory;
        _providerOptions = providerOptions;
        _registry = registry;
        _fileSystem = fileSystem;
        _tokenTracker = tokenTracker;
        _ui = ui;
        _projectRoot = projectRoot;
        _maxReportChars = maxReportChars;
        _maxToolCalls = maxToolCalls;
    }

    /// <inheritdoc />
    public async Task<SubAgentResult> RunAsync(
        SubAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tools = ReadOnlyTools();

        if (tools.Count == 0)
        {
            return SubAgentResult.Failure(
                "This session offers no read-only tools, so a sub-agent would have no way " +
                "to find anything out. Answer from what you already have.");
        }

        var report = new StringBuilder();
        var usage = TokenUsage.Empty;
        string? failure = null;

        try
        {
            await ReportAsync(
                $"Sub-agent: {request.Description}",
                async ct =>
                {
                    var chat = CreateChat(tools);

                    await foreach (var update in chat
                        .StreamAsync(BuildRequest(tools, request), ct)
                        .WithCancellation(ct)
                        .ConfigureAwait(false))
                    {
                        if (!string.IsNullOrEmpty(update.ContentDelta))
                        {
                            report.Append(update.ContentDelta);
                        }

                        if (update.IsCompleted && update.Usage.HasValue)
                        {
                            usage = update.Usage;
                        }
                    }

                    return true;
                },
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ChatProviderException ex)
        {
            failure =
                $"The sub-agent could not run: {ex.Message} Continue without it, or answer " +
                "from the tools you have.";
        }
        finally
        {
            // In a finally block because a run can report what it spent and then fail: the
            // tokens were spent either way, and a cost report that hides a failed run is a
            // lie. The variable is captured, so this sees whatever the stream last set.
            _tokenTracker.Record(usage);
        }

        if (failure is not null)
        {
            return SubAgentResult.Failure(failure);
        }

        if (report.Length == 0)
        {
            return SubAgentResult.Failure(
                "The sub-agent returned nothing usable. Continue without it, or answer from " +
                "the tools you have.");
        }

        return SubAgentResult.Success(
            $"# Report from a sub-agent\n\n{Truncate(report.ToString())}\n\n" +
            "(It investigated this with read-only tools, so it cannot have changed any " +
            "file. Verify anything you are about to act on.)");
    }

    /// <summary>
    /// The tools a sub-agent may use: the read-only ones, without the tool that starts a
    /// sub-agent.
    /// </summary>
    private List<ITool> ReadOnlyTools() =>
    [
        .. _registry.Tools.Where(tool =>
            tool.IsReadOnly &&
            !string.Equals(tool.Name, SubAgent.ToolName, StringComparison.OrdinalIgnoreCase)),
    ];

    /// <summary>
    /// Builds the sub-agent's client over its own tool set and a governor of its own, so
    /// its calls are neither charged to the parent's budget nor restricted by the parent's
    /// mode.
    /// </summary>
    private IChatCompletionService CreateChat(IReadOnlyList<ITool> tools)
    {
        var executor = new ToolRunner(
            new ToolRegistry(tools),

            // Read-only tools never ask, so this is never consulted. It is deny-all rather
            // than the host's approver so that a bug in the filter above cannot turn into a
            // write: the worst outcome is a refusal the model can read.
            DenyAllToolApprover.Instance,
            NullToolObserver.Instance,
            _fileSystem,
            _projectRoot,
            governor: new ToolCallGovernor { MaxCalls = _maxToolCalls });

        return _clientFactory.CreateService(_providerOptions, executor);
    }

    /// <summary>
    /// The request. The system prompt carries the role, the rules and the tool list; the
    /// task stays in the user message, because that is what it is.
    /// </summary>
    private static ChatRequest BuildRequest(IReadOnlyList<ITool> tools, SubAgentRequest request) =>
        new(
            new StringBuilder()
                .AppendLine("You are a sub-agent. Another assistant asked you to look into a")
                .AppendLine("project and report back, and you are the only one who sees this")
                .AppendLine("conversation.")
                .AppendLine()
                .AppendLine("## How to answer")
                .AppendLine()
                .AppendLine("- Find out what you need with the tools. Never guess at what a file")
                .AppendLine("  contains.")
                .AppendLine("- Name the files, symbols and line numbers you actually saw.")
                .AppendLine("- Quote only the smallest piece of code that proves the point.")
                .AppendLine("- Say plainly what you could not find or could not verify. A guess")
                .AppendLine("  reported as a fact is worse than a gap.")
                .AppendLine("- You cannot change anything, and you are not being asked to.")
                .AppendLine("- Be brief: a few hundred words. Whoever asked can open a file")
                .AppendLine("  themselves if they want the whole thing.")
                .AppendLine()
                .AppendLine("## Tools you can call")
                .AppendLine()
                .Append(ToolCatalogText.Lines(tools))
                .Append('\n')
                .ToString(),
            $"## Here is what the assistant needs\n\n{request.Description}\n\n{request.Prompt}");

    /// <summary>
    /// Runs the work under a progress label when the host has a user interface. A
    /// sub-agent's own tool calls are not reported: they would be indistinguishable from
    /// the parent's, and the label is what tells the user who is working.
    /// </summary>
    private Task<T> ReportAsync<T>(
        string label,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken) =>
        _ui is null
            ? work(cancellationToken)
            : _ui.RunWithProgressAsync(label, work, cancellationToken);

    /// <summary>
    /// Bounds the report. A sub-agent that answers with two thousand lines has moved the
    /// problem rather than solved it, so the report is cut and the cut is admitted.
    /// </summary>
    private string Truncate(string report) =>
        report.Length <= _maxReportChars
            ? report
            : string.Concat(
                report.AsSpan(0, _maxReportChars),
                $"\n\n[report truncated after {_maxReportChars} characters]");
}
