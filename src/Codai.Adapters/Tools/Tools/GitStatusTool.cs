using Codai.Core.Abstractions;

namespace Codai.Tools;

/// <summary>Shows the current Git branch and changed files.</summary>
public sealed class GitStatusTool : GitToolBase
{
    public GitStatusTool(IProcessRunner runner) : base(runner) { }

    public override string Name => "git_status";

    public override string Description =>
        "Show the current Git branch and working-tree changes without modifying the repository.";

    protected override string CommandLine => "git status --short --branch";
}