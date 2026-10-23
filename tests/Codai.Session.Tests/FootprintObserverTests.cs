using System.Text.Json;
using Codai.Core.Chat;
using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Session.Tests;

/// <summary>
/// Covers the record a compaction restores from being filled as tools run, and the reporting
/// it decorates still reaching the user.
/// </summary>
public class FootprintObserverTests
{
    [Fact]
    public void A_read_file_call_records_the_path()
    {
        var observer = Create();

        observer.OnToolResult("read_file", Arguments("""{"path":"src/Parser.cs"}"""), ToolResult.Success("class Parser { }"));

        observer.Footprint.Files.Should().Equal("src/Parser.cs");
    }

    [Fact]
    public void A_batch_read_records_every_path_with_the_last_one_first()
    {
        var observer = Create();

        observer.OnToolResult(
            "read_file",
            Arguments("""{"paths":["src/A.cs","src/B.cs","src/C.cs"]}"""),
            ToolResult.Success("contents"));

        observer.Footprint.Files.Should().Equal("src/C.cs", "src/B.cs", "src/A.cs");
    }

    [Fact]
    public void Writing_and_editing_a_file_counts_as_touching_it()
    {
        var observer = Create();

        observer.OnToolResult("write_file", Arguments("""{"path":"src/New.cs","content":"x"}"""), ToolResult.Success("Written."));
        observer.OnToolResult("edit_file", Arguments("""{"path":"src/Old.cs","old":"a","new":"b"}"""), ToolResult.Success("Edited."));

        observer.Footprint.Files.Should().Equal("src/Old.cs", "src/New.cs");
    }

    [Fact]
    public void A_tool_that_failed_touched_nothing()
    {
        var observer = Create();

        observer.OnToolResult("read_file", Arguments("""{"path":"src/Missing.cs"}"""), ToolResult.Failure("File not found."));

        observer.Footprint.Files.Should().BeEmpty();
    }

    [Fact]
    public void Reading_a_skill_records_the_skill_and_not_a_file()
    {
        var observer = Create();

        observer.OnToolResult("read_skill", Arguments("""{"name":"audit"}"""), ToolResult.Success("body"));

        observer.Footprint.Skills.Should().Equal("audit");
        observer.Footprint.Files.Should().BeEmpty();
    }

    [Fact]
    public void What_the_wrapped_observer_reports_still_reaches_it()
    {
        var inner = new RecordingObserver();
        var observer = new FootprintObserver(inner, new SessionFootprint());

        observer.OnToolResult("read_file", Arguments("""{"path":"src/Parser.cs"}"""), ToolResult.Success("x"));
        observer.OnToolCompleted("read_file", true, "read 1 file");

        inner.Results.Should().ContainSingle();
        inner.Completed.Should().Equal("read_file:True");
        observer.Inner.Should().BeSameAs(inner);
    }

    private static FootprintObserver Create() => new(new RecordingObserver(), new SessionFootprint());
    private static JsonElement Arguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class RecordingObserver : IToolObserver
    {
        public List<string> Results { get; } = [];

        public List<string> Completed { get; } = [];

        public void OnToolStarted(string toolName, string summary)
        {
        }

        public void OnToolCompleted(string toolName, bool succeeded, string detail) =>
            Completed.Add($"{toolName}:{succeeded}");

        public void OnToolDenied(string toolName, string summary)
        {
        }

        public void OnToolResult(string toolName, JsonElement arguments, ToolResult result) =>
            Results.Add(toolName);
    }
}
