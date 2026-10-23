using Codai.Core.Chat;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the record a compaction restores from: what it keeps, in what order, and what it
/// forgets.
/// </summary>
public class SessionFootprintTests
{
    [Fact]
    public void The_most_recently_touched_file_comes_first()
    {
        var footprint = new SessionFootprint();

        footprint.RecordFile("src/A.cs");
        footprint.RecordFile("src/B.cs");

        footprint.Files.Should().Equal("src/B.cs", "src/A.cs");
    }

    [Fact]
    public void Touching_a_file_again_moves_it_to_the_front_instead_of_duplicating_it()
    {
        var footprint = new SessionFootprint();

        footprint.RecordFile("src/A.cs");
        footprint.RecordFile("src/B.cs");
        footprint.RecordFile("SRC/a.cs");

        footprint.Files.Should().Equal("SRC/a.cs", "src/B.cs");
    }

    [Fact]
    public void A_blank_path_is_not_recorded()
    {
        var footprint = new SessionFootprint();

        footprint.RecordFile("   ");
        footprint.RecordFile(null);

        footprint.Files.Should().BeEmpty();
    }

    [Fact]
    public void The_oldest_path_is_forgotten_once_the_capacity_is_reached()
    {
        var footprint = new SessionFootprint(capacity: 2);

        footprint.RecordFile("src/A.cs");
        footprint.RecordFile("src/B.cs");
        footprint.RecordFile("src/C.cs");

        footprint.Files.Should().Equal("src/C.cs", "src/B.cs");
    }

    [Fact]
    public void Skills_are_kept_apart_from_files()
    {
        var footprint = new SessionFootprint();

        footprint.RecordFile("src/A.cs");
        footprint.RecordSkill("audit");

        footprint.Files.Should().Equal("src/A.cs");
        footprint.Skills.Should().Equal("audit");
    }

    [Fact]
    public void Clearing_forgets_everything()
    {
        var footprint = new SessionFootprint();

        footprint.RecordFile("src/A.cs");
        footprint.RecordSkill("audit");
        footprint.Clear();

        footprint.Files.Should().BeEmpty();
        footprint.Skills.Should().BeEmpty();
    }

    [Fact]
    public void What_a_caller_read_stays_as_it_was_read()
    {
        var footprint = new SessionFootprint();
        footprint.RecordFile("src/A.cs");

        var files = footprint.Files;
        footprint.RecordFile("src/B.cs");

        files.Should().Equal("src/A.cs");
        footprint.Files.Should().Equal("src/B.cs", "src/A.cs");
    }
}
