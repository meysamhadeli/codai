using Codai.Core.Context;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the token estimate. It only has to be conservative, so these tests pin the
/// properties that make it usable, not an exact number that no implementation could
/// guarantee.
/// </summary>
public class TokenEstimatorTests
{
    [Fact]
    public void Empty_input_costs_nothing()
    {
        TokenEstimator.FromCharacters(0).Should().Be(0);
        TokenEstimator.FromCharacters(-5).Should().Be(0);
        TokenEstimator.Estimate(null).Should().Be(0);
        TokenEstimator.Estimate(string.Empty).Should().Be(0);
    }

    [Fact]
    public void Rounds_up_so_the_estimate_is_never_an_understatement()
    {
        TokenEstimator.FromCharacters(1).Should().Be(1);
        TokenEstimator.FromCharacters(4).Should().Be(1);
        TokenEstimator.FromCharacters(5).Should().Be(2);
    }

    [Fact]
    public void A_longer_text_never_estimates_lower() =>
        TokenEstimator.Estimate(new string('x', 400))
            .Should().BeGreaterThan(TokenEstimator.Estimate(new string('x', 40)));

    [Fact]
    public void A_request_costs_more_than_the_sum_of_its_parts()
    {
        const string one = "aaaaaaaa";
        const string two = "bbbbbbbb";

        var parts = TokenEstimator.Estimate(one) + TokenEstimator.Estimate(two);
        var request = TokenEstimator.EstimateRequest(one, two);

        // The per-message and per-request overhead is the difference, and without it the
        // estimate would ignore the framing the provider adds to every message.
        request.Should().Be(parts + (2 * TokenEstimator.PerMessageOverhead) + TokenEstimator.PerRequestOverhead);
    }

    [Fact]
    public void Null_parts_are_skipped_rather_than_counted_as_a_message()
    {
        var withNull = TokenEstimator.EstimateRequest("aaaa", null);
        var withoutNull = TokenEstimator.EstimateRequest("aaaa");

        withNull.Should().Be(withoutNull);
    }

    [Fact]
    public void A_request_with_no_content_still_costs_the_request_overhead() =>
        TokenEstimator.EstimateRequest().Should().Be(TokenEstimator.PerRequestOverhead);
}
