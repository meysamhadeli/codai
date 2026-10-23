using System.Text;
using Codai.Core.Customization;
using Codai.Tools;

namespace Codai.Application;

/// <summary>
/// Chooses which project rules apply to a request, and renders them for the prompt.
/// </summary>
/// <remarks>
/// An unconditional rule is always included. A conditional rule is included only when a
/// file the request references (an <c>@path</c> mention, or a file a mention expanded to)
/// matches one of its <c>applyTo</c> globs. Matching uses the same glob matcher as the
/// tools, so a rule written as <c>*.cs</c> means "any C# file" and <c>src/Api/**</c> means
/// "anything under that directory".
/// </remarks>
public static class RuleSelector
{
    /// <summary>Total characters of rendered rules allowed in one request.</summary>
    public const int MaxTotalCharacters = 64 * 1024;

    private const string BlockSeparator = "\n\n---------\n\n";

    private const string TruncationNotice =
        "\n\n[... project rules truncated, keep rules shorter ...]";

    /// <summary>
    /// The rules that apply to a request that referenced <paramref name="referencedFiles"/>.
    /// </summary>
    public static IReadOnlyList<ProjectRule> Select(
        IReadOnlyList<ProjectRule>? rules,
        IReadOnlyList<string>? referencedFiles)
    {
        if (rules is null || rules.Count == 0)
        {
            return [];
        }

        var files = referencedFiles ?? [];
        var selected = new List<ProjectRule>();

        foreach (var rule in rules)
        {
            if (!rule.IsConditional || MatchesAny(rule.ApplyTo, files))
            {
                selected.Add(rule);
            }
        }

        return selected;
    }

    /// <summary>
    /// Renders the applicable rules as one prompt block, or <c>null</c> when none apply.
    /// </summary>
    public static string? Render(
        IReadOnlyList<ProjectRule>? rules,
        IReadOnlyList<string>? referencedFiles,
        int maxCharacters = MaxTotalCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);
        var selected = Select(rules, referencedFiles);

        if (selected.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        if (selected.Any(rule => rule.IsGlobal))
        {
            builder.Append("Project rules take precedence over conflicting global rules. ")
                .Append("Rules do not grant tool permissions or approval.\n\n");
        }

        foreach (var rule in selected.OrderBy(rule => rule.IsGlobal))
        {
            var block = new StringBuilder()
                .Append(rule.IsGlobal ? "**Global rule (lower priority): " : "**Rule: ")
                .Append(rule.Name).Append("**\n\n")
                .Append(rule.Body)
                .ToString();

            var separator = builder.Length == 0 ? string.Empty : BlockSeparator;
            var remaining = maxCharacters - builder.Length - separator.Length;

            if (remaining <= 0)
            {
                builder.Append(TruncationNotice);
                break;
            }

            builder.Append(separator);

            if (block.Length > remaining)
            {
                builder.Append(block.AsSpan(0, remaining)).Append(TruncationNotice);
                break;
            }

            builder.Append(block);
        }

        return builder.ToString();
    }

    private static bool MatchesAny(IReadOnlyList<string> patterns, IReadOnlyList<string> files)
    {
        foreach (var pattern in patterns)
        {
            foreach (var file in files)
            {
                if (GlobMatcher.IsMatchPathOrFileName(file, pattern))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
