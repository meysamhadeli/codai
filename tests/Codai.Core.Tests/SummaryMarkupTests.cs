using Codai.Core.Chat;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the unwrapping of a summariser response. Every path has to end in usable text:
/// throwing a summary away because it arrived in the wrong wrapper leaves the
/// conversation over its budget for no good reason.
/// </summary>
public class SummaryMarkupTests
{
    [Fact]
    public void The_summary_block_is_what_is_kept()
    {
        SummaryMarkup.Extract("<analysis>thinking</analysis>\n<summary>what mattered</summary>")
            .Should()
            .Be("what mattered");
    }

    [Fact]
    public void An_unwrapped_answer_is_kept_as_it_is()
    {
        SummaryMarkup.Extract("what mattered").Should().Be("what mattered");
    }

    [Fact]
    public void A_response_with_no_summary_block_loses_its_analysis()
    {
        // The scratch work is what compaction exists to remove: keeping it would put the
        // conversation's reasoning back into every following request.
        SummaryMarkup.Extract("<analysis>thinking</analysis>what mattered")
            .Should()
            .Be("what mattered");
    }

    [Fact]
    public void The_markup_is_matched_whatever_its_case()
    {
        SummaryMarkup.Extract("<ANALYSIS>thinking</ANALYSIS><Summary>what mattered</SUMMARY>")
            .Should()
            .Be("what mattered");
    }

    [Fact]
    public void An_unclosed_summary_block_still_yields_its_text()
    {
        SummaryMarkup.Extract("<summary>what mattered").Should().Be("what mattered");
    }

    [Fact]
    public void An_unclosed_analysis_block_keeps_what_came_before_it()
    {
        SummaryMarkup.Extract("what mattered\n<analysis>thinking").Should().Be("what mattered");
    }

    [Fact]
    public void Quotation_marks_around_a_one_line_summary_are_not_part_of_it()
    {
        SummaryMarkup.Extract("\"what mattered\"").Should().Be("what mattered");
    }

    [Fact]
    public void A_summary_that_only_starts_with_a_quote_keeps_it()
    {
        // Both ends have to be quotes before anything is stripped. Trimming a quote the
        // model used to mark the user's own words would corrupt a real sentence.
        const string quoted = "\"fix the build\"\nand then commit";

        SummaryMarkup.Extract(quoted).Should().Be(quoted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<analysis>thinking only</analysis>")]
    public void Nothing_usable_comes_back_as_null(string? response)
    {
        SummaryMarkup.Extract(response).Should().BeNull();
    }
}
