using System.Text.RegularExpressions;

namespace Codai.Core.Prompting;

/// <summary>
/// Finds the <c>@path</c> mentions the user typed in a request.
/// </summary>
/// <remarks>
/// A mention may be a file, a directory, or a glob. Parsing is deliberately a pure
/// string operation: resolving a mention needs the file system, and keeping the two
/// apart is what makes the syntax testable on its own.
/// </remarks>
public static partial class MentionParser
{
    /// <summary>
    /// An <c>@</c> that starts a token. The negative look-behind keeps e-mail
    /// addresses and already-quoted handles out.
    /// </summary>
    [GeneratedRegex(@"(?<![\w@])@([^\s@]+)", RegexOptions.CultureInvariant)]
    private static partial Regex MentionRegex();

    /// <summary>
    /// Punctuation that commonly trails a path when it is written in a sentence
    /// ("look at @src/Program.cs, please"). A period is not in the list because it is
    /// part of most file names.
    /// </summary>
    private static readonly char[] TrailingPunctuation =
        [',', ';', ':', '!', '?', ')', ']', '}', '"', '\'', '`', '>', '='];

    /// <summary>
    /// Returns the distinct mentions in <paramref name="userInput"/>, in the order they
    /// appear and without the leading <c>@</c>.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mentions = new List<string>();

        foreach (Match match in MentionRegex().Matches(userInput))
        {
            var mention = match.Groups[1].Value.TrimEnd(TrailingPunctuation).Trim();

            if (mention.Length == 0 || !seen.Add(mention))
            {
                continue;
            }

            mentions.Add(mention);
        }

        return mentions;
    }
}
