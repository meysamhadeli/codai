using Codai.Core.Abstractions;

namespace Codai.Tools;

/// <summary>Shows recent Git commits.</summary>
public sealed class GitLogTool : GitToolBase
{
    public GitLogTool(IProcessRunner runner) : base(runner) { }

    public override string Name => "git_log";

    public override string Description =>
        "Show recent Git commits to understand project history without modifying the repository.";

    protected override string CommandLine => "git log --oneline --decorate -n 20";
}