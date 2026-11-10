using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>Shows read-only working, staged, commit, or branch differences.</summary>
public sealed class GitDiffTool : GitToolBase
{
    public GitDiffTool(IProcessRunner runner) : base(runner) { }

    public override string Name => "git_diff";

    public override string Description =>
        "Show Git differences without modifying the repository. Use source working (default), staged, commit, or branch. " +
        "Commit and branch require a simple target ref. Use git_status and read_file for untracked files.";

    public override string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "source": { "type": "string", "enum": ["working", "staged", "commit", "branch"] },
            "target": { "type": "string", "description": "Commit hash or simple ref; required for commit and branch." }
          },
          "additionalProperties": false
        }
        """;

    protected override string CommandLine => "git --no-pager diff --no-ext-diff --no-textconv --color=never --";

    protected override bool TryGetCommandLine(JsonElement arguments, out string commandLine, out string failureReason)
    {
        commandLine = CommandLine;
        failureReason = string.Empty;
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            failureReason = "Git diff arguments must be an object.";
            return false;
        }
        var source = "working";
        if (arguments.TryGetProperty("source", out _) && !ToolArguments.TryGetString(arguments, "source", out source, out failureReason))
        {
            return false;
        }
        if (source is "working" or "staged")
        {
            if (arguments.TryGetProperty("target", out _))
            {
                failureReason = "A target is only accepted for commit or branch comparisons.";
                return false;
            }
            commandLine = source == "working" ? CommandLine : "git --no-pager diff --no-ext-diff --no-textconv --color=never --cached --";
            return true;
        }
        if (source is not ("commit" or "branch"))
        {
            failureReason = "Use source working, staged, commit, or branch.";
            return false;
        }
        if (!ToolArguments.TryGetString(arguments, "target", out var target, out failureReason))
        {
            return false;
        }
        if (target.Length > 256 || !char.IsAsciiLetterOrDigit(target[0]) || target.Contains("..", StringComparison.Ordinal) ||
            target.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('/' or '-' or '_' or '.')))
        {
            failureReason = "Use a commit hash or simple ref containing only ASCII letters, digits, '/', '-', '_', and '.', without '..' or a leading option.";
            return false;
        }
        commandLine = source == "commit"
            ? $"git --no-pager show --format=fuller --no-ext-diff --no-textconv --color=never {target} --"
            : $"git --no-pager diff --no-ext-diff --no-textconv --color=never {target}...HEAD --";
        return true;
    }
}
