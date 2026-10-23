using Codai.Core.Agents;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

public sealed class AgentTeamStoreTests
{
    [Fact]
    public async Task Persists_bounded_team_mailbox_and_specialist_identity()
    {
        var fileSystem = new InMemoryFileSystem();
        var store = new FileAgentTeamStore(fileSystem);
        var team = new AgentTeam(
            "team-1",
            "coordinator",
            AgentTeamState.Running,
            [new AgentTeamMember("member-1", "auth", "Inspect auth", AgentTeamMemberState.Running, BackgroundTaskId: "task-1")],
            [new AgentTeamMessage("user", "Check tokens", DateTimeOffset.UtcNow)],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "Review auth first.",
            [new AgentTeamBoardItem("item-1", "Inspect auth", AgentTeamBoardItemState.InProgress, "member-1")]);

        await store.SaveAsync(@"C:\project", team);

        (await store.LoadAsync(@"C:\project")).Should().ContainSingle().Which.Should().BeEquivalentTo(team);
    }
}
