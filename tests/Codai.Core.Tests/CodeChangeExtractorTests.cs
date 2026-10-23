using Codai.Core.Diffs;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Ports of the original Go extractor tests so the accepted response formats
/// stay byte-compatible after the rewrite.
/// </summary>
public class CodeChangeExtractorTests
{
    private readonly CodeChangeExtractor _extractor = new();

    [Fact]
    public void Extracts_two_files()
    {
        const string text = "File: test.go\n```go\npackage main\n```\nFile: test2.go\n```go\npackage main\n```";

        var changes = _extractor.Extract(text);

        changes.Should().HaveCount(2);
        changes[0].RelativePath.Should().Be("test.go");
        changes[0].Code.Should().Be("package main");
        changes[1].RelativePath.Should().Be("test2.go");
        changes[1].Code.Should().Be("package main");
    }

    [Fact]
    public void Extracts_file_path_with_directory()
    {
        const string text = "File: tests/fakes/Foo1.cs\n```go\npackage main\n```";

        var changes = _extractor.Extract(text);

        changes.Should().ContainSingle();
        changes[0].RelativePath.Should().Be("tests/fakes/Foo1.cs");
    }

    [Fact]
    public void Extracts_file_path_with_number_and_dot_prefix()
    {
        const string text = "1. tests/fakes/Foo1.cs\n```go\npackage main\n```";

        var changes = _extractor.Extract(text);

        changes.Should().ContainSingle();
        changes[0].RelativePath.Should().Be("tests/fakes/Foo1.cs");
    }

    [Fact]
    public void Extracts_file_path_surrounded_by_markup()
    {
        const string text = "\n\n#####File: test.go#####\n```go\npackage main\n```\nFile: test2.go\n```go\npackage main\n```";

        var changes = _extractor.Extract(text);

        changes.Should().HaveCount(2);
        changes[0].RelativePath.Should().Be("test.go");
        changes[1].RelativePath.Should().Be("test2.go");
    }

    [Fact]
    public void Extracts_lowercase_file_label_without_space()
    {
        const string text = "file:test.go\n```go\npackage main\n```\nFile: test2.go\n```go\npackage main\n```";

        var changes = _extractor.Extract(text);

        changes.Should().HaveCount(2);
        changes[0].RelativePath.Should().Be("test.go");
        changes[1].RelativePath.Should().Be("test2.go");
    }

    [Fact]
    public void Extracts_path_from_numbered_heading_and_ignores_trailing_slash_segment()
    {
        const string text = "### 5. Direction.cs/nssss\n```csharp\npublic class Direction {}\n```";

        var changes = _extractor.Extract(text);

        changes.Should().ContainSingle();
        changes[0].RelativePath.Should().Be("Direction.cs");
        changes[0].Code.Should().Be("public class Direction {}");
    }

    [Fact]
    public void Extracts_path_from_file_heading_and_ignores_trailing_slash_segment()
    {
        const string text = "## File: Direction.cs/nssss\n```csharp\npublic class Direction {}\n```";

        var changes = _extractor.Extract(text);

        changes.Should().ContainSingle();
        changes[0].RelativePath.Should().Be("Direction.cs");
    }

    [Fact]
    public void Ignores_unsupported_colon_prefix()
    {
        const string text = "## test: Direction.cs/nssss\n```csharp\npublic class Direction {}\n```";

        var changes = _extractor.Extract(text);

        changes.Should().BeEmpty();
    }

    [Fact]
    public void Ignores_unsupported_dot_prefix()
    {
        const string text = "someCharacter. Direction.cs/nssss\n```csharp\npublic class Direction {}\n```";

        var changes = _extractor.Extract(text);

        changes.Should().BeEmpty();
    }

    [Fact]
    public void Returns_empty_when_no_code_blocks()
    {
        var changes = _extractor.Extract("File: test.go\nFile: test2.go\n");

        changes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Random text without any file paths or code blocks.")]
    public void Returns_empty_for_unusable_input(string text)
    {
        _extractor.Extract(text).Should().BeEmpty();
    }

    [Fact]
    public void Keeps_only_first_code_block_for_dat_same_file()
    {
        const string text = "File: test.go\n```go\npackage main\n```\n```go\nfunc main() {}\n```";

        var changes = _extractor.Extract(text);

        changes.Should().ContainSingle();
        changes[0].Code.Should().Be("package main");
    }

    [Fact]
    public void Extracts_markdown_body_without_fences()
    {
        const string text = "File: README.md\n```markdown\n# Title\n\n- one\n- two\n```";

        var changes = _extractor.Extract(text);

        changes.Should().ContainSingle();
        changes[0].RelativePath.Should().Be("README.md");
        changes[0].IsTextFile.Should().BeTrue();
        changes[0].Code.Should().Be("# Title\n\n- one\n- two");
    }

    [Fact]
    public void Removes_only_the_outer_fences_of_a_text_body()
    {
        // Nested fences are preserved; only the first and last fence lines are dropped.
        const string text = "File: guide.md\n```markdown\n# Title\n\n```bash\necho hi\n```\n```";

        var changes = _extractor.Extract(text);

        changes.Should().ContainSingle();
        changes[0].Code.Should().Be("# Title\n\n```bash\necho hi\n```");
    }

    [Fact]
    public void Marks_text_files_case_insensitively()
    {
        var changes = _extractor.Extract("File: NOTES.TXT\n```text\nhello\n```");

        changes.Should().ContainSingle();
        changes[0].IsTextFile.Should().BeTrue();
    }

    [Fact]
    public void Marks_code_files_as_non_text()
    {
        var changes = _extractor.Extract("File: Program.cs\n```csharp\nclass Program {}\n```");

        changes.Should().ContainSingle();
        changes[0].IsTextFile.Should().BeFalse();
    }
}
