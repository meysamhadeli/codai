namespace Codai.Core.Customization;

/// <summary>
/// Reads the optional <c>---</c> front matter block at the top of a markdown file.
/// </summary>
/// <remarks>
/// Deliberately a small subset of YAML: <c>key: value</c> pairs only, with no nesting,
/// lists or anchors. The files this parses are written by hand by users and by skills,
/// and shipping a YAML dependency to read two keys would be a larger surface than the
/// problem deserves. A malformed line is skipped rather than thrown on, because a
/// broken description must not stop a session from starting.
/// </remarks>
public static class FrontMatter
{
    private const string Delimiter = "---";

    /// <summary>
    /// Splits <paramref name="text"/> into its front matter fields and its body.
    /// </summary>
    /// <returns>
    /// The fields (empty when there is no front matter) and the body with any front
    /// matter removed. Lookups on the field dictionary ignore case, so a file that
    /// writes <c>Description:</c> is read the same as one that writes <c>description:</c>.
    /// </returns>
    public static (IReadOnlyDictionary<string, string> Fields, string Body) Parse(string? text)
    {
        var empty = (Fields: (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(), Body: string.Empty);

        if (string.IsNullOrWhiteSpace(text))
        {
            return empty;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');

        if (!IsDelimiter(lines[0]))
        {
            return (empty.Fields, text);
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var closingIndex = -1;
        string? listKey = null;

        for (var i = 1; i < lines.Length; i++)
        {
            if (IsDelimiter(lines[i]))
            {
                closingIndex = i;
                break;
            }

            var line = lines[i];

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var listItem = line.Trim();
            if (listKey is not null && listItem.StartsWith("- ", StringComparison.Ordinal))
            {
                var listValue = Unquote(listItem[2..].Trim());
                fields[listKey] = string.IsNullOrWhiteSpace(fields[listKey])
                    ? listValue
                    : $"{fields[listKey]}, {listValue}";
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                listKey = null;
                continue;
            }

            var key = line[..separator].Trim();
            var fieldValue = Unquote(line[(separator + 1)..].Trim());

            if (key.Length > 0)
            {
                fields[key] = fieldValue;
                listKey = fieldValue.Length == 0 ? key : null;
            }
        }

        // An unterminated block is not front matter at all: treat the whole file as body
        // rather than silently swallowing it.
        if (closingIndex < 0)
        {
            return (empty.Fields, text);
        }

        var body = string.Join('\n', lines[(closingIndex + 1)..]).Trim();

        return (fields, body);
    }

    private static bool IsDelimiter(string line) =>
        line.TrimEnd().Equals(Delimiter, StringComparison.Ordinal);

    private static string Unquote(string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        var first = value[0];
        var last = value[^1];

        return first == last && (first == '"' || first == '\'')
            ? value[1..^1]
            : value;
    }
}
