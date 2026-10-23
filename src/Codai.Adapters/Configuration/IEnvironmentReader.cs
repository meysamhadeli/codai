namespace Codai.Configuration;

/// <summary>
/// Abstraction over environment variables so configuration resolution can be
/// tested deterministically.
/// </summary>
public interface IEnvironmentReader
{
    string? GetVariable(string name);

    /// <summary>All variables, keyed by name.</summary>
    IReadOnlyDictionary<string, string?> All();
}
