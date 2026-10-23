using System.Text.Json;
using FluentAssertions;

namespace Codai.Tools.Tests;

public sealed class ApplyPatchToolTests
{
    [Fact]
    public async Task Updates_file_from_a_context_hunk()
    {
        var files = ToolTestHost.NewProject().AddFile(
            Path.Combine(ToolTestHost.Root, "a.txt"), "before\nkeep\nafter\n");
        var patch = """
            *** Begin Patch
            *** Update File: a.txt
            @@
             before
            -keep
            +changed
             after
            *** End Patch
            """;

        var result = await new ApplyPatchTool().InvokeAsync(
            ToolTestHost.Args($$"""{"patch":{{JsonSerializer.Serialize(patch)}}}"""),
            ToolTestHost.Context(files));

        result.IsSuccess.Should().BeTrue();
        result.FileChanges.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Path = "a.txt", AddedLines = 1, RemovedLines = 1 });
        (await files.ReadAllTextAsync(Path.Combine(ToolTestHost.Root, "a.txt")))
            .Should().Be("before\nchanged\nafter\n");
    }

    [Fact]
    public async Task Rejects_stale_hunk_without_writing()
    {
        var files = ToolTestHost.NewProject().AddFile(Path.Combine(ToolTestHost.Root, "a.txt"), "current\n");
        var patch = """
            *** Begin Patch
            *** Update File: a.txt
            @@
            -old
            +new
            *** End Patch
            """;

        var result = await new ApplyPatchTool().InvokeAsync(
            ToolTestHost.Args($$"""{"patch":{{JsonSerializer.Serialize(patch)}}}"""),
            ToolTestHost.Context(files));

        result.IsSuccess.Should().BeFalse();
        result.FileChanges.Should().BeEmpty();
        (await files.ReadAllTextAsync(Path.Combine(ToolTestHost.Root, "a.txt"))).Should().Be("current\n");
    }

    [Fact]
    public async Task Adds_and_deletes_files()
    {
        var files = ToolTestHost.NewProject().AddFile(Path.Combine(ToolTestHost.Root, "old.txt"), "old");
        var patch = """
            *** Begin Patch
            *** Add File: new.txt
            +new
            *** Delete File: old.txt
            *** End Patch
            """;

        var result = await new ApplyPatchTool().InvokeAsync(
            ToolTestHost.Args($$"""{"patch":{{JsonSerializer.Serialize(patch)}}}"""),
            ToolTestHost.Context(files));

        result.IsSuccess.Should().BeTrue();
        result.FileChanges.Should().HaveCount(2);
        result.FileChanges[0].Should().BeEquivalentTo(new { Path = "new.txt", AddedLines = 1, RemovedLines = 0 });
        result.FileChanges[1].Should().BeEquivalentTo(new { Path = "old.txt", AddedLines = 0, RemovedLines = 1 });
        files.FileExists(Path.Combine(ToolTestHost.Root, "new.txt")).Should().BeTrue();
        files.FileExists(Path.Combine(ToolTestHost.Root, "old.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Partial_failure_reports_only_changes_that_reached_disk()
    {
        var files = ToolTestHost.NewProject();
        var patch = """
            *** Begin Patch
            *** Add File: made.txt
            +landed
            *** Delete File: missing.txt
            *** End Patch
            """;
        var result = await new ApplyPatchTool().InvokeAsync(
            ToolTestHost.Args(JsonSerializer.Serialize(new { patch })), ToolTestHost.Context(files));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("Added made.txt").And.Contain("does not exist");
        result.FileChanges.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Path = "made.txt", AddedLines = 1, RemovedLines = 0 });
        (await files.ReadAllTextAsync(Path.Combine(ToolTestHost.Root, "made.txt"))).TrimEnd('\r', '\n').Should().Be("landed");
    }
}