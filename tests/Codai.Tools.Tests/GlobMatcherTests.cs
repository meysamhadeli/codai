using FluentAssertions;

namespace Codai.Tools.Tests;

public class GlobMatcherTests
{
    [Theory]
    [InlineData("a.cs", "**/*.cs")]
    [InlineData("src/a.cs", "**/*.cs")]
    [InlineData("src/deep/nested/a.cs", "**/*.cs")]
    [InlineData("src/a.cs", "src/*.cs")]
    [InlineData("src/a.cs", "**/a.cs")]
    [InlineData("a.cs", "*.cs")]
    public void IsMatch_matches_expected_paths(string path, string pattern) =>
        GlobMatcher.IsMatch(path, pattern).Should().BeTrue();

    [Theory]
    [InlineData("src/a.cs", "*.cs", "a single star must not cross a directory separator")]
    [InlineData("src/deep/a.cs", "src/*.cs", "a single star must not cross a directory separator")]
    [InlineData("a.txt", "*.cs", "the extension must match")]
    [InlineData("a.cs", "src/*.cs", "the directory prefix must match")]
    public void IsMatch_rejects_expected_paths(string path, string pattern, string because) =>
        GlobMatcher.IsMatch(path, pattern).Should().BeFalse(because);

    [Fact]
    public void DoubleStar_slash_also_matches_zero_directories() =>
        GlobMatcher.IsMatch("a.cs", "**/*.cs").Should().BeTrue();

    [Fact]
    public void Question_mark_matches_exactly_one_character()
    {
        GlobMatcher.IsMatch("a.cs", "?.cs").Should().BeTrue();
        GlobMatcher.IsMatch("ab.cs", "?.cs").Should().BeFalse();
    }

    [Fact]
    public void Braces_expand_to_an_alternation()
    {
        GlobMatcher.IsMatch("a.cs", "*.{cs,vb}").Should().BeTrue();
        GlobMatcher.IsMatch("a.vb", "*.{cs,vb}").Should().BeTrue();
        GlobMatcher.IsMatch("a.fs", "*.{cs,vb}").Should().BeFalse();
    }

    [Fact]
    public void Character_classes_are_honoured()
    {
        GlobMatcher.IsMatch("a.cs", "[ab].cs").Should().BeTrue();
        GlobMatcher.IsMatch("c.cs", "[ab].cs").Should().BeFalse();
    }

    [Fact]
    public void Regex_metacharacters_in_the_pattern_are_literal()
    {
        GlobMatcher.IsMatch("a+b.cs", "a+b.cs").Should().BeTrue();
        GlobMatcher.IsMatch("aab.cs", "a+b.cs").Should().BeFalse();
    }

    [Fact]
    public void IsMatchAny_returns_true_when_one_pattern_matches() =>
        GlobMatcher.IsMatchAny("src/a.cs", ["*.vb", "**/*.cs"]).Should().BeTrue();

    [Fact]
    public void IsMatchAny_returns_false_for_an_empty_pattern_list() =>
        GlobMatcher.IsMatchAny("src/a.cs", []).Should().BeFalse();

    [Fact]
    public void Backslashes_are_treated_as_separators() =>
        GlobMatcher.IsMatch(@"src\a.cs", "src/*.cs").Should().BeTrue();

    [Fact]
    public void PathsEqual_ignores_separator_and_case_differences()
    {
        GlobMatcher.PathsEqual("src/a.cs", @"src\a.cs").Should().BeTrue();
        GlobMatcher.PathsEqual("src/A.cs", "src/a.cs").Should().BeTrue();
        GlobMatcher.PathsEqual("src/a.cs", "src/b.cs").Should().BeFalse();
    }
}
