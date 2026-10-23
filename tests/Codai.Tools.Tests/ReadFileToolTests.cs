using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Tools.Tests;

public class ReadFileToolTests
{
    private static readonly ReadFileTool Tool = new();

    private static InMemoryFileSystem ProjectWithFile(string relativePath, string contents) =>
        ToolTestHost.NewProject().AddFile(Path.Combine(ToolTestHost.Root, relativePath), contents);

    [Fact]
    public async Task Reads_multiple_files_in_order_with_common_line_window()
    {
        var files = ProjectWithFile("a.txt", "alpha\nbravo\ncharlie")
            .AddFile(Path.Combine(ToolTestHost.Root, "b.txt"), "one\ntwo\nthree");

        var result = await Tool.InvokeAsync(ToolTestHost.Args("""{"paths":["a.txt","b.txt"],"offset":2,"limit":1}"""),
            ToolTestHost.Context(files));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("a.txt (lines 2-2 of 3):").And.Contain("2\tbravo")
            .And.Contain("b.txt (lines 2-2 of 3):").And.Contain("2\ttwo");
        result.Content.IndexOf("a.txt", StringComparison.Ordinal).Should()
            .BeLessThan(result.Content.IndexOf("b.txt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Batch_errors_preserve_other_file_content_and_mark_failure()
    {
        var files = ProjectWithFile("a.txt", "visible")
            .AddFile(Path.Combine(ToolTestHost.Root, "b.bin"), "binary\0data");

        var result = await Tool.InvokeAsync(ToolTestHost.Args("""{"paths":["a.txt","missing.txt","b.bin"]}"""),
            ToolTestHost.Context(files));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("visible").And.Contain("[error]")
            .And.Contain("missing.txt").And.Contain("does not exist").And.Contain("binary");
    }

    [Theory]
    [InlineData("{\"paths\":[]}")]
    [InlineData("{\"paths\":\"a.txt\"}")]
    [InlineData("{\"paths\":[1]}")]
    [InlineData("{\"paths\":[\" \" ]}")]
    [InlineData("{\"paths\":[\"a.txt\",\"./a.txt\"]}")]
    [InlineData("{\"path\":\"a.txt\",\"paths\":[\"a.txt\"]}")]
    public async Task Invalid_batches_are_rejected(string arguments)
    {
        var result = await Tool.InvokeAsync(ToolTestHost.Args(arguments), ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Entire_batch_is_path_validated_before_reading()
    {
        var files = ProjectWithFile("a.txt", "must not read");
        var result = await Tool.InvokeAsync(ToolTestHost.Args("""{"paths":["a.txt","../outside.txt"]}"""),
            ToolTestHost.Context(files));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("outside the project").And.NotContain("does not exist");
        files.ReadPaths.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Long_lines_obey_result_budget_without_reading_later_batch_content(bool batch)
    {
        var files = ProjectWithFile("a.txt", new string('a', ToolRunner.DefaultMaxResultChars * 2))
            .AddFile(Path.Combine(ToolTestHost.Root, "b.txt"), "later sentinel");
        var arguments = batch ? """{"paths":["a.txt","b.txt"]}""" : """{"path":"a.txt"}""";

        var result = await Tool.InvokeAsync(ToolTestHost.Args(arguments), ToolTestHost.Context(files));

        result.IsSuccess.Should().BeTrue();
        result.Content.Length.Should().BeLessThanOrEqualTo(ToolRunner.DefaultMaxResultChars);
        result.Content.Should().Contain("output budget reached").And.NotContain("later sentinel");
        files.ReadPaths.Should().Equal(Path.Combine(ToolTestHost.Root, "a.txt"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oversized_paths_fail_before_any_read(bool batch)
    {
        var files = ToolTestHost.NewProject();
        var path = new string('a', ReadFileTool.MaxPathChars + 1);
        var arguments = batch ? $$"""{"paths":["{{path}}"]}""" : $$"""{"path":"{{path}}"}""";

        var result = await Tool.InvokeAsync(ToolTestHost.Args(arguments), ToolTestHost.Context(files));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("2048");
        files.ReadPaths.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ReadFileTool.MaxFileCount, true)]
    [InlineData(ReadFileTool.MaxFileCount + 1, false)]
    public async Task File_count_limit_is_enforced_before_reads(int count, bool valid)
    {
        var files = ToolTestHost.NewProject();
        var paths = Enumerable.Range(0, count).Select(index => $"{index}.txt").ToArray();
        foreach (var path in paths)
        {
            files.AddFile(Path.Combine(ToolTestHost.Root, path), string.Empty);
        }

        var arguments = System.Text.Json.JsonSerializer.SerializeToElement(new { paths });
        var result = await Tool.InvokeAsync(arguments, ToolTestHost.Context(files));

        result.IsSuccess.Should().Be(valid);
        files.ReadPaths.Count.Should().Be(valid ? count : 0);
    }

    [Fact]
    public async Task Oversized_batch_file_is_reported_without_read_and_other_files_continue()
    {
        var files = ProjectWithFile("large.txt", new string('x', (int)ReadFileTool.MaxFileSizeBytes + 1))
            .AddFile(Path.Combine(ToolTestHost.Root, "a.txt"), "visible");

        var result = await Tool.InvokeAsync(ToolTestHost.Args("""{"paths":["large.txt","a.txt"]}"""),
            ToolTestHost.Context(files));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("larger than 5 MB").And.Contain("visible");
        files.ReadPaths.Should().Equal(Path.Combine(ToolTestHost.Root, "a.txt"));
    }

    [Fact]
    public async Task Cancellation_between_files_prevents_next_read()
    {
        using var cancellation = new CancellationTokenSource();
        var files = ProjectWithFile("a.txt", "one").AddFile(Path.Combine(ToolTestHost.Root, "b.txt"), "two");
        files.AfterRead = _ => cancellation.Cancel();
        var read = () => Tool.InvokeAsync(ToolTestHost.Args("""{"paths":["a.txt","b.txt"]}"""),
            ToolTestHost.Context(files), cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
        files.ReadPaths.Should().Equal(Path.Combine(ToolTestHost.Root, "a.txt"));
    }

    [Fact]
    public async Task Batch_budget_is_shared_across_files_and_skips_unvisited_files()
    {
        var files = ProjectWithFile("a.txt", new string('a', 30_000))
            .AddFile(Path.Combine(ToolTestHost.Root, "b.txt"), new string('b', 40_000))
            .AddFile(Path.Combine(ToolTestHost.Root, "c.txt"), "must not read");

        var result = await Tool.InvokeAsync(ToolTestHost.Args("""{"paths":["a.txt","b.txt","c.txt"]}"""),
            ToolTestHost.Context(files));

        result.IsSuccess.Should().BeTrue();
        result.Content.Length.Should().BeLessThanOrEqualTo(ToolRunner.DefaultMaxResultChars);
        result.Content.Should().Contain("a.txt").And.Contain("b.txt").And.Contain("output budget reached")
            .And.NotContain("must not read");
        files.ReadPaths.Should().Equal(Path.Combine(ToolTestHost.Root, "a.txt"), Path.Combine(ToolTestHost.Root, "b.txt"));
    }

    [Fact]
    public async Task Unreadable_file_does_not_hide_later_successful_content()
    {
        var files = ProjectWithFile("a.txt", "unreadable").AddFile(Path.Combine(ToolTestHost.Root, "b.txt"), "visible");
        files.AfterRead = path =>
        {
            if (Path.GetFileName(path) == "a.txt")
            {
                throw new IOException("read unavailable");
            }
        };

        var result = await Tool.InvokeAsync(ToolTestHost.Args("""{"paths":["a.txt","b.txt"]}"""),
            ToolTestHost.Context(files));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("[error]").And.Contain("read unavailable").And.Contain("visible");
    }

    [Fact]
    public async Task Invalid_window_for_one_file_does_not_hide_other_file_window()
    {
        var files = ProjectWithFile("a.txt", "one").AddFile(Path.Combine(ToolTestHost.Root, "b.txt"), "one\ntwo");

        var result = await Tool.InvokeAsync(ToolTestHost.Args("""{"paths":["a.txt","b.txt"],"offset":2}"""),
            ToolTestHost.Context(files));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("past the end").And.Contain("b.txt (lines 2-2 of 2):").And.Contain("2\ttwo");
    }

    [Fact]
    public async Task Cancelled_batch_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var read = () => Tool.InvokeAsync(ToolTestHost.Args("""{"paths":["a.txt"]}"""),
            ToolTestHost.Context(ProjectWithFile("a.txt", "content")), cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Reads_a_file_with_line_numbers()
    {
        var fileSystem = ProjectWithFile("src/a.cs", "alpha\nbravo\ncharlie\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"src/a.cs"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeTrue();
        ToolTestHost.Lines(result.Content).Should().Equal(
            "src/a.cs (lines 1-3 of 3):",
            "1\talpha",
            "2\tbravo",
            "3\tcharlie");
    }

    [Fact]
    public async Task A_trailing_newline_does_not_add_a_line()
    {
        var fileSystem = ProjectWithFile("a.txt", "only\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.txt"}"""),
            ToolTestHost.Context(fileSystem));

        result.Content.Should().Contain("(lines 1-1 of 1):");
    }

    [Fact]
    public async Task Crlf_and_lf_files_number_identically()
    {
        var fileSystem = ProjectWithFile("crlf.txt", "one\r\ntwo\r\n");
        var lfSystem = ProjectWithFile("lf.txt", "one\ntwo\n");

        var crlf = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"crlf.txt"}"""),
            ToolTestHost.Context(fileSystem));

        var lf = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"lf.txt"}"""),
            ToolTestHost.Context(lfSystem));

        ToolTestHost.Lines(crlf.Content).Skip(1).Should().Equal(
            ToolTestHost.Lines(lf.Content).Skip(1));
    }

    [Fact]
    public async Task Offset_and_limit_select_a_window_and_report_the_rest()
    {
        var fileSystem = ProjectWithFile("a.txt", "1\n2\n3\n4\n5\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.txt","offset":2,"limit":2}"""),
            ToolTestHost.Context(fileSystem));

        ToolTestHost.Lines(result.Content).Should().Equal(
            "a.txt (lines 2-3 of 5):",
            "2\t2",
            "3\t3",
            "[... 2 more line(s); continue with offset=4]");
    }

    [Fact]
    public async Task Line_numbers_are_padded_to_the_width_of_the_total()
    {
        var contents = string.Join('\n', Enumerable.Range(1, 10)) + "\n";
        var fileSystem = ProjectWithFile("a.txt", contents);

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.txt","limit":1}"""),
            ToolTestHost.Context(fileSystem));

        result.Content.Should().Contain(" 1\t1");
    }

    [Fact]
    public async Task A_missing_path_argument_fails() =>
        (await Tool.InvokeAsync(
            ToolTestHost.Args("{}"),
            ToolTestHost.Context(ToolTestHost.NewProject())))
        .IsSuccess.Should().BeFalse();

    [Fact]
    public async Task A_missing_file_fails()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"nope.cs"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("does not exist");
    }

    [Fact]
    public async Task An_empty_file_is_reported_as_empty()
    {
        var fileSystem = ProjectWithFile("a.txt", string.Empty);

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.txt"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Be("a.txt is empty.");
    }

    [Fact]
    public async Task An_offset_past_the_end_fails_with_the_real_line_count()
    {
        var fileSystem = ProjectWithFile("a.txt", "1\n2\n3\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.txt","offset":10}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("past the end").And.Contain("3 line(s)");
    }

    [Fact]
    public async Task A_binary_file_is_rejected()
    {
        var fileSystem = ProjectWithFile("a.bin", "a\0b");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.bin"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("binary");
    }

    [Fact]
    public async Task Relative_escape_out_of_the_project_is_rejected()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"../outside.txt"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task An_absolute_path_outside_the_project_is_rejected()
    {
        var outside = Path.Combine(Path.GetTempPath(), "codai-tools-tests", "outside.txt");
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args($$"""{"path":"{{outside.Replace("\\", "\\\\")}}"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Declares_itself_read_only() => Tool.IsReadOnly.Should().BeTrue();

    [Fact]
    public void Schema_is_valid_json()
    {
        var schema = Tool.Schema;

        schema.GetProperty("oneOf")[0].GetProperty("required")[0].GetString().Should().Be("path");
        schema.GetProperty("oneOf")[1].GetProperty("required")[0].GetString().Should().Be("paths");
    }
}
