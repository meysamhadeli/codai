using Codai.Core.Tools;

namespace Codai.Session.Tests.Fakes;

/// <summary>
/// A registry over a fixed set of tools, so a test can decide what a session offers
/// without registering the whole tool package.
/// </summary>
internal sealed class StubToolRegistry : IToolRegistry
{
    /// <summary>Creates a registry over <paramref name="tools"/>.</summary>
    public StubToolRegistry(params ITool[] tools) => Tools = [.. tools];

    /// <inheritdoc />
    public IReadOnlyList<ITool> Tools { get; }

    /// <inheritdoc />
    public bool TryGet(string name, out ITool tool)
    {
        var found = Tools.FirstOrDefault(candidate => candidate.Name == name);

        tool = found!;
        return found is not null;
    }
}
