namespace Codai.Core.Tools;

/// <summary>
/// A fixed set of tools, looked up by name.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _byName;

    /// <summary>Creates a registry over <paramref name="tools"/>.</summary>
    public ToolRegistry(IEnumerable<ITool> tools)
        : this(tools, [])
    {
    }

    /// <summary>Creates a registry excluding explicitly disabled tool names.</summary>
    public ToolRegistry(IEnumerable<ITool> tools, IEnumerable<string> disabledTools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(disabledTools);

        Tools = [.. tools];

        _byName = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in Tools)
        {
            // A duplicate name would make routing depend on registration order.
            if (!_byName.TryAdd(tool.Name, tool))
            {
                throw new ArgumentException(
                    $"Two tools are registered with the name '{tool.Name}'.",
                    nameof(tools));
            }
        }

        foreach (var name in disabledTools.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(name) || !_byName.Remove(name))
            {
                throw new ArgumentException($"Unknown disabled tool '{name}'.", nameof(disabledTools));
            }
        }

        Tools = Tools.Where(tool => _byName.ContainsKey(tool.Name)).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<ITool> Tools { get; }

    /// <summary>An empty tool set.</summary>
    public static ToolRegistry Empty { get; } = new([]);

    /// <inheritdoc />
    public bool TryGet(string name, out ITool tool)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            tool = null!;
            return false;
        }

        return _byName.TryGetValue(name, out tool!);
    }
}
