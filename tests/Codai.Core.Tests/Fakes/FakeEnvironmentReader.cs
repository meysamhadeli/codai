using Codai.Configuration;

namespace Codai.Core.Tests.Fakes;

/// <summary>Deterministic environment used by configuration tests.</summary>
internal sealed class FakeEnvironmentReader : IEnvironmentReader
{
    private readonly Dictionary<string, string?> _values;

    public FakeEnvironmentReader(IDictionary<string, string?>? values = null)
    {
        _values = new Dictionary<string, string?>(
            values ?? new Dictionary<string, string?>(),
            StringComparer.OrdinalIgnoreCase);
    }

    public string? GetVariable(string name) => _values.TryGetValue(name, out var value) ? value : null;

    public IReadOnlyDictionary<string, string?> All() => _values;
}
