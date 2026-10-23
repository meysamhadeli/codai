using FluentAssertions;

namespace Codai.Tools.Tests;

public sealed class ToolFileChangesTests
{
    [Theory]
    [InlineData("", "", 0, 0)]
    [InlineData("", "\n", 1, 0)]
    [InlineData("", "one\n\n", 2, 0)]
    [InlineData("one\n\n", "", 0, 2)]
    [InlineData("same\n", "same\n", 0, 0)]
    [InlineData("a\r\nb\r\n", "a\nb\n", 0, 0)]
    [InlineData("before\nold\nafter\n", "before\nnew\nafter\n", 1, 1)]
    [InlineData("old\nkeep\nold\n", "new\nkeep\nnew\n", 2, 2)]
    [InlineData("keep\n", "keep\nadded\n", 1, 0)]
    [InlineData("keep\ndrop\n", "keep\n", 0, 1)]
    [InlineData("value = 1;\n", "value = 2;\n", 1, 1)]
    [InlineData(" x\n", "x\n", 1, 1)]
    public void Counts_only_changed_lines(string before, string after, int added, int removed)
    {
        var change = ToolFileChanges.Describe("a.txt", before, after);

        change.Path.Should().Be("a.txt");
        change.AddedLines.Should().Be(added);
        change.RemovedLines.Should().Be(removed);
    }

    [Fact]
    public void Preview_is_bounded_without_truncating_counts()
    {
        var after = string.Join('\n', Enumerable.Range(1, 1000).Select(index => $"line {index}"));

        var change = ToolFileChanges.Describe("large.txt", "", after);

        change.AddedLines.Should().Be(1000);
        change.RemovedLines.Should().Be(0);
        change.Preview.Split('\n').Should().HaveCountLessThanOrEqualTo(201);
        change.Preview.Length.Should().BeLessThan(12100);
        change.Preview.Should().Contain("diff preview truncated").And.NotContain("+line 1000");
    }
}
