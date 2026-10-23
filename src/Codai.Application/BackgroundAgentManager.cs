using System.Collections.Concurrent;
using Codai.Core.Abstractions;
using Codai.Core.Agents;

namespace Codai.Application;

public sealed class BackgroundAgentManager : IBackgroundAgentManager
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    private readonly ISubAgent _subAgent;
    private readonly IAgentProgressSink _progress;
    private readonly IBackgroundAgentStore _store;
    private readonly string _projectRoot;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public Task<BackgroundAgentInfo> WaitAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            return Task.FromException<BackgroundAgentInfo>(new KeyNotFoundException($"Background task not found: {id}"));
        }

        return entry.Completion.Task.WaitAsync(cancellationToken);
    }

    public BackgroundAgentManager(
        ISubAgent subAgent,
        IBackgroundAgentStore? store = null,
        IAgentProgressSink? progress = null,
        string? projectRoot = null)
    {
        ArgumentNullException.ThrowIfNull(subAgent);
        _subAgent = subAgent;
        _store = store ?? NullBackgroundAgentStore.Instance;
        _progress = progress ?? NullAgentProgressSink.Instance;
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        foreach (var task in await _store.LoadAsync(_projectRoot, cancellationToken).ConfigureAwait(false))
        {
            var shouldResume = task.State == BackgroundAgentState.Running &&
                !string.IsNullOrWhiteSpace(task.Prompt) &&
                (task.LeaseExpiresAt is null || task.LeaseExpiresAt <= DateTimeOffset.UtcNow);
            var restoredTask = shouldResume
                ? task with
                {
                    LeaseId = Guid.NewGuid().ToString("N"),
                    LeaseExpiresAt = DateTimeOffset.UtcNow.Add(LeaseDuration),
                    UpdatedAt = DateTimeOffset.UtcNow,
                }
                : task;
            var entry = new Entry(
                restoredTask.Id,
                restoredTask.Description,
                restoredTask.Prompt ?? string.Empty,
                new CancellationTokenSource(),
                restoredTask.State,
                restoredTask.Report,
                restoredTask.StartedAt,
                restoredTask.UpdatedAt,
                restoredTask.LeaseId,
                restoredTask.LeaseExpiresAt,
                new TaskCompletionSource<BackgroundAgentInfo>(TaskCreationOptions.RunContinuationsAsynchronously));
            if (_entries.TryAdd(restoredTask.Id, entry) && shouldResume)
            {
                await PersistAsync(restoredTask).ConfigureAwait(false);
                _ = RunAsync(entry);
            }
            else if (_entries.ContainsKey(restoredTask.Id) && restoredTask.State != BackgroundAgentState.Running)
            {
                entry.Completion.TrySetResult(restoredTask);
            }
        }
    }

    public BackgroundAgentInfo Start(string description, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var now = DateTimeOffset.UtcNow;
        var entry = new Entry(
            Guid.NewGuid().ToString("N"),
            description,
            prompt,
            new CancellationTokenSource(),
            BackgroundAgentState.Running,
            null,
            now,
            now,
            Guid.NewGuid().ToString("N"),
            now.Add(LeaseDuration),
            new TaskCompletionSource<BackgroundAgentInfo>(TaskCreationOptions.RunContinuationsAsynchronously));

        _entries[entry.Id] = entry;
        _ = PersistAsync(entry.Info);
        _ = PublishAsync(entry, AgentProgressEventKind.Started, null);
        _ = RunAsync(entry);
        return entry.Info;
    }

    public IReadOnlyList<BackgroundAgentInfo> List() =>
        _entries.Values
            .Select(entry => entry.Info)
            .OrderByDescending(info => info.UpdatedAt)
            .ToArray();

    public bool Cancel(string id)
    {
        if (!_entries.TryGetValue(id, out var entry) || entry.Info.State != BackgroundAgentState.Running)
        {
            return false;
        }

        entry.Cancellation.Cancel();
        return true;
    }

    private async Task RunAsync(Entry entry)
    {
        try
        {
            var result = await _subAgent.RunAsync(
                new SubAgentRequest(entry.Description, entry.Prompt),
                entry.Cancellation.Token).ConfigureAwait(false);

            var state = result.Succeeded ? BackgroundAgentState.Completed : BackgroundAgentState.Failed;
            Update(entry, state, result.Report);
            await PublishAsync(
                entry,
                result.Succeeded ? AgentProgressEventKind.Completed : AgentProgressEventKind.Failed,
                result.Report).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Update(entry, BackgroundAgentState.Cancelled, "Cancelled");
            await PublishAsync(entry, AgentProgressEventKind.Cancelled, "Cancelled").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Update(entry, BackgroundAgentState.Failed, ex.Message);
            await PublishAsync(entry, AgentProgressEventKind.Failed, ex.Message).ConfigureAwait(false);
        }
        finally
        {
            entry.Cancellation.Dispose();
        }
    }

    private async Task PublishAsync(Entry entry, AgentProgressEventKind kind, string? message)
    {
        try
        {
            await _progress.PublishAsync(
                new AgentProgressEvent(entry.Id, entry.Description, kind, message, DateTimeOffset.UtcNow))
                .ConfigureAwait(false);
        }
        catch
        {
            // Progress must never change task outcome.
        }
    }

    private void Update(Entry entry, BackgroundAgentState state, string? report)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = _entries.AddOrUpdate(
            entry.Id,
            entry with
            {
                State = state,
                Report = report,
                UpdatedAt = now,
                LeaseId = null,
                LeaseExpiresAt = null,
            },
            (_, current) => current with
            {
                State = state,
                Report = report,
                UpdatedAt = now,
                LeaseId = null,
                LeaseExpiresAt = null,
            });
        updated.Completion.TrySetResult(updated.Info);
        _ = PersistAsync(updated.Info);
    }

    private async Task PersistAsync(BackgroundAgentInfo task)
    {
        try
        {
            await _store.SaveAsync(_projectRoot, task).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private sealed record Entry(
        string Id,
        string Description,
        string Prompt,
        CancellationTokenSource Cancellation,
        BackgroundAgentState State,
        string? Report,
        DateTimeOffset StartedAt,
        DateTimeOffset UpdatedAt,
        string? LeaseId,
        DateTimeOffset? LeaseExpiresAt,
        TaskCompletionSource<BackgroundAgentInfo> Completion)
    {
        public BackgroundAgentInfo Info =>
            new(Id, Description, State, Report, StartedAt, UpdatedAt, Prompt, LeaseId, LeaseExpiresAt);
    }
}
