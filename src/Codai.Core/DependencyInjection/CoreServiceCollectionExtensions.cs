using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Assets;
using Codai.Core.Chat;
using Codai.Core.Context;
using Codai.Core.Customization;
using Codai.Core.Diffs;
using Codai.Core.IO;
using Codai.Core.Prompting;
using Codai.Core.Tokens;
using Codai.Core.Tools;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition helpers for the core services.
/// </summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>Registers the always-available core services.</summary>
    public static IServiceCollection AddCodaiCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IChatHistory, ChatHistory>();
        services.AddSingleton<ITokenTracker, TokenTracker>();
        services.AddSingleton<ICostCalculator>(NullCostCalculator.Instance);
        services.AddSingleton<IFileSystem>(PhysicalFileSystem.Instance);

        // The tool registry is registered by the tool package, which is optional here: a
        // host without tools gets exactly the prompt it had before tools existed.
        services.AddSingleton<IPromptBuilder>(sp => new PromptBuilder(
            EmbeddedResources.FullContextTemplate,
            sp.GetService<IToolRegistry>()?.Tools));

        services.AddSingleton<IRequestedContextResolver>(_ => new RequestedContextResolver(PhysicalFileSystem.Instance));
        services.AddSingleton<IProjectInstructionsProvider>(_ => new ProjectInstructionsProvider(PhysicalFileSystem.Instance));
        services.AddSingleton<IProjectMemoryCatalog, ProjectMemoryCatalog>();
        services.AddSingleton<ISessionStore>(_ => new FileSessionStore(PhysicalFileSystem.Instance));
        services.AddSingleton<IAgentTaskStore>(_ => new FileAgentTaskStore(PhysicalFileSystem.Instance));
        services.AddSingleton<IWorkspaceCheckpointStore>(sp => new GitWorkspaceCheckpointStore(
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<IProcessRunner>()));
        services.AddSingleton<IBackgroundAgentStore>(_ => new FileBackgroundAgentStore(PhysicalFileSystem.Instance));
        services.AddSingleton<IAgentTeamStore>(_ => new FileAgentTeamStore(PhysicalFileSystem.Instance));
        services.AddSingleton<ITranscriptStore>(_ => new TranscriptStore(PhysicalFileSystem.Instance));
        services.AddSingleton<ICodeChangeExtractor, CodeChangeExtractor>();
        services.AddSingleton<IDiffApplier>(_ => new DiffApplier(PhysicalFileSystem.Instance));

        // Project customization: the commands and skills a project ships in .codai, and
        // the folders they live in. Registered here rather than in the tool package
        // because the prompt, the session loop and the tools all read them.
        services.AddSingleton<ICommandCatalog, CommandCatalog>();
        services.AddSingleton<ISkillCatalog, SkillCatalog>();
        services.AddSingleton<IProjectRuleCatalog, ProjectRuleCatalog>();

        // Long-running work is behind an interface so tools can start a process without
        // knowing how one is started, which is also what makes the behaviour testable.
        services.AddSingleton<IProcessRunner>(_ => new ProcessRunner());

        // One governor per session: it counts the calls of the request in flight, so it
        // has to be the same instance the executor uses.
        services.AddSingleton<ToolCallGovernor>();

        // Same reasoning for the tool set: the request records what it may call here and
        // the executor reads it, so the two have to be looking at one object.
        services.AddSingleton<ToolAccessPolicy>();

        // The mode a session is running in. A mutable holder rather than a constructor
        // argument because the user can change it mid-session with :mode.
        services.AddSingleton<AgentModeState>();
        services.AddSingleton<AgentTaskContext>();
        services.AddSingleton<AgentBudget>();
        services.AddSingleton<IAgentProgressSink>(NullAgentProgressSink.Instance);

        // Holds the compaction circuit-breaker state, so it too must be a single instance.
        services.AddSingleton<CompactionPolicy>();

        // Safe defaults: nothing may change state until a host registers a real
        // approver, and progress messages are dropped until one registers an observer.
        services.TryAddSingleton<IToolApprover>(DenyAllToolApprover.Instance);
        services.TryAddSingleton<IToolObserver>(NullToolObserver.Instance);

        return services;
    }
}
