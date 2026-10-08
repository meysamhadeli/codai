using Codai.Core.Abstractions;
using Codai.Core.Diffs;
using Codai.Core.History;
using Codai.Core.Models;
using Codai.Core.Prompting;
using Codai.Core.Tokens;
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
            WorkingDirectory = Root,
            Provider = provider,
            Model = model,
        };

        FileSystem = new InMemoryFileSystem(Root);
        Chat = new ScriptedChatCompletionService();
        Ui = new FakeUserInterface();
        Scanner = new FakeProjectScanner();
        History = new ChatHistory();
        Tokens = new TokenTracker();
    }

    public string Root { get; }

    public SessionOptions Options { get; }

    public InMemoryFileSystem FileSystem { get; }

    public ScriptedChatCompletionService Chat { get; }

    public FakeUserInterface Ui { get; }

    public FakeProjectScanner Scanner { get; }

    public ChatHistory History { get; }

    public TokenTracker Tokens { get; }

    public ICostCalculator? CostCalculator { get; set; }

    public CodeSession Build() => new(
        Options,
        Scanner,
        new PromptBuilder(Template),
        Chat,
        History,
        Tokens,
        new CodeChangeExtractor(),
        new DiffApplier(FileSystem),
        new RequestedContextResolver(FileSystem),
        Ui,
        CostCalculator);

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
