using Codai.Core.Abstractions;
using Codai.Core.Tools;
using Codai.Tools;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    /// <param name="allowSubAgents">
    /// When true, <c>task</c> is registered as well. It needs the host to have registered
    /// an <see cref="ISubAgent"/>, which is where the nested loop lives, and it is off by
    /// default because every use spends a model call.
    /// </param>
    /// <remarks>
    /// Read-only tools skip approval; foreground-interaction tools can ask questions.
    /// A tool that writes or executes reports
    /// <see cref="ITool.IsReadOnly"/> as false and is gated by the registered
    /// <see cref="IToolApprover"/>.
    /// </remarks>
    public static IServiceCollection AddCodaiTools(
        this IServiceCollection services,
        bool allowShell = false,
        bool allowSubAgents = false)
        => services.AddCodaiTools(allowShell, allowSubAgents, []);

    /// <summary>Registers available tools, excluding explicitly disabled names.</summary>
    public static IServiceCollection AddCodaiTools(
        this IServiceCollection services,
        bool allowShell,
        bool allowSubAgents,
        IEnumerable<string> disabledTools)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(disabledTools);
        var disabled = disabledTools.ToArray();

        services.TryAddSingleton<IClarificationHandler>(UnavailableClarificationHandler.Instance);
        services.TryAddSingleton<PublicWebTransport>();

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
                new SkillsTool(sp.GetRequiredService<ISkillCatalog>()),
                new ReadSkillResourceTool(sp.GetRequiredService<ISkillCatalog>()),
                new SubmitTaskTool(),
                new AskQuestionTool(sp.GetRequiredService<IClarificationHandler>()),
                new FetchWebTool(sp.GetRequiredService<PublicWebTransport>().Client),
                new GitStatusTool(sp.GetRequiredService<IProcessRunner>()),
                new GitDiffTool(sp.GetRequiredService<IProcessRunner>()),
                new GitLogTool(sp.GetRequiredService<IProcessRunner>()),

                // Writes: offered only in agent mode, and each call asks first.
                new WriteFileTool(),
                new EditFileTool(),
                new ApplyPatchTool(),
            };

            ITool[] shellTools =
            [
                new RunCommandTool(sp.GetRequiredService<IProcessRunner>()),
                new DotnetTestTool(sp.GetRequiredService<IProcessRunner>()),
                new DotnetBuildTool(sp.GetRequiredService<IProcessRunner>()),
            ];
            tools.AddRange(shellTools);
            var unavailable = new List<string>(disabled);
            if (!allowShell)
            {
                unavailable.AddRange(shellTools.Select(tool => tool.Name));
            }

            var taskTool = new TaskTool(sp.GetRequiredService<ISubAgent>);
            tools.Add(taskTool);
            if (!allowSubAgents)
            {
                unavailable.Add(taskTool.Name);
            }

            tools.AddRange(sp.GetServices<ITool>());

            return new ToolRegistry(tools, unavailable);
        });

        return services;
    }
}
