using System.Text.RegularExpressions;
using System.Text.Json;

namespace Codai.Core.Prompting;

/// <summary>
/// Parses the "I need the full contents of these files" response the model returns
/// when the project signatures are not enough.
/// </summary>
/// <remarks>
/// Ports the original behaviour: a JSON array is located anywhere in the response
/// (inside an optional surrounding object) and each element is treated as a
/// relative file path.
/// </remarks>
public static partial class RequestedContextParser
{
    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex ArrayRegex();

    /// <summary>
    /// Attempts to read a JSON string array from <paramref name="response"/>.
    /// </summary>
    public static bool TryParse(string? response, out IReadOnlyList<string> paths)
    {
        paths = [];

        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        var match = ArrayRegex().Match(response);

        if (!match.Success)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(match.Value, PromptingJsonContext.Default.StringArray);

            if (parsed is null || parsed.Length == 0)
            {
                return false;
            }

            paths = parsed
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .ToList();

            return paths.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
