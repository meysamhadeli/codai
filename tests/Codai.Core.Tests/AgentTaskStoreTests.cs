using System.Text.Json;
using Codai.Core.Agents;
using FluentAssertions;

namespace Codai.Core.Tests;

public sealed class AgentTaskStoreTests
{
    [Fact]
    public async Task Legacy_ask_tasks_load_list_and_import_as_read_only_plans()
    {
        var fileSystem = new Fakes.InMemoryFileSystem();
        var store = new FileAgentTaskStore(fileSystem);
        const string root = @"C:\project";
        const string importRoot = @"C:\imported";
        const string exportPath = @"C:\exports\legacy.json";
        var task = AgentTask.Create("Explain the project").Complete();
        var json = JsonSerializer.Serialize(new AgentTaskExport(1, task));
        json = json.Replace("\"Mode\":2", "\"Mode\":0", StringComparison.Ordinal);
        fileSystem.AddFile(FileAgentTaskStore.GetPath(root), json);
        fileSystem.AddFile(FileAgentTaskStore.GetTaskPath(root, task.Id), json);
        fileSystem.AddFile(exportPath, json);

        (await store.LoadAsync(root))!.Mode.Should().Be(AgentMode.Plan);
        (await store.ListAsync(root)).Should().ContainSingle().Which.Mode.Should().Be(AgentMode.Plan);
        await store.ImportAsync(importRoot, exportPath);
        (await store.LoadAsync(importRoot))!.Mode.Should().Be(AgentMode.Plan);
    }

    [Fact]
    public async Task Export_and_import_round_trip_versioned_metadata_only()
    {
        var fileSystem = new Fakes.InMemoryFileSystem();
        var store = new FileAgentTaskStore(fileSystem);
        var task = AgentTask.Create("inspect the project").Start().Complete();
        var root = @"C:\project";
        var exportPath = @"C:\exports\task.json";

        await store.SaveAsync(root, task);
        await store.ExportAsync(root, task.Id, exportPath);

        using var document = JsonDocument.Parse(fileSystem.Files[exportPath.Replace('\\', '/')]);
        document.RootElement.GetProperty("SchemaVersion").GetInt32()
            .Should().Be(FileAgentTaskStore.CurrentExportSchemaVersion);
        document.RootElement.GetProperty("Task").GetProperty("Id").GetString().Should().Be(task.Id);
        document.RootElement.TryGetProperty("Turns", out _).Should().BeFalse();

        var importedRoot = @"C:\other-project";
        await store.ImportAsync(importedRoot, exportPath);

        (await store.LoadAsync(importedRoot)).Should().BeEquivalentTo(task);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"SchemaVersion\":99,\"Task\":{}}")]
    [InlineData("{\"SchemaVersion\":1,\"Task\":null}")]
    public async Task Import_rejects_corrupt_or_incompatible_envelopes(string contents)
    {
        var fileSystem = new Fakes.InMemoryFileSystem();
        var store = new FileAgentTaskStore(fileSystem);
        var exportPath = @"C:\exports\bad.json";
        fileSystem.AddFile(exportPath, contents);

        var act = () => store.ImportAsync(@"C:\project", exportPath);

        await act.Should().ThrowAsync<InvalidDataException>();
        (await store.LoadAsync(@"C:\project")).Should().BeNull();
    }
}