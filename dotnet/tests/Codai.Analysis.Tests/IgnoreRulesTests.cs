using Codai.Analysis;
using FluentAssertions;

namespace Codai.Analysis.Tests;

public class LanguageIdTests
{
    [Theory]
    [InlineData("Program.cs", "csharp")]
    [InlineData("main.go", "go")]
    [InlineData("app.ts", "typescript")]
    [InlineData("app.tsx", "typescript")]
    [InlineData("app.js", "javascript")]
    [InlineData("app.jsx", "javascript")]
    [InlineData("script.py", "python")]
    [InlineData("Main.java", "java")]
    public void Detects_language_from_extension(string path, string expected)
    {
        LanguageId.DetectFromPath(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("data.json")]
    [InlineData("noextension")]
    public void Returns_null_for_unsupported_files(string path)
    {
        LanguageId.DetectFromPath(path).Should().BeNull();
    }
}

public class IgnoreRulesTests
{
    [Theory]
    [InlineData("node_modules")]
    [InlineData("src/node_modules")]
    [InlineData("src/bin/Debug")]
    [InlineData("obj")]
    [InlineData("dist")]
    [InlineData(".git/config")]
    [InlineData(".idea/workspace.xml")]
    [InlineData(".vscode/settings.json")]
    [InlineData("app.log")]
    [InlineData("image.PNG")]
    [InlineData("archive.exe")]
    public void Skips_builtin_ignored_paths(string path)
    {
        IgnoreRules.IsDefaultIgnored(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("src/Program.cs")]
    [InlineData("README.md")]
    [InlineData("src/services/OrderService.cs")]
    public void Keeps_normal_source_paths(string path)
    {
        IgnoreRules.IsDefaultIgnored(path).Should().BeFalse();
    }

    [Fact]
    public void Parses_patterns_skipping_comments_and_blanks()
    {
        const string contents = """
            # comment

            secrets/
            *.secret
            """;

        var patterns = IgnoreRules.ParsePatterns(contents);

        patterns.Should().BeEquivalentTo(["secrets/", "*.secret"]);
    }

    [Fact]
    public void Drops_user_patterns_that_duplicate_builtins()
    {
        IgnoreRules.ParsePatterns("node_modules\n").Should().BeEmpty();
    }

    [Fact]
    public void Matches_directory_patterns_by_prefix()
    {
        IgnoreRules.IsIgnoredByPatterns("secrets/token.txt", ["secrets/"]).Should().BeTrue();
        IgnoreRules.IsIgnoredByPatterns("src/secrets/token.txt", ["secrets/"]).Should().BeFalse();
    }

    [Fact]
    public void Matches_glob_patterns()
    {
        IgnoreRules.IsIgnoredByPatterns("a.snapshot", ["*.snapshot"]).Should().BeTrue();
    }

    [Fact]
    public void Glob_star_does_not_cross_directories()
    {
        IgnoreRules.GlobMatch("*.cs", "src/Program.cs").Should().BeFalse();
        IgnoreRules.GlobMatch("*.cs", "Program.cs").Should().BeTrue();
    }

    [Fact]
    public void Glob_question_mark_matches_single_character()
    {
        IgnoreRules.GlobMatch("file?.txt", "file1.txt").Should().BeTrue();
        IgnoreRules.GlobMatch("file?.txt", "file12.txt").Should().BeFalse();
    }
}
