using Codai.Core.Customization;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers discovery of the rules a project keeps in <c>.codai/rules</c>.
/// </summary>
public class ProjectRuleCatalogTests
{
    [Fact]
    public async Task Global_rules_combine_with_project_rules_and_preserve_origin()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-rules"));
        var globalPath = Path.Combine(global, "rules", "personal.md");
        var fileSystem = Project().AddFile(RulePath("team.md"), "Project guidance.")
            .AddFile(globalPath, "Personal guidance.");

        var rules = await new ProjectRuleCatalog(fileSystem, global).LoadAsync(Root);

        rules.Select(rule => rule.Body).Should().Equal("Project guidance.", "Personal guidance.");
        rules[0].IsGlobal.Should().BeFalse();
        rules[1].IsGlobal.Should().BeTrue();
        rules[1].SourcePath.Should().Be(Path.GetFullPath(globalPath).Replace('\\', '/'));
    }

    [Fact]
    public async Task Project_rule_overrides_global_rule_with_same_relative_path_but_not_same_basename()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-rules"));
        var fileSystem = Project().AddFile(RulePath("style.md"), "Project style.")
            .AddFile(Path.Combine(global, "rules", "style.md"), "Shadowed global style.")
            .AddFile(Path.Combine(global, "rules", "nested", "style.md"), "Nested global style.");

        var rules = await new ProjectRuleCatalog(fileSystem, global).LoadAsync(Root);

        rules.Select(rule => rule.Body).Should().Equal("Project style.", "Nested global style.");
    }

    [Fact]
    public async Task Project_rule_directory_is_discovered()
    {
        var fileSystem = Project()
            .AddFile(Path.Combine(Root, ".codai", "rules", "coding.md"), "Project rule.")
            .AddFile(Path.Combine(Root, ".codai", "rules", "testing.md"), "Project testing rule.");

        var rules = await new ProjectRuleCatalog(fileSystem).LoadAsync(Root);

        rules.Select(rule => rule.Name).Should().Equal("coding", "testing");
    }

    [Fact]
    public async Task Global_discovery_is_opt_in_for_library_callers()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-rules"));
        var fileSystem = Project().AddFile(Path.Combine(global, "rules", "personal.md"), "Not opted in.");

        (await new ProjectRuleCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task One_directory_used_as_both_global_and_project_is_not_duplicated()
    {
        var fileSystem = Project().AddFile(RulePath("style.md"), "Only once.");

        var rules = await new ProjectRuleCatalog(fileSystem, Path.Combine(Root, ".codai")).LoadAsync(Root);

        rules.Should().ContainSingle().Which.IsGlobal.Should().BeFalse();
    }

    [Fact]
    public async Task Global_rules_cannot_displace_project_rules_or_exceed_combined_count_limit()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-rules"));
        var fileSystem = Project();
        for (var index = 0; index < ProjectRuleCatalog.MaxRules; index++)
        {
            fileSystem.AddFile(Path.Combine(global, "rules", $"global-{index:D3}.md"), "Global.");
        }

        fileSystem.AddFile(RulePath("project.md"), "Project takes priority.");

        var rules = await new ProjectRuleCatalog(fileSystem, global).LoadAsync(Root);

        rules.Should().HaveCount(ProjectRuleCatalog.MaxRules);
        rules[0].Body.Should().Be("Project takes priority.");
    }

    private const string Root = @"C:\project";

    private static InMemoryFileSystem Project() => new InMemoryFileSystem().AddDirectory(Root);

    private static string RulePath(string name) =>
        Path.Combine(Root, ".codai", "rules", name);

    [Fact]
    public async Task A_project_without_the_directory_has_no_rules()
    {
        var catalog = new ProjectRuleCatalog(Project());

        (await catalog.LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_without_front_matter_is_an_unconditional_rule()
    {
        var fileSystem = Project().AddFile(RulePath("style.md"), "Prefer records.");

        var rule = (await new ProjectRuleCatalog(fileSystem).LoadAsync(Root)).Should().ContainSingle().Subject;

        rule.Name.Should().Be("style");
        rule.IsConditional.Should().BeFalse();
        rule.Body.Should().Be("Prefer records.");
        rule.SourcePath.Should().Be(".codai/rules/style.md");
    }

    [Theory]
    [InlineData("src/**/*.cs, src/**/*.ts")]
    [InlineData("src/**/*.cs;src/**/*.ts")]
    [InlineData("src/**/*.cs src/**/*.ts")]
    public async Task ApplyTo_splits_on_commas_semicolons_and_whitespace(string value)
    {
        var fileSystem = Project().AddFile(
            RulePath("api.md"),
            $"---\napplyTo: \"{value}\"\n---\nUse ProblemDetails.");

        var rule = (await new ProjectRuleCatalog(fileSystem).LoadAsync(Root)).Single();

        rule.IsConditional.Should().BeTrue();
        rule.ApplyTo.Should().Equal("src/**/*.cs", "src/**/*.ts");
    }

    [Fact]
    public async Task Paths_frontmatter_supports_yaml_lists()
    {
        var fileSystem = Project().AddFile(
            Path.Combine(Root, ".codai", "rules", "api.md"),
            "---\npaths:\n  - src/**/*.cs\n  - src/**/*.ts\n---\nUse ProblemDetails.");

        var rule = (await new ProjectRuleCatalog(fileSystem).LoadAsync(Root)).Single();

        rule.ApplyTo.Should().Equal("src/**/*.cs", "src/**/*.ts");
    }

    [Fact]
    public async Task Disabled_rules_are_not_loaded()
    {
        var fileSystem = Project()
            .AddFile(RulePath("style.md"), "Style.")
            .AddFile(RulePath("testing.md"), "Testing.")
            .AddFile(Path.Combine(Root, ".codai", "rules.json"), "{\"disabled\":[\"STYLE\"]}");

        var rules = await new ProjectRuleCatalog(fileSystem).LoadAsync(Root);

        rules.Should().ContainSingle().Which.Name.Should().Be("testing");
    }

    [Fact]
    public async Task An_empty_applyTo_is_treated_as_unconditional()
    {
        var fileSystem = Project().AddFile(RulePath("x.md"), "---\napplyTo:\n---\nAlways.");

        (await new ProjectRuleCatalog(fileSystem).LoadAsync(Root)).Single().IsConditional.Should().BeFalse();
    }

    [Fact]
    public async Task A_rule_with_no_body_is_skipped()
    {
        var fileSystem = Project().AddFile(RulePath("empty.md"), "---\napplyTo: src/**\n---\n");

        (await new ProjectRuleCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task Non_markdown_files_are_ignored()
    {
        var fileSystem = Project().AddFile(RulePath("notes.txt"), "not a rule");

        (await new ProjectRuleCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task Nested_folders_are_searched_and_results_are_sorted_by_path()
    {
        var fileSystem = Project()
            .AddFile(RulePath("b.md"), "B")
            .AddFile(Path.Combine(Root, ".codai", "rules", "nested", "a.md"), "A");

        var rules = await new ProjectRuleCatalog(fileSystem).LoadAsync(Root);

        rules.Select(rule => rule.SourcePath).Should().Equal(".codai/rules/b.md", ".codai/rules/nested/a.md");
    }

    [Fact]
    public async Task A_body_past_the_limit_is_truncated()
    {
        var body = new string('x', ProjectRuleCatalog.MaxCharacters + 100);
        var fileSystem = Project().AddFile(RulePath("big.md"), body);

        var rule = (await new ProjectRuleCatalog(fileSystem).LoadAsync(Root)).Single();

        rule.Body.Length.Should().BeLessThanOrEqualTo(ProjectRuleCatalog.MaxCharacters + 64);
        rule.Body.Should().Contain("rule truncated");
    }

    [Fact]
    public void Over_long_and_duplicate_patterns_are_dropped()
    {
        var longPattern = new string('a', ProjectRuleCatalog.MaxPatternLength + 1);

        ProjectRuleCatalog.ParsePatterns($"src/**, {longPattern}, SRC/**, src/**")
            .Should().Equal("src/**");
    }

    [Fact]
    public async Task Cancellation_is_observed()
    {
        var fileSystem = Project().AddFile(RulePath("style.md"), "Prefer records.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var load = () => new ProjectRuleCatalog(fileSystem).LoadAsync(Root, cancellation.Token);

        await load.Should().ThrowAsync<OperationCanceledException>();
    }
}
