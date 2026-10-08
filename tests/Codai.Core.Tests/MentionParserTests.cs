using Codai.Core.Prompting;
using FluentAssertions;

namespace Codai.Core.Tests;

public class MentionParserTests
{
    [Fact]
    public void Returns_no_mentions_for_plain_text()
    {
        MentionParser.Parse("please refactor the parser").Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Returns_no_mentions_for_empty_input(string? input)
    {
        MentionParser.Parse(input).Should().BeEmpty();
    }

    [Fact]
    public void Finds_a_single_mention()
    {
        MentionParser.Parse("look at @src/Program.cs").Should().Equal("src/Program.cs");
    }

    [Fact]
    public void Finds_several_mentions_in_order()
    {
        MentionParser.Parse("@src/a.cs and @src/b.cs then @docs/")
            .Should().Equal("src/a.cs", "src/b.cs", "docs/");
    }

    [Fact]
    public void Finds_a_mention_at_the_start_of_the_line()
    {
        MentionParser.Parse("@README.md summarize this").Should().Equal("README.md");
    }

    [Fact]
    public void Drops_trailing_sentence_punctuation()
    {
        MentionParser.Parse("check @src/a.cs, then @src/b.cs.").Should().Equal("src/a.cs", "src/b.cs.");
    }

    [Fact]
    public void Drops_a_closing_parenthesis()
    {
        MentionParser.Parse("(see @src/a.cs)").Should().Equal("src/a.cs");
    }

    [Fact]
    public void Ignores_email_addresses()
    {
        MentionParser.Parse("mail me at someone@example.com").Should().BeEmpty();
    }

    [Fact]
    public void Deduplicates_mentions_ignoring_case()
    {
        MentionParser.Parse("@src/A.cs and @src/a.cs").Should().Equal("src/A.cs");
    }

    [Fact]
    public void Keeps_an_at_sign_that_starts_nothing()
    {
        MentionParser.Parse("use @ alone").Should().BeEmpty();
    }

    [Fact]
    public void Keeps_glob_mentions_intact()
    {
        MentionParser.Parse("review @src/**/*.cs please").Should().Equal("src/**/*.cs");
    }
}
