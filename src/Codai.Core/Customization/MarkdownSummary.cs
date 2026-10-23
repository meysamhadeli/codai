namespace Codai.Core.Customization;

/// <summary>
/// Derives a one-line summary from a markdown body when a file has no front matter.
/// </summary>
internal static class MarkdownSummary
{
    /// <summary>Longest summary produced. Anything longer reads as a paragraph, not a label.</summary>
    public const int MaxLength = 120;

    /// <summary>
    /// Returns the first line that carries text, with leading <c>#</c> markers removed
    /// and the result capped at <see cref="MaxLength"/> characters.
    /// </summary>
    public static string FromBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('#').Trim();

            if (line.Length > 0)
            {
                return Truncate(line);
            }
        }

        return string.Empty;
    }

    /// <summary>Collapses whitespace and caps the length.</summary>
    public static string Truncate(string value)
    {
        var collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return collapsed.Length <= MaxLength ? collapsed : collapsed[..MaxLength].TrimEnd() + "…";
    }
}
