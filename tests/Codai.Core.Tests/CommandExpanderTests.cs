using Codai.Core.Customization;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers placeholder substitution in a project command's body.
/// </summary>
public class CommandExpanderTests
{
    private static CommandDefinition Definition(string body) =>
        new("review", "Reviews something.", "<path>", body, @".codai/commands/review.md");

    [Fact]
    public void Substitutes_the_whole_argument_string_for_the_placeholder() =>
        CommandExpander
            .Expand(Definition($"Review this: {CommandExpander.ArgumentsPlaceholder}"), "src/a.cs src/b.cs")
            .Should().Be("Review this: src/a.cs src/b.cs");

    [Fact]
    public void Substitutes_positional_arguments_individually() =>
        CommandExpander
            .Expand(Definition("Compare $1 with $2"), "left right")
            .Should().Be("Compare left with right");

    [Fact]
    public void An_absent_positional_argument_becomes_empty_rather_than_literal() =>
        CommandExpander
            .Expand(Definition("Compare $1 with $2"), "left")
            .Should().Be("Compare left with");

    [Fact]
    public void The_placeholder_is_matched_ignoring_case() =>
        CommandExpander
            .Expand(Definition("Do it: $arguments"), "now")
            .Should().Be("Do it: now");

    [Fact]
    public void A_body_without_a_placeholder_still_receives_the_arguments()
    {
        // The alternative is silently dropping what the user typed, which looks like the
        // command ignoring the file it was given.
        CommandExpander
            .Expand(Definition("Review the working tree."), "src/a.cs")
            .Should().Be("Review the working tree.\n\nsrc/a.cs");
    }

    [Fact]
    public void A_body_without_a_placeholder_and_no_arguments_is_unchanged() =>
        CommandExpander
            .Expand(Definition("Review the working tree."), null)
            .Should().Be("Review the working tree.");

    [Fact]
    public void A_dollar_sign_that_is_not_a_placeholder_survives() =>
        CommandExpander
            .Expand(Definition("Cost is $0 and $NAME stays"), "unused")
            .Should().Be("Cost is $0 and $NAME stays\n\nunused");

    [Fact]
    public void The_expansion_is_trimmed() =>
        CommandExpander
            .Expand(Definition("  Body  "), null)
            .Should().Be("Body");
}
