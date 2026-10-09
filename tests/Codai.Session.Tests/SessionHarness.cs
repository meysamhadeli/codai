using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.Context;
using Codai.Core.Customization;
using Codai.Core.Diffs;
using Codai.Core.Projects;
using Codai.Core.Prompting;
using Codai.Core.Tokens;
using Codai.Core.Tools;
using Codai.Session.Tests.Fakes;

namespace Codai.Session.Tests;

/// <summary>
/// Builds a <see cref="CodeSession"/> from real core services plus scripted fakes,
/// so tests exercise the actual prompt builder, extractor, diff applier and
/// requested-context resolver.
/// </summary>
internal sealed class SessionHarness
{
    private const string Template = "You are a helpful coding assistant.";

    public SessionHarness(string projectRoot = @"C:\project", string provider = "deepseek", string model = "deepseek-flash")
    {
        Root = projectRoot;
        Options = new SessionOptions
        {
            ProjectRoot = Root,
            Provider = provider,
            Model = model,
        };

        FileSystem = new InMemoryFileSystem(Root);
        Chat = new ScriptedChatCompletionService();
        Ui = new FakeUserInterface();
        Scanner = new FakeProjectScanner();
        History = new ChatHistory();
        Tokens = new TokenTracker();
        SessionStore = new FileSessionStore(FileSystem);

        // The same wiring the composition root uses: one governor and one access policy
        // shared by the request that records them and the executor that enforces them.
        Executor = new ToolRunner(
            new ToolRegistry(Tools),
            DenyAllToolApprover.Instance,
            NullToolObserver.Instance,
            FileSystem,
            Root,
            governor: Governor,
            access: Access);
    }

    public string Root { get; }

    public SessionOptions Options { get; set; }

    public InMemoryFileSystem FileSystem { get; }

    public ScriptedChatCompletionService Chat { get; }

    public FakeUserInterface Ui { get; }

    public FakeProjectScanner Scanner { get; }

    public ChatHistory History { get; }

    public TokenTracker Tokens { get; }

    public ICostCalculator? CostCalculator { get; set; }

    /// <summary>Instructions the project "ships"; null means the project has none.</summary>
    public string? Instructions { get; set; }

    /// <summary>The store the session persists into. Backed by the in-memory file system.</summary>
    public ISessionStore SessionStore { get; }

    /// <summary>The mode the session is running in. Tests change it the way the user does.</summary>
    public AgentModeState Mode { get; } = new();

    /// <summary>The per-request tool budget the session shares with its executor.</summary>
    public ToolCallGovernor Governor { get; } = new();

    /// <summary>
    /// The tools the request in flight may call, shared with the executor the way the
    /// composition root shares it, so a test exercises the same wiring the CLI does.
    /// </summary>
    public ToolAccessPolicy Access { get; } = new();

    /// <summary>
    /// The tools the session offers. A read-only one and a mutating one, because the
    /// difference between them is what a mode decides.
    /// </summary>
    public IReadOnlyList<ITool> Tools { get; } =
    [
        new StubTool("read_file"),
        new StubTool("write_file", isReadOnly: false),
    ];

    /// <summary>
    /// The executor the session's tools run through. Always refuses a write, because it
    /// is built with the deny-all approver: these tests are about which tools a mode
    /// offers, not about the approval prompt.
    /// </summary>
    public IToolExecutor Executor { get; }

    public CodeSession Build()
    {
        var instructionsProvider = new FakeProjectInstructionsProvider(Instructions);
        var skills = new SkillCatalog(FileSystem);
        var commands = new CommandCatalog(FileSystem);
        var compactor = new HistoryCompactor(Chat, History, Tokens, Access, Ui);

        return new CodeSession(
            Options,
            Ui,
            new ProjectContextLoader(Options, Scanner, Ui),
            new ProjectGuidanceLoader(Options, instructionsProvider, skills, Ui),
            new ChatExchangeService(
                Options,
                new PromptBuilder(Template, Tools),
                Chat,
                History,
                Tokens,
                new RequestedContextResolver(FileSystem),
                new MentionResolver(FileSystem),
                Mode,
                Governor,
                Access,
                new AutoCompactor(Options, History, compactor, new CompactionPolicy(), Ui),
                Ui),
            new ChangeApplicator(Options, new CodeChangeExtractor(), new DiffApplier(FileSystem), Mode, Ui),
            new SessionCommandHandler(
                Options,
                History,
                Tokens,
                CostCalculator ?? NullCostCalculator.Instance,
                compactor,
                Mode,
                Ui),
            new UserCommandResolver(commands, Options),
            History,
            SessionStore);
    }

    /// <summary>Queues the given lines followed by end-of-input.</summary>
    public void QueueInputs(params string[] lines)
    {
        foreach (var line in lines)
        {
            Ui.EnqueueInput(line);
        }

        Ui.EnqueueInput(null);
    }
}

/// <summary>Returns a fixed project context, or throws when configured to fail.</summary>
internal sealed class FakeProjectScanner : IProjectScanner
{
    public ProjectContext Result { get; set; } = new()
    {
        Files = [],
        RawCodes = ["**File: Program.cs**\n\nclass Program"],
    };

    public Exception? Failure { get; set; }

    public List<string> ScannedRoots { get; } = [];

    public Task<ProjectContext> ScanAsync(string rootDirectory, CancellationToken cancellationToken = default)
    {
        ScannedRoots.Add(rootDirectory);

        return Failure is null
            ? Task.FromResult(Result)
            : Task.FromException<ProjectContext>(Failure);
    }
}
