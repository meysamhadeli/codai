using System.Collections.Concurrent;
using Codai.Core.Abstractions;
using Codai.Core.Agents;

namespace Codai.Application;

/// <summary>
/// Coordinates a small, read-only group of background specialists. It owns orchestration
/// state; execution and tool policy remain in <see cref="IBackgroundAgentManager"/> and
/// <see cref="ISubAgent"/>.
/// </summary>
public sealed class AgentTeamCoordinator : IAgentTeamCoordinator
{
    public const int MaxMembers = 4;
    public const int MaxMailboxMessages = 32;
    public const int MaxMessageChars = 4_000;
    public const int MaxBoardItems = 32;
    public const int MaxBoardItemText = 4_000;
    public const int MaxCoordinatorPlanChars = 8_000;

    private readonly IBackgroundAgentManager _agents;
    private readonly IAgentTeamStore _store;
    private readonly string _projectRoot;
    private readonly ConcurrentDictionary<string, TeamEntry> _teams = new(StringComparer.OrdinalIgnoreCase);

    public AgentTeamCoordinator(
        IBackgroundAgentManager agents,
        IAgentTeamStore? store = null,
        string? projectRoot = null)
    {
        ArgumentNullException.ThrowIfNull(agents);
        _agents = agents;
        _store = store ?? NullAgentTeamStore.Instance;
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
    }

    public async Task<AgentTeam> StartAsync(
        string coordinator,
        IReadOnlyList<(string Description, string Prompt)> specialists,
        string? coordinatorPlan = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinator);
        ArgumentNullException.ThrowIfNull(specialists);
        if (coordinatorPlan?.Length > MaxCoordinatorPlanChars)
        {
            throw new ArgumentOutOfRangeException(nameof(coordinatorPlan), $"The coordinator plan cannot exceed {MaxCoordinatorPlanChars} characters.");
        }
        if (specialists.Count is < 1 or > MaxMembers)
        {
            throw new ArgumentOutOfRangeException(nameof(specialists), $"A team must have 1 to {MaxMembers} specialists.");
        }

        foreach (var specialist in specialists)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(specialist.Description);
            ArgumentException.ThrowIfNullOrWhiteSpace(specialist.Prompt);
        }

        var teamId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var members = specialists.Select(specialist => new AgentTeamMember(
            Guid.NewGuid().ToString("N"), specialist.Description, specialist.Prompt,
            AgentTeamMemberState.Pending)).ToArray();
        var entry = new TeamEntry(
            new AgentTeam(teamId, coordinator, AgentTeamState.Running, members, [], now, now,
                string.IsNullOrWhiteSpace(coordinatorPlan) ? null : coordinatorPlan.Trim(), []),
            new CancellationTokenSource(),
            null);
        _teams[teamId] = entry;

        try
        {
            var started = members.Select((member, index) =>
            {
                var task = _agents.Start(member.Description, member.Prompt);
                return (Member: member with { State = AgentTeamMemberState.Running, BackgroundTaskId = task.Id }, TaskId: task.Id, Index: index);
            }).ToArray();
            entry.MemberTaskIds = started.ToDictionary(item => item.Member.Id, item => item.TaskId);
            entry.Team = entry.Team with { Members = started.Select(item => item.Member).ToArray() };
            await PersistAsync(entry.Team, cancellationToken).ConfigureAwait(false);
            entry.Run = RunTeamAsync(entry);
            return entry.Team;
        }
        catch
        {
            await CancelEntryAsync(entry, CancellationToken.None).ConfigureAwait(false);
            _teams.TryRemove(teamId, out _);
            throw;
        }
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        foreach (var team in await _store.LoadAsync(_projectRoot, cancellationToken).ConfigureAwait(false))
        {
            var entry = new TeamEntry(team, new CancellationTokenSource(), null);
            if (_teams.TryAdd(team.Id, entry) && team.State == AgentTeamState.Running)
            {
                var tasks = _agents.List().ToDictionary(task => task.Id, StringComparer.OrdinalIgnoreCase);
                entry.MemberTaskIds = team.Members
                    .Where(member => !string.IsNullOrWhiteSpace(member.BackgroundTaskId))
                    .ToDictionary(member => member.Id, member => member.BackgroundTaskId!);
                var anotherProcessOwnsTask = entry.MemberTaskIds.Values
                    .Select(taskId => tasks.GetValueOrDefault(taskId))
                    .Any(task => task?.State == BackgroundAgentState.Running &&
                        task.LeaseExpiresAt > DateTimeOffset.UtcNow);
                if (entry.MemberTaskIds.Count == team.Members.Count && !anotherProcessOwnsTask)
                {
                    entry.Run = RunTeamAsync(entry);
                }
                else
                {
                    entry.Team = team with { State = AgentTeamState.Failed, UpdatedAt = DateTimeOffset.UtcNow };
                    await PersistAsync(entry.Team, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    public IReadOnlyList<AgentTeam> List() =>
        _teams.Values.Select(entry => entry.Team).OrderByDescending(team => team.UpdatedAt).ToArray();

    public AgentTeamBoardItem? AddBoardItem(string teamId, string text, string? assignedSpecialistId = null)
    {
        if (!_teams.TryGetValue(teamId, out var entry) ||
            string.IsNullOrWhiteSpace(text) || text.Trim().Length > MaxBoardItemText)
        {
            return null;
        }

        lock (entry.Gate)
        {
            var board = entry.Team.Board ?? [];
            if (board.Count >= MaxBoardItems)
            {
                return null;
            }

            var specialist = assignedSpecialistId?.Trim();
            if (specialist is not null && !entry.Team.Members.Any(member =>
                    string.Equals(member.Id, specialist, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var item = new AgentTeamBoardItem(
                Guid.NewGuid().ToString("N"), text.Trim(),
                specialist is null ? AgentTeamBoardItemState.Pending : AgentTeamBoardItemState.InProgress,
                specialist);
            entry.Team = entry.Team with
            {
                Board = [.. board, item],
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            _ = PersistAsync(entry.Team, CancellationToken.None);
            return item;
        }
    }

    public bool CompleteBoardItem(string teamId, string itemId)
    {
        if (!_teams.TryGetValue(teamId, out var entry) || string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        lock (entry.Gate)
        {
            var board = entry.Team.Board ?? [];
            var itemIndex = board.ToList().FindIndex(item =>
                string.Equals(item.Id, itemId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (itemIndex < 0 || board[itemIndex].State is AgentTeamBoardItemState.Completed or AgentTeamBoardItemState.Failed)
            {
                return false;
            }

            var updated = board.ToArray();
            updated[itemIndex] = updated[itemIndex] with
            {
                State = AgentTeamBoardItemState.Completed,
                CompletedAt = DateTimeOffset.UtcNow,
            };
            entry.Team = entry.Team with { Board = updated, UpdatedAt = DateTimeOffset.UtcNow };
            _ = PersistAsync(entry.Team, CancellationToken.None);
            return true;
        }
    }

    public bool Send(string teamId, string from, string message)
    {
        if (!_teams.TryGetValue(teamId, out var entry) ||
            string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(message) ||
            message.Length > MaxMessageChars)
        {
            return false;
        }

        lock (entry.Gate)
        {
            if (entry.Team.Mailbox.Count >= MaxMailboxMessages)
            {
                return false;
            }

            entry.Team = entry.Team with
            {
                Mailbox = [.. entry.Team.Mailbox, new AgentTeamMessage(from.Trim(), message.Trim(), DateTimeOffset.UtcNow)],
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            _ = PersistAsync(entry.Team, CancellationToken.None);
        }

        return true;
    }

    public async Task<bool> CancelAsync(string teamId, CancellationToken cancellationToken = default)
    {
        if (!_teams.TryGetValue(teamId, out var entry) || entry.Team.State != AgentTeamState.Running)
        {
            return false;
        }

        await CancelEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        var running = _teams.Values.Where(entry => entry.Team.State == AgentTeamState.Running).ToArray();
        foreach (var entry in running)
        {
            await CancelEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        var runs = running.Select(entry => entry.Run).Where(run => run is not null).Cast<Task>().ToArray();
        await Task.WhenAll(runs).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunTeamAsync(TeamEntry entry)
    {
        try
        {
            var results = await Task.WhenAll(entry.Team.Members.Select(async member =>
            {
                var result = await _agents.WaitAsync(entry.MemberTaskIds[member.Id], entry.Cancellation.Token)
                    .ConfigureAwait(false);
                return (Member: member, Result: result);
            })).ConfigureAwait(false);

            var members = results.Select(item => item.Member with
            {
                State = item.Result.State switch
                {
                    BackgroundAgentState.Completed => AgentTeamMemberState.Completed,
                    BackgroundAgentState.Cancelled => AgentTeamMemberState.Cancelled,
                    _ => AgentTeamMemberState.Failed,
                },
                Report = item.Result.Report,
            }).ToArray();
            var messages = results.Where(item => !string.IsNullOrWhiteSpace(item.Result.Report))
                .Select(item => new AgentTeamMessage(item.Member.Description, item.Result.Report!, DateTimeOffset.UtcNow))
                .Take(MaxMailboxMessages)
                .ToArray();
            var failedSpecialists = members
                .Where(member => member.State == AgentTeamMemberState.Failed)
                .Select(member => member.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var board = (entry.Team.Board ?? []).Select(item =>
                item.State is AgentTeamBoardItemState.Pending or AgentTeamBoardItemState.InProgress &&
                item.AssignedSpecialistId is not null && failedSpecialists.Contains(item.AssignedSpecialistId)
                    ? item with { State = AgentTeamBoardItemState.Failed }
                    : item).ToArray();
            entry.Team = entry.Team with
            {
                State = members.Any(member => member.State == AgentTeamMemberState.Failed)
                    ? AgentTeamState.Failed : AgentTeamState.Completed,
                Members = members,
                Mailbox = [.. entry.Team.Mailbox.Take(MaxMailboxMessages - messages.Length), .. messages],
                Board = board,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
        }
        catch (OperationCanceledException)
        {
            entry.Team = entry.Team with
            {
                State = AgentTeamState.Cancelled,
                Members = entry.Team.Members.Select(member => member with { State = AgentTeamMemberState.Cancelled }).ToArray(),
                UpdatedAt = DateTimeOffset.UtcNow,
            };
        }
        catch (Exception ex)
        {
            entry.Team = entry.Team with { State = AgentTeamState.Failed, Mailbox = AddMessage(entry.Team, "coordinator", ex.Message) };
        }
        finally
        {
            await PersistAsync(entry.Team, CancellationToken.None).ConfigureAwait(false);
            entry.Cancellation.Dispose();
        }
    }

    private async Task CancelEntryAsync(TeamEntry entry, CancellationToken cancellationToken)
    {
        entry.Cancellation.Cancel();
        foreach (var taskId in entry.MemberTaskIds.Values)
        {
            _agents.Cancel(taskId);
        }

        if (entry.Run is not null)
        {
            await entry.Run.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private Task PersistAsync(AgentTeam team, CancellationToken cancellationToken) =>
        PersistCoreAsync(team, cancellationToken);

    private async Task PersistCoreAsync(AgentTeam team, CancellationToken cancellationToken)
    {
        try { await _store.SaveAsync(_projectRoot, team, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { }
    }

    private static IReadOnlyList<AgentTeamMessage> AddMessage(AgentTeam team, string from, string body) =>
        [.. team.Mailbox.Take(MaxMailboxMessages - 1), new AgentTeamMessage(from, body[..Math.Min(body.Length, MaxMessageChars)], DateTimeOffset.UtcNow)];

    private sealed class TeamEntry(AgentTeam team, CancellationTokenSource cancellation, Task? run)
    {
        public readonly object Gate = new();
        public AgentTeam Team { get; set; } = team;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Dictionary<string, string> MemberTaskIds { get; set; } = new();
        public Task? Run { get; set; } = run;
    }
}
