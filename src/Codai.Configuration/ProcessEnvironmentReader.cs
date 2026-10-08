using System.Collections;

namespace Codai.Configuration;

/// <summary>
/// Reads the current process environment.
/// </summary>
public sealed class ProcessEnvironmentReader : IEnvironmentReader
{
    public static ProcessEnvironmentReader Instance { get; } = new();

    public string? GetVariable(string name) => Environment.GetEnvironmentVariable(name);

    public IReadOnlyDictionary<string, string?> All()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = entry.Key?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                values[name] = entry.Value?.ToString();
            }
        }

        return values;
    }
}
