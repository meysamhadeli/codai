using Codai.Core.Tools;
using Codai.Tools;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition helpers for the built-in tool set.
/// </summary>
public static class ToolServiceCollectionExtensions
{
    /// <summary>
    /// Registers the read-only tools the model may call and the registry that exposes
    /// them.
    /// </summary>
    /// <remarks>
    /// Every tool here is read-only, so none of them prompts for approval. A tool that
    /// writes or executes must set <see cref="ITool.IsReadOnly"/> to false, and is then
    /// gated by the registered <see cref="IToolApprover"/>.
    /// </remarks>
    public static IServiceCollection AddCodaiTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IToolRegistry>(_ => new ToolRegistry(
        [
            new ReadFileTool(),
            new ListFilesTool(),
            new GlobTool(),
            new GrepTool(),
        ]));

        return services;
    }
}
