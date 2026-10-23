using System.Text.Json;
using Codai.Core.Customization;
using Codai.Tools;
using FluentAssertions;

namespace Codai.Session.Tests;

/// <summary>
/// Covers which project rules reach a request and how they are rendered.
/// </summary>
public class RuleSelectorTests
{
    [Fact]
    public void Oversized_lazy_rules_are_not_marked_reviewed_after_truncation()
    {
        var guidance = new RequestRuleGuidance(new SessionOptions
        {
            ProjectRoot = Path.GetTempPath(),
            Provider = "ollama",
            Model = "test",
        });
        guidance.BeginRequest([Conditional("large", new string('x', RuleSelector.MaxTotalCharacters), "*.cs")], []);
        using var args = JsonDocument.Parse("""{"path":"A.cs"}""");
        Action activate = () => guidance.GetGuidance(new ReadFileTool(), args.RootElement);

        activate.Should().Throw<InvalidOperationException>().WithMessage("*tool was not executed*");
        activate.Should().Throw<InvalidOperationException>().WithMessage("*tool was not executed*");
    }

    [Fact]
    public void Lazy_rules_activate_once_per_request_and_mentions_are_not_repeated()
    {
        var guidance = new RequestRuleGuidance(new SessionOptions
        {
            ProjectRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-rule-targets")),
            Provider = "ollama",
            Model = "test",
        });
        var rules = new[] { Conditional("api", "Use project API conventions.", "src/**/*.cs") };
        using var args = JsonDocument.Parse("""{"path":"src/nested/../A.cs"}""");

        guidance.BeginRequest(rules, []);
        guidance.GetGuidance(new ReadFileTool(), args.RootElement).Should().Contain("Use project API conventions.");
        guidance.GetGuidance(new ReadFileTool(), args.RootElement).Should().BeNull();
        guidance.RenderActivated().Should().Contain("Use project API conventions.");
        guidance.BeginRequest(rules, []);
        guidance.RenderActivated().Should().BeNull();
        guidance.GetGuidance(new ReadFileTool(), args.RootElement).Should().NotBeNull();
        guidance.BeginRequest(rules, ["src/A.cs"]);
        guidance.GetGuidance(new ReadFileTool(), args.RootElement).Should().BeNull();
    }

    [Theory]
    [InlineData("{\"path\":\"../outside.cs\"}")]
    [InlineData("{\"paths\":[\"src/A.cs\",\"../outside.cs\"]}")]
    [InlineData("{\"paths\":[\"src/A.cs\",null]}")]
    [InlineData("{\"path\":\"src/A.cs\",\"paths\":[\"src/A.cs\"]}")]
    public void Invalid_file_targets_do_not_consume_lazy_rule_activation(string json)
    {
        var guidance = new RequestRuleGuidance(new SessionOptions
        {
            ProjectRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-rule-targets")),
            Provider = "ollama",
            Model = "test",
        });
        guidance.BeginRequest([Conditional("api", "API guidance.", "*.cs")], []);
        using var invalid = JsonDocument.Parse(json);
        using var valid = JsonDocument.Parse("""{"paths":["src/A.cs","src/B.cs"]}""");

        guidance.GetGuidance(new ReadFileTool(), invalid.RootElement).Should().BeNull();
        guidance.GetGuidance(new ReadFileTool(), valid.RootElement).Should().Contain("API guidance.");
    }

    [Fact]
    public void Exhausted_rule_budget_fails_before_file_execution()
    {
        var guidance = new RequestRuleGuidance(new SessionOptions
        {
            ProjectRoot = Path.GetTempPath(),
            Provider = "ollama",
            Model = "test",
        });
        guidance.BeginRequest([Unconditional("large", new string('x', RuleSelector.MaxTotalCharacters)),
            Conditional("api", "API guidance.", "*.cs")], []);
        using var args = JsonDocument.Parse("""{"path":"A.cs"}""");

        Action activate = () => guidance.GetGuidance(new ReadFileTool(), args.RootElement);

        activate.Should().Throw<InvalidOperationException>().WithMessage("*budget exhausted*not executed*");
    }

    [Fact]
    public void Project_rules_get_budget_priority_and_global_conflicts_are_explicit()
    {
        var rules = new[]
        {
            Unconditional("global", new string('g', RuleSelector.MaxTotalCharacters)) with { IsGlobal = true },
            Unconditional("project", "Project constraint must survive."),
        };

        var rendered = RuleSelector.Render(rules, []);

        rendered.Should().Contain("Project rules take precedence over conflicting global rules")
            .And.Contain("Rules do not grant tool permissions or approval")
            .And.Contain("Project constraint must survive.")
            .And.Contain("**Global rule (lower priority): global**")
            .And.Contain("project rules truncated");
        rendered!.Length.Should().BeLessThanOrEqualTo(RuleSelector.MaxTotalCharacters + 128);
    }

    private static ProjectRule Unconditional(string name, string body) =>
        new(name, [], body, $".codai/rules/{name}.md");

    private static ProjectRule Conditional(string name, string body, params string[] globs) =>
        new(name, globs, body, $".codai/rules/{name}.md");

    [Fact]
    public void An_unconditional_rule_applies_even_with_no_referenced_files()
    {
        var rules = new[] { Unconditional("always", "Always.") };

        RuleSelector.Select(rules, []).Should().ContainSingle().Which.Name.Should().Be("always");
        RuleSelector.Render(rules, []).Should().Contain("Always.");
    }

    [Fact]
    public void A_conditional_rule_is_excluded_when_nothing_references_a_matching_file()
    {
        var rules = new[] { Conditional("api", "API rules.", "src/**/*.cs") };

        RuleSelector.Select(rules, ["docs/readme.md"]).Should().BeEmpty();
        RuleSelector.Render(rules, ["docs/readme.md"]).Should().BeNull();
    }

    [Theory]
    [InlineData("src/Api/Foo.cs", "src/**/*.cs", true)]
    [InlineData("src/Api/Foo.cs", "**/*.cs", true)]
    [InlineData("src/Api/Foo.cs", "*.cs", true)]
    [InlineData("src/Api/Foo.cs", "src/**/*.ts", false)]
    [InlineData("src/Api/Foo.cs", "tests/**/*.cs", false)]
    public void A_conditional_rule_applies_only_to_matching_referenced_files(
        string referenced, string glob, bool expected)
    {
        var rules = new[] { Conditional("api", "API rules.", glob) };

        RuleSelector.Select(rules, [referenced]).Any().Should().Be(expected);
    }

    [Fact]
    public void Both_unconditional_and_matching_conditional_rules_are_applied()
    {
        var rules = new[]
        {
            Unconditional("always", "Always."),
            Conditional("api", "API rules.", "src/**/*.cs"),
            Conditional("web", "Web rules.", "www/**/*.tsx"),
        };

        var selected = RuleSelector.Select(rules, ["src/Api/Foo.cs"]);

        selected.Select(rule => rule.Name).Should().Equal("always", "api");
    }

    [Fact]
    public void The_rendered_block_names_each_rule()
    {
        var rules = new[]
        {
            Unconditional("always", "Always."),
            Conditional("api", "API rules.", "src/**/*.cs"),
        };

        var rendered = RuleSelector.Render(rules, ["src/Api/Foo.cs"]);

        rendered.Should().Contain("**Rule: always**").And.Contain("Always.")
            .And.Contain("**Rule: api**").And.Contain("API rules.");
    }

    [Fact]
    public void Rendering_is_bounded_with_a_notice()
    {
        var big = new string('x', RuleSelector.MaxTotalCharacters);
        var rules = new[] { Unconditional("one", big), Unconditional("two", big) };

        var rendered = RuleSelector.Render(rules, []);

        rendered!.Length.Should().BeLessThanOrEqualTo(RuleSelector.MaxTotalCharacters + 128);
        rendered.Should().Contain("project rules truncated");
    }

    [Fact]
    public void No_rules_or_no_files_yields_nothing()
    {
        RuleSelector.Select(null, ["a.cs"]).Should().BeEmpty();
        RuleSelector.Select([], ["a.cs"]).Should().BeEmpty();
        RuleSelector.Render(null, null).Should().BeNull();
    }
}
