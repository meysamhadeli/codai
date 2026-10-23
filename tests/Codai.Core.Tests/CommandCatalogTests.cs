using Codai.Core.Abstractions;
using Codai.Core.Customization;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers discovery of the <c>:commands</c> a project keeps in <c>.codai/commands</c>.
/// </summary>
public class CommandCatalogTests
{
    [Fact]
    public async Task Identical_global_and_project_workflow_roots_keep_project_origin()
    {
        var fileSystem = Project().AddFile(CommandPath("review.md"), "Project workflow.");
        var catalog = new CommandCatalog(fileSystem, Path.Combine(Root, ".codai", "."));

        var commands = await catalog.LoadAsync(Root);

        commands.Should().ContainSingle().Which.IsGlobal.Should().BeFalse();
        commands[0].SourcePath.Should().Be(".codai/commands/review.md");
        fileSystem.ReadPaths.Should().ContainSingle().Which.Should()
            .Be(InMemoryFileSystem.Normalize(CommandPath("review.md")));
        (await catalog.FindAsync(Root, "review"))!.Body.Should().Be("Project workflow.");
    }

    [Fact]
    public async Task Empty_project_workflow_does_not_hide_valid_global_fallback()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-workflows"));
        var fileSystem = Project().AddFile(CommandPath("review.md"), "---\ndescription: Empty\n---\n")
            .AddFile(Path.Combine(global, "commands", "review.md"), "Global workflow.");
        var catalog = new CommandCatalog(fileSystem, global);

        var commands = await catalog.LoadAsync(Root);

        commands.Should().ContainSingle().Which.IsGlobal.Should().BeTrue();
        (await catalog.FindAsync(Root, "review"))!.Body.Should().Be("Global workflow.");
    }

    [Fact]
    public async Task Combined_workflow_limit_reserves_project_entries_and_cancellation_is_not_swallowed()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-workflows"));
        var fileSystem = Project().AddFile(CommandPath("project.md"), "Project.");
        for (var index = 0; index < CommandCatalog.MaxCommands; index++)
        {
            fileSystem.AddFile(Path.Combine(global, "commands", $"global-{index:D3}.md"), "Global.");
        }

        var catalog = new CommandCatalog(fileSystem, global);
        var commands = await catalog.LoadAsync(Root);
        commands.Should().HaveCount(CommandCatalog.MaxCommands);
        commands.Should().Contain(command => command.Name == "project" && !command.IsGlobal);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> load = () => catalog.LoadAsync(Root, cancellation.Token);
        await load.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Global_workflows_are_discovered_but_project_names_override_case_insensitively()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-workflows"));
        var fileSystem = Project().AddFile(CommandPath("REVIEW.md"), "Project $ARGUMENTS")
            .AddFile(Path.Combine(global, "commands", "review.md"), "Shadowed")
            .AddFile(Path.Combine(global, "commands", "ship.md"), "Global $ARGUMENTS");
        var catalog = new CommandCatalog(fileSystem, global);

        var commands = await catalog.LoadAsync(Root);

        commands.Select(command => command.Name).Should().Equal("review", "ship");
        commands[0].IsGlobal.Should().BeFalse();
        commands[1].IsGlobal.Should().BeTrue();
        (await catalog.FindAsync(Root, "REVIEW"))!.Body.Should().Be("Project $ARGUMENTS");
        (await catalog.FindAsync(Root, "ship"))!.SourcePath.Should()
            .Be(Path.Combine(global, "commands", "ship.md").Replace('\\', '/'));
    }

    [Fact]
    public async Task Global_workflow_fallback_works_without_project_directory_and_requires_opt_in()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-workflows"));
        var fileSystem = Project().AddFile(Path.Combine(global, "commands", "review.md"), "Global.");

        (await new CommandCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
        (await new CommandCatalog(fileSystem, global).LoadAsync(Root)).Should().ContainSingle();
    }

    [Fact]
    public async Task Plugin_commands_are_available_alongside_project_commands()
    {
        var provider = new TestPluginCommandProvider(
            new CommandDefinition("format", "Format files", string.Empty, "Format the files.", "plugin:format"));

        var catalog = new CommandCatalog(Project(), pluginProviders: [provider]);
        var commands = await catalog.LoadAsync(Root);

        commands.Should().ContainSingle().Which.Name.Should().Be("format");
        (await catalog.FindAsync(Root, "format"))!.SourcePath.Should().Be("plugin:format");
    }

    private const string Root = @"C:\project";

    private static InMemoryFileSystem Project() => new InMemoryFileSystem().AddDirectory(Root);

    private static string CommandPath(string fileName) =>
        Path.Combine(Root, ".codai", "commands", fileName);

    private sealed class TestPluginCommandProvider(CommandDefinition command) : IPluginCommandProvider
    {
        public IReadOnlyList<CommandDefinition> Commands { get; } = [command];
    }

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
