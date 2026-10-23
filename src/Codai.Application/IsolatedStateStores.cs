using System.Security.Cryptography;
using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;

namespace Codai.Application;

internal sealed class IsolatedStateStores(IFileSystem fileSystem, string dataDirectory)
    : ISessionStore, IAgentTaskStore, IBackgroundAgentStore, IAgentTeamStore, ITranscriptStore
{
    private readonly FileSessionStore _sessions = new(fileSystem);
    private readonly FileAgentTaskStore _tasks = new(fileSystem);
    private readonly FileBackgroundAgentStore _background = new(fileSystem);
    private readonly FileAgentTeamStore _teams = new(fileSystem);
    private readonly TranscriptStore _transcripts = new(fileSystem);

    Task<IReadOnlyList<ChatTurn>> ISessionStore.LoadAsync(string rootDirectory, CancellationToken cancellationToken) =>
        _sessions.LoadAsync(StorageRoot(rootDirectory), cancellationToken);

    Task<IReadOnlyList<ChatTurn>> ISessionStore.LoadAsync(string rootDirectory, string taskId, CancellationToken cancellationToken) =>
        _sessions.LoadAsync(StorageRoot(rootDirectory), taskId, cancellationToken);

    Task ISessionStore.SaveAsync(string rootDirectory, IReadOnlyList<ChatTurn> turns, CancellationToken cancellationToken) =>
        _sessions.SaveAsync(StorageRoot(rootDirectory), turns, cancellationToken);

    Task ISessionStore.SaveAsync(string rootDirectory, string taskId, IReadOnlyList<ChatTurn> turns, CancellationToken cancellationToken) =>
        _sessions.SaveAsync(StorageRoot(rootDirectory), taskId, turns, cancellationToken);

    Task<AgentTask?> IAgentTaskStore.LoadAsync(string rootDirectory, CancellationToken cancellationToken) =>
        _tasks.LoadAsync(StorageRoot(rootDirectory), cancellationToken);

    Task<IReadOnlyList<AgentTask>> IAgentTaskStore.ListAsync(string rootDirectory, CancellationToken cancellationToken) =>
        _tasks.ListAsync(StorageRoot(rootDirectory), cancellationToken);

    Task IAgentTaskStore.SaveAsync(string rootDirectory, AgentTask task, CancellationToken cancellationToken) =>
        _tasks.SaveAsync(StorageRoot(rootDirectory), task, cancellationToken);

    Task IAgentTaskStore.ExportAsync(string rootDirectory, string taskId, string path, CancellationToken cancellationToken) =>
        _tasks.ExportAsync(StorageRoot(rootDirectory), taskId, path, cancellationToken);

    Task IAgentTaskStore.ImportAsync(string rootDirectory, string path, CancellationToken cancellationToken) =>
        _tasks.ImportAsync(StorageRoot(rootDirectory), path, cancellationToken);

    Task<IReadOnlyList<BackgroundAgentInfo>> IBackgroundAgentStore.LoadAsync(string rootDirectory, CancellationToken cancellationToken) =>
        _background.LoadAsync(StorageRoot(rootDirectory), cancellationToken);

    Task IBackgroundAgentStore.SaveAsync(string rootDirectory, BackgroundAgentInfo task, CancellationToken cancellationToken) =>
        _background.SaveAsync(StorageRoot(rootDirectory), task, cancellationToken);

    Task<IReadOnlyList<AgentTeam>> IAgentTeamStore.LoadAsync(string rootDirectory, CancellationToken cancellationToken) =>
        _teams.LoadAsync(StorageRoot(rootDirectory), cancellationToken);

    Task IAgentTeamStore.SaveAsync(string rootDirectory, AgentTeam team, CancellationToken cancellationToken) =>
        _teams.SaveAsync(StorageRoot(rootDirectory), team, cancellationToken);

    async Task<string?> ITranscriptStore.ArchiveAsync(string rootDirectory, IReadOnlyList<ChatTurn> turns,
        CancellationToken cancellationToken)
    {
        var storageRoot = StorageRoot(rootDirectory);
        var archive = await _transcripts.ArchiveAsync(storageRoot, turns, cancellationToken).ConfigureAwait(false);
        return archive is null ? null : Path.Combine(storageRoot, archive);
    }

    private string StorageRoot(string projectRoot)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        if (OperatingSystem.IsWindows())
        {
            normalized = normalized.ToUpperInvariant();
        }

        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(dataDirectory, "projects", key);
    }
}
