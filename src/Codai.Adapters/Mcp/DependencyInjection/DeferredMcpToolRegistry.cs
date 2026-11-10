using Codai.Core.Tools;
using Codai.Mcp.Client;

namespace Codai.Mcp.DependencyInjection;

internal sealed class DeferredMcpToolRegistry : IAsyncToolRegistry, IDisposable
{
    private readonly ITool[] _localTools;
    private readonly string[] _disabledTools;
    private readonly Func<CancellationToken, Task<IReadOnlyList<McpToolDefinition>>> _discover;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private ToolRegistry _registry;
    private bool _initialized;

    internal DeferredMcpToolRegistry(IEnumerable<ITool> tools, IEnumerable<string> disabledTools,
        Func<CancellationToken, Task<IReadOnlyList<McpToolDefinition>>> discover)
    {
        _localTools = tools.ToArray();
        _disabledTools = disabledTools.ToArray();
        _discover = discover;
        var disabled = _disabledTools.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _registry = new ToolRegistry(_localTools.Where(tool => !disabled.Contains(tool.Name)));
        Tools = new CurrentTools(() => Volatile.Read(ref _registry).Tools);
    }

    public IReadOnlyList<ITool> Tools { get; }

    public bool TryGet(string name, out ITool tool) => Volatile.Read(ref _registry).TryGet(name, out tool);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }
            var definitions = await _discover(cancellationToken).ConfigureAwait(false);
            var registry = new ToolRegistry(_localTools.Concat(definitions.Select(definition => new McpToolAdapter(definition))), _disabledTools);
            Volatile.Write(ref _registry, registry);
            _initialized = true;
        }
        finally
        {
            _initialization.Release();
        }
    }

    public void Dispose() => _initialization.Dispose();

    private sealed class CurrentTools(Func<IReadOnlyList<ITool>> current) : IReadOnlyList<ITool>
    {
        public int Count => current().Count;
        public ITool this[int index] => current()[index];
        public IEnumerator<ITool> GetEnumerator() => current().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}