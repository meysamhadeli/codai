using Codai.Core.Diffs;
using Codai.Core.IO;
using Codai.Core.Models;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

public class DiffApplierTests
{
    private static DiffApplier CreateApplier() => new(PhysicalFileSystem.Instance);

    [Fact]
    public async Task Creates_a_new_file()
    {
        using var temp = new TempDirectory();
        var applier = CreateApplier();

        var result = await applier.ApplyAsync(
            temp.Path,
            new CodeChange("newfile.go", "package main\nfunc main() {}"));

        result.Written.Should().BeTrue();
        result.Deleted.Should().BeFalse();
        temp.ReadFile("newfile.go").Should().Be("package main\nfunc main() {}");
    }

    [Fact]
    public async Task Creates_nested_directories()
    {
        using var temp = new TempDirectory();
        var applier = CreateApplier();

        await applier.ApplyAsync(
            temp.Path,
            new CodeChange("src/deep/newfile.go", "package main"));

        temp.ReadFile("src/deep/newfile.go").Should().Be("package main");
    }

    [Fact]
    public async Task Overwrites_an_existing_file_when_no_markers_present()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("existing.go", "package main\nfunc main() {}");
        var applier = CreateApplier();

        await applier.ApplyAsync(
            temp.Path,
            new CodeChange("existing.go", "package main\nfunc updatedMain() {}"));

        temp.ReadFile("existing.go").Should().Be("package main\nfunc updatedMain() {}");
    }

    [Fact]
    public async Task Recreates_a_deleted_file()
    {
        using var temp = new TempDirectory();
        var applier = CreateApplier();

        var result = await applier.ApplyAsync(
            temp.Path,
            new CodeChange("deleted.go", "package main\nfunc deletedMain() {}"));

        result.Written.Should().BeTrue();
        temp.ReadFile("deleted.go").Should().Be("package main\nfunc deletedMain() {}");
    }

    [Fact]
    public async Task Applies_added_and_removed_lines()
    {
        using var temp = new TempDirectory();
        var applier = CreateApplier();

        const string annotated = "package main\n-func old() {}\n+func newer() {}\n func main() {}";

        await applier.ApplyAsync(temp.Path, new CodeChange("program.go", annotated));

        temp.ReadFile("program.go")
            .Should().Be("package main\nfunc newer() {}\n func main() {}");
    }

    [Fact]
    public async Task Preserves_increment_operators()
    {
        using var temp = new TempDirectory();
        var applier = CreateApplier();

        const string annotated = "for i := 0; i < n; i++ {\n+    total++\n}";

        await applier.ApplyAsync(temp.Path, new CodeChange("loop.go", annotated));

        temp.ReadFile("loop.go")
            .Should().Be("for i := 0; i < n; i++ {\n    total++\n}");
    }

    [Fact]
    public void StripDiffMarkers_does_not_treat_double_operators_as_markers()
    {
        DiffApplier.StripDiffMarkers("++i;\n--j;\ni++;\n-j;")
            .Should().Be("++i;\n--j;\ni++;");
    }

    [Fact]
    public void StripDiffMarkers_strips_a_single_leading_marker_and_keeps_indentation()
    {
        DiffApplier.StripDiffMarkers("    +    return 1;\n    -    return 2;")
            .Should().Be("        return 1;");
    }

    [Fact]
    public void StripDiffMarkers_returns_content_unchanged_when_no_markers()
    {
        const string code = "package main\n\nfunc main() {}\n";

        DiffApplier.StripDiffMarkers(code).Should().Be(code);
    }

    [Fact]
    public async Task Writes_text_files_verbatim_without_interpreting_markers()
    {
        using var temp = new TempDirectory();
        var applier = CreateApplier();

        const string markdown = "# Title\n\n- item one\n- item two";

        await applier.ApplyAsync(
            temp.Path,
            new CodeChange("guide.md", markdown, IsTextFile: true));

        temp.ReadFile("guide.md").Should().Be(markdown);
    }

    [Fact]
    public async Task Deletes_the_file_when_the_result_is_empty()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("old.go", "package main\nfunc old() {}");
        var applier = CreateApplier();

        var result = await applier.ApplyAsync(
            temp.Path,
            new CodeChange("old.go", "-package main\n-func old() {}"));

        result.Deleted.Should().BeTrue();
        File.Exists(temp.Combine("old.go")).Should().BeFalse();
    }

    [Fact]
    public async Task Deletes_a_now_empty_directory()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("feature/old.go", "package feature");
        var applier = CreateApplier();

        await applier.ApplyAsync(
            temp.Path,
            new CodeChange("feature/old.go", "-package feature"));

        Directory.Exists(temp.Combine("feature")).Should().BeFalse();
    }

    [Fact]
    public async Task Keeps_a_directory_that_still_has_files()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("feature/old.go", "package feature");
        temp.WriteFile("feature/keep.go", "package feature");
        var applier = CreateApplier();

        await applier.ApplyAsync(
            temp.Path,
            new CodeChange("feature/old.go", "-package feature"));

        Directory.Exists(temp.Combine("feature")).Should().BeTrue();
        File.Exists(temp.Combine("feature/keep.go")).Should().BeTrue();
    }

    [Fact]
    public async Task Never_deletes_the_root_directory()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("only.go", "package main");
        var applier = CreateApplier();

        await applier.ApplyAsync(temp.Path, new CodeChange("only.go", "-package main"));

        Directory.Exists(temp.Path).Should().BeTrue();
    }
}
