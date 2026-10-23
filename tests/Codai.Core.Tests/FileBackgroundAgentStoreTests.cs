using Codai.Core.Agents;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

public sealed class FileBackgroundAgentStoreTests
{
    private const string Root = @"C:\project";

    [Fact]
    public async Task Saves_and_loads_background_results_by_id()
    {
        var fileSystem = new InMemoryFileSystem();
        var store = new FileBackgroundAgentStore(fileSystem);
        var task = new BackgroundAgentInfo(
            "task-1",
            "Inspect auth",
            BackgroundAgentState.Completed,
            "Found AuthService.cs.",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow);

        await store.SaveAsync(Root, task);

        var loaded = await store.LoadAsync(Root);
        loaded.Should().ContainSingle().Which.Should().Be(task);
    }
}