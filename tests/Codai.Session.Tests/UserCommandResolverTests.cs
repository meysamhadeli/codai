using Codai.Core.Customization;
using Codai.Session.Tests.Fakes;
using FluentAssertions;

namespace Codai.Session.Tests;

/// <summary>
/// Covers resolution of the <c>:commands</c> a project defines. The rule under test is
/// that an unrecognised colon line is reported, never forwarded to the model as a request.
/// </summary>
public class UserCommandResolverTests
{
    private const string Root = @"C:\project";

    private static string CommandPath(string fileName) =>
        Path.Combine(Root, ".codai", "commands", fileName);

    private static (UserCommandResolver Resolver, InMemoryFileSystem FileSystem) Build()
    {
        var fileSystem = new InMemoryFileSystem(Root);
        var options = new SessionOptions
        {
            ProjectRoot = Root,
            Provider = "openai",
            Model = "gpt-4o",
        };

        return (new UserCommandResolver(
            new CommandCatalog(fileSystem),
            new SkillCatalog(fileSystem),
            options), fileSystem);
    }

    [Fact]
    public async Task Expands_a_project_command_into_the_request_it_stands_for()
    {
        var (resolver, fileSystem) = Build();
        fileSystem.AddFile(
            CommandPath("review.md"),
            "---\ndescription: Reviews a change\n---\nReview the working tree. Focus on $ARGUMENTS");

        var expansion = await resolver.ExpandAsync(":review src/a.cs");

        expansion.Should().NotBeNull();
        expansion!.Request.Should().Be("Review the working tree. Focus on src/a.cs");
        expansion.SourcePath.Should().EndWith("review.md");
    }

    [Fact]
    public async Task A_command_name_is_matched_ignoring_case()
    {
        var (resolver, fileSystem) = Build();
        fileSystem.AddFile(CommandPath("review.md"), "Review it.");

        (await resolver.ExpandAsync(":REVIEW"))!.Request.Should().Be("Review it.");
    }

    [Fact]
    public async Task A_colon_line_that_names_nothing_returns_null_rather_than_reaching_the_model()
    {
        var (resolver, fileSystem) = Build();
        fileSystem.AddFile(CommandPath("review.md"), "Review it.");

        (await resolver.ExpandAsync(":nope")).Should().BeNull();

        // The colon is the point: a typo in a command name must not be answered as if the
        // user had asked a question with that text.
        (await resolver.ExpandAsync("nope")).Should().BeNull();
    }

    [Fact]
    public async Task A_project_with_no_commands_resolves_nothing()
    {
        var (resolver, _) = Build();

        (await resolver.ExpandAsync(":review")).Should().BeNull();
        (await resolver.NamesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task The_available_names_are_reported_with_their_colon()
    {
        var (resolver, fileSystem) = Build();
        fileSystem.AddFile(CommandPath("review.md"), "Review it.");
        fileSystem.AddFile(CommandPath("migrate.md"), "Migrate it.");

        (await resolver.NamesAsync()).Should().Equal(":migrate", ":review");
    }

    [Fact]
    public async Task A_command_added_mid_session_works_on_the_next_line()
    {
        var (resolver, fileSystem) = Build();

        (await resolver.ExpandAsync(":review")).Should().BeNull();

        fileSystem.AddFile(CommandPath("review.md"), "Review it.");

        (await resolver.ExpandAsync(":review"))!.Request.Should().Be("Review it.");
    }

    [Fact]
    public async Task Slash_skill_invocation_loads_skill_and_appends_request()
    {
        var (resolver, fileSystem) = Build();
        fileSystem.AddFile(
            Path.Combine(Root, ".codai", "skills", "review", "SKILL.md"),
            "---\ndescription: Review code\n---\nReview carefully.");

        var expansion = await resolver.ExpandAsync("/review src/a.cs");

        expansion.Should().NotBeNull();
        expansion!.IsSkill.Should().BeTrue();
        expansion.Request.Should().Contain("Review carefully.").And.Contain("src/a.cs");
    }

    [Fact]
    public async Task Project_command_wins_over_skill_with_same_name()
    {
        var (resolver, fileSystem) = Build();
        fileSystem.AddFile(CommandPath("review.md"), "Use command workflow.");
        fileSystem.AddFile(
            Path.Combine(Root, ".codai", "skills", "review", "SKILL.md"),
            "Use skill workflow.");

        var expansion = await resolver.ExpandAsync(":review");

        expansion.Should().NotBeNull();
        expansion!.IsSkill.Should().BeFalse();
        expansion.Request.Should().Be("Use command workflow.");
    }
}
