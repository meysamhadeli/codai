using Codai.Core.Customization;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers discovery of the <c>:commands</c> a project keeps in <c>.codai/commands</c>.
/// </summary>
public class CommandCatalogTests
{
    private const string Root = @"C:\project";

    private static InMemoryFileSystem Project() => new InMemoryFileSystem().AddDirectory(Root);

    private static string CommandPath(string fileName) =>
        Path.Combine(Root, ".codai", "commands", fileName);

    [Fact]
    public async Task A_project_without_the_directory_has_no_commands()
    {
        var catalog = new CommandCatalog(Project());

        (await catalog.LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_file_name_names_the_command_and_the_body_is_the_prompt()
    {
        var fileSystem = Project().AddFile(
            CommandPath("Review-Diff.md"),
            "---\ndescription: Reviews a diff\n---\nReview the working tree.");

        var command = (await new CommandCatalog(fileSystem).LoadAsync(Root)).Should().ContainSingle().Subject;

        command.Name.Should().Be("review-diff");
        command.Description.Should().Be("Reviews a diff");
        command.Body.Should().Be("Review the working tree.");
        command.SourcePath.Should().EndWith("Review-Diff.md");
    }

    [Fact]
    public async Task The_argument_hint_is_read_from_front_matter()
    {
        var fileSystem = Project().AddFile(
            CommandPath("review.md"),
            "---\nargument-hint: <path>\n---\nBody.");

        var command = (await new CommandCatalog(fileSystem).LoadAsync(Root)).Single();

        command.ArgumentHint.Should().Be("<path>");
    }

    [Fact]
    public async Task Without_a_description_the_first_line_of_the_body_is_used()
    {
        var fileSystem = Project().AddFile(
            CommandPath("summary.md"),
            "# Write a summary\n\nDetails follow.");

        var command = (await new CommandCatalog(fileSystem).LoadAsync(Root)).Single();

        command.Description.Should().Be("Write a summary");
        command.ArgumentHint.Should().BeEmpty();
    }

    [Fact]
    public async Task A_very_long_description_is_truncated_because_it_reaches_the_prompt()
    {
        var fileSystem = Project().AddFile(
            CommandPath("long.md"),
            "---\ndescription: " + new string('x', 400) + "\n---\nBody.");

        var command = (await new CommandCatalog(fileSystem).LoadAsync(Root)).Single();

        command.Description.Should().EndWith("…");
        command.Description.Length.Should().BeLessThanOrEqualTo(MarkdownSummary.MaxLength + 1);
    }

    [Fact]
    public async Task A_file_with_no_body_is_ignored()
    {
        var fileSystem = Project().AddFile(CommandPath("empty.md"), "---\ndescription: x\n---\n");

        (await new CommandCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task Only_markdown_files_are_commands()
    {
        var fileSystem = Project()
            .AddFile(CommandPath("review.md"), "Body.")
            .AddFile(CommandPath("notes.txt"), "Body.");

        (await new CommandCatalog(fileSystem).LoadAsync(Root))
            .Should().ContainSingle().Which.Name.Should().Be("review");
    }

    [Theory]
    [InlineData("has space.md")]
    [InlineData("-starts-with-a-dash.md")]
    [InlineData(".hidden.md")]
    public async Task A_name_that_is_not_usable_is_ignored_rather_than_exposed(string fileName)
    {
        var fileSystem = Project().AddFile(CommandPath(fileName), "Body.");

        (await new CommandCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task Commands_are_sorted_by_name()
    {
        var fileSystem = Project()
            .AddFile(CommandPath("zebra.md"), "Body.")
            .AddFile(CommandPath("alpha.md"), "Body.");

        (await new CommandCatalog(fileSystem).LoadAsync(Root))
            .Select(command => command.Name)
            .Should().Equal("alpha", "zebra");
    }

    [Fact]
    public async Task Find_is_case_insensitive_and_returns_null_for_an_unknown_name()
    {
        var fileSystem = Project().AddFile(CommandPath("review.md"), "Body.");
        var catalog = new CommandCatalog(fileSystem);

        (await catalog.FindAsync(Root, "REVIEW")).Should().NotBeNull();
        (await catalog.FindAsync(Root, "nope")).Should().BeNull();
    }

    [Fact]
    public async Task A_command_added_mid_session_is_picked_up_without_a_restart()
    {
        // Commands are re-read on every load, so a file that appears after the first load
        // is usable on the next line rather than requiring a new session.
        var fileSystem = Project();
        var catalog = new CommandCatalog(fileSystem);

        (await catalog.LoadAsync(Root)).Should().BeEmpty();

        fileSystem.AddFile(CommandPath("review.md"), "Body.");

        (await catalog.LoadAsync(Root)).Should().ContainSingle();
    }
}
