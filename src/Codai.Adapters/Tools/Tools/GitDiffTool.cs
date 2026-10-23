using Codai.Core.Abstractions;

namespace Codai.Tools;

/// <summary>Shows the current unstaged Git diff.</summary>
public sealed class GitDiffTool : GitToolBase
{
    public GitDiffTool(IProcessRunner runner) : base(runner) { }

    public override string Name => "git_diff";

    public override string Description =>
        "Show the current unstaged Git diff without modifying the repository.";

    protected override string CommandLine => "git diff --no-ext-diff --";
}