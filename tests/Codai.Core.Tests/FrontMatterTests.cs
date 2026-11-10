using Codai.Core.Customization;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the deliberately small front-matter reader.
/// </summary>
public class FrontMatterTests
{
    [Theory]
    [InlineData(">", "Concise answers with full accuracy.\n")]
    [InlineData(">-", "Concise answers with full accuracy.")]
    [InlineData("|", "Concise answers\nwith full accuracy.\n")]
    [InlineData("|-", "Concise answers\nwith full accuracy.")]
    public void Reads_multiline_yaml_scalars_without_exposing_block_markers(string marker, string expected)
    {
        var (fields, body) = FrontMatter.Parse($"---\nname: caveman\ndescription: {marker}\n  Concise answers\n  with full accuracy.\nargument-hint: <path>\n---\nKeep technical substance.");

        fields["description"].Should().Be(expected);
        fields["name"].Should().Be("caveman");
        fields["argument-hint"].Should().Be("<path>");
        fields.Should().HaveCount(3);
        body.Should().Be("Keep technical substance.");
    }

    [Fact]
    public void Multiline_colons_and_nested_metadata_do_not_become_top_level_fields()
    {
        var (fields, body) = FrontMatter.Parse(
            "---\ndescription: >-\n  Use: retain technical accuracy.\n  Fold this line.\nallowed-tools:\n  - read_file\n  - glob\nmetadata:\n  label: ignored\n---\nBody.");

        fields["description"].Should().Be("Use: retain technical accuracy. Fold this line.");
        fields["allowed-tools"].Should().Be("read_file, glob");
        fields.Should().HaveCount(2).And.NotContainKey("Use").And.NotContainKey("label");
        body.Should().Be("Body.");
    }

    [Fact]
    public void Reads_the_keys_before_the_second_delimiter()
    {
        var (fields, body) = FrontMatter.Parse(
            """
            ---
            description: Reviews a diff
            argument-hint: <path>
            ---
            Body text here.
            """);

        fields.Should().Contain("description", "Reviews a diff");
        fields.Should().Contain("argument-hint", "<path>");
        body.Should().Be("Body text here.");
    }

    [Fact]
    public void Keys_are_looked_up_ignoring_case()
    {
        var (fields, _) = FrontMatter.Parse(
            """
            ---
            Description: x
            ---
            body
            """);

        fields.Should().ContainKey("description");
        fields["description"].Should().Be("x");
    }

    [Fact]
    public void Quotes_around_a_value_are_removed()
    {
        var (fields, _) = FrontMatter.Parse(
            """
            ---
            description: "quoted value"
            argumenthint: 'single'
            ---
            body
            """);

        fields["description"].Should().Be("quoted value");
        fields["argumenthint"].Should().Be("single");
    }

    [Fact]
    public void Blank_lines_and_comments_are_skipped()
    {
        var (fields, _) = FrontMatter.Parse(
            """
            ---

            # a comment
            description: value
            ---
            body
            """);

        fields.Should().HaveCount(1);
        fields["description"].Should().Be("value");
    }

    [Fact]
    public void Content_without_front_matter_is_all_body()
    {
        var (fields, body) = FrontMatter.Parse("# Heading\n\nText.");

        fields.Should().BeEmpty();
        body.Should().Be("# Heading\n\nText.");
    }

    [Fact]
    public void An_unterminated_block_is_treated_as_no_front_matter()
    {
        // Treating it as front matter would swallow the whole file and lose the prompt.
        var (fields, body) = FrontMatter.Parse("---\ndescription: x\n\nText without a closing rule.");

        fields.Should().BeEmpty();
        body.Should().Contain("Text without a closing rule.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_input_produces_nothing(string? input)
    {
        var (fields, body) = FrontMatter.Parse(input);

        fields.Should().BeEmpty();
        body.Should().BeEmpty();
    }

    [Fact]
    public void Carriage_returns_do_not_leak_into_the_keys_or_the_body()
    {
        var (fields, body) = FrontMatter.Parse("---\r\ndescription: x\r\n---\r\nbody\r\n");

        fields["description"].Should().Be("x");
        body.Should().StartWith("body");
    }

    [Fact]
    public void A_line_that_is_not_a_key_value_pair_is_skipped_rather_than_swallowing_the_block()
    {
        var (fields, body) = FrontMatter.Parse("---\njust a sentence\ndescription: kept\n---\nbody");

        fields.Should().ContainKey("description");
        fields.Should().NotContainKey("just a sentence");
        body.Should().Be("body");
    }
}
