using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Codai.Core.Customization;

/// <summary>
/// Reads the optional <c>---</c> front matter block at the top of a markdown file.
/// </summary>
/// <remarks>
/// Reads top-level YAML scalars, including folded and literal multiline values,
/// and flattens scalar lists into comma-separated values. Nested mappings are ignored.
/// Malformed YAML falls back to line-based fields so a broken description does not
/// stop a session from starting. Unterminated headers remain part of the body.
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

        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(string.Join('\n', lines[1..closingIndex]) + "\n"));
            if (yaml.Documents.FirstOrDefault()?.RootNode is YamlMappingNode mapping)
            {
                fields.Clear();
                foreach (var field in mapping.Children)
                {
                    if (field.Key is not YamlScalarNode { Value: { Length: > 0 } key })
                    {
                        continue;
                    }
                    switch (field.Value)
                    {
                        case YamlScalarNode scalar:
                            fields[key] = scalar.Value ?? string.Empty;
                            break;
                        case YamlSequenceNode sequence:
                            fields[key] = string.Join(", ", sequence.Children.OfType<YamlScalarNode>().Select(value => value.Value ?? string.Empty));
                            break;
                    }
                }
            }
        }
        catch (YamlException)
        {
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
