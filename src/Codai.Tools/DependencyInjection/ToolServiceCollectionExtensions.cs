using Codai.Core.Abstractions;
using Codai.Core.Tools;
using Codai.Tools;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition helpers for the built-in tool set.
/// </summary>
public static class ToolServiceCollectionExtensions
{
    /// <summary>
    /// Registers the built-in tools and the registry that exposes them.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="allowShell">
    /// When true, <c>run_command</c> is registered as well. It is off by default because
    /// it runs arbitrary command lines with the user's own privileges; a host opts in
    /// only when it can also register an <see cref="IToolApprover"/> that asks before
    /// every such call.
    /// </param>
    /// <remarks>
    /// The read-only tools never prompt. A tool that writes or executes reports
    /// <see cref="ITool.IsReadOnly"/> as false and is gated by the registered
    /// <see cref="IToolApprover"/>.
    /// </remarks>
    public static IServiceCollection AddCodaiTools(
        this IServiceCollection services,
        bool allowShell = false)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IToolRegistry>(sp =>
        {
            var tools = new List<ITool>
            {
                // Read-only: everything the model can use to look around.
                new ReadFileTool(),
                new ListFilesTool(),
                new GlobTool(),
                new GrepTool(),
                new ReadSkillTool(sp.GetRequiredService<ISkillCatalog>()),

                // Writes: offered only in agent mode, and each call asks first.
                new WriteFileTool(),
                new EditFileTool(),
            };

            if (allowShell)
            {
                tools.Add(new RunCommandTool(sp.GetRequiredService<IProcessRunner>()));
            }

            return new ToolRegistry(tools);
        });

        return services;
    }
}
