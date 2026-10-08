using Codai.Analysis.Extractors;
using Codai.Analysis;
using Codai.Analysis.Tests.Fakes;
using FluentAssertions;

namespace Codai.Analysis.Tests;

public class FileSystemProjectScannerTests
{
    private static FileSystemProjectScanner CreateScanner(
        InMemoryFileSystem fileSystem,
        long maxFileSize = FileSystemProjectScanner.DefaultMaxFileSizeBytes) =>
        new(fileSystem, new SignatureExtractorRegistry(), maxFileSize);

    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";

    [Fact]
    public async Task Scans_files_and_builds_signature_blocks()
    {
        var fileSystem = new InMemoryFileSystem()
            .AddFile($@"{Root}\Program.cs", "namespace A; public class Program {}");
        var scanner = CreateScanner(fileSystem);

        var context = await scanner.ScanAsync(Root);

        context.Files.Should().ContainSingle();
        context.Files[0].RelativePath.Should().Be("Program.cs");
        context.RawCodes.Should().ContainSingle()
            .Which.Should().StartWith("**File: Program.cs**");
        context.RawCodes[0].Should().Contain("class: Program");
    }

    [Fact]
    public async Task Skips_builtin_ignored_directories()
    {
        var fileSystem = new InMemoryFileSystem()
            .AddFile($@"{Root}\src\Program.cs", "class A {}")
            .AddFile($@"{Root}\node_modules\pkg\index.js", "class B {}")
            .AddFile($@"{Root}\bin\Debug\app.dll", "binary");
        var scanner = CreateScanner(fileSystem);

        var context = await scanner.ScanAsync(Root);

        context.Files.Select(f => f.RelativePath).Should().Equal("src/Program.cs");
    }

    [Fact]
    public async Task Skips_files_larger_than_the_limit()
    {
        var fileSystem = new InMemoryFileSystem()
            .AddFile($@"{Root}\small.cs", "class Small {}", sizeOverride: 10)
            .AddFile($@"{Root}\large.cs", "class Large {}", sizeOverride: 200_000);
        var scanner = CreateScanner(fileSystem);

        var context = await scanner.ScanAsync(Root);

        context.Files.Select(f => f.RelativePath).Should().Equal("small.cs");
    }

    [Fact]
    public async Task Applies_codai_gitignore_patterns()
    {
        var fileSystem = new InMemoryFileSystem()
            .AddFile($@"{Root}\.codai-gitignore", "secrets/\n*.generated.cs\n")
            .AddFile($@"{Root}\src\Program.cs", "class A {}")
            .AddFile($@"{Root}\secrets\token.txt", "shh")
            .AddFile($@"{Root}\src\Model.generated.cs", "class Generated {}");
        var scanner = CreateScanner(fileSystem);

        var context = await scanner.ScanAsync(Root);

        context.Files.Select(f => f.RelativePath).Should().Equal("src/Program.cs");
    }

    [Fact]
    public async Task Returns_forward_slash_relative_paths()
    {
        var fileSystem = new InMemoryFileSystem()
            .AddFile($@"{Root}\src\services\OrderService.cs", "class OrderService {}");
        var scanner = CreateScanner(fileSystem);

        var context = await scanner.ScanAsync(Root);

        context.Files[0].RelativePath.Should().Be("src/services/OrderService.cs");
    }

    [Fact]
    public async Task Unsupported_languages_produce_an_empty_signature()
    {
        var fileSystem = new InMemoryFileSystem()
            .AddFile($@"{Root}\notes.md", "# Notes");
        var scanner = CreateScanner(fileSystem);

        var context = await scanner.ScanAsync(Root);

        context.Files.Should().ContainSingle();
        context.Files[0].Signature.Should().BeEmpty();
        context.RawCodes[0].Should().Be("**File: notes.md**\n\n");
    }

    [Fact]
    public async Task Throws_when_root_does_not_exist()
    {
        var scanner = CreateScanner(new InMemoryFileSystem());

        var act = async () => await scanner.ScanAsync($@"{Root}\missing");

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task Order_is_deterministic()
    {
        var fileSystem = new InMemoryFileSystem()
            .AddFile($@"{Root}\b.cs", "class B {}")
            .AddFile($@"{Root}\a.cs", "class A {}")
            .AddFile($@"{Root}\c.cs", "class C {}");
        var scanner = CreateScanner(fileSystem);

        var context = await scanner.ScanAsync(Root);

        context.Files.Select(f => f.RelativePath).Should().Equal("a.cs", "b.cs", "c.cs");
    }
}
