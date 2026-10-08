using Codai.Core.Prompting;
using Codai.Session.Tests.Fakes;
using FluentAssertions;

namespace Codai.Session.Tests;

public class RequestedContextResolverTests
{
    private static RequestedContextResolver Create(InMemoryFileSystem fileSystem) => new(fileSystem);

    [Fact]
    public async Task Renders_a_file_block_for_each_readable_path()
    {
        var fileSystem = new InMemoryFileSystem(@"C:\project");
        fileSystem.AddFile(@"C:\project\src\Foo.cs", "class Foo { }");

        var result = await Create(fileSystem).ResolveAsync(@"C:\project", ["src/Foo.cs"]);

        result.Should().Be("**File: src/Foo.cs**\n\nclass Foo { }");
    }

    [Fact]
    public async Task Separates_multiple_files_with_the_history_separator()
    {
        var fileSystem = new InMemoryFileSystem(@"C:\project");
        fileSystem.AddFile(@"C:\project\a.cs", "A");
        fileSystem.AddFile(@"C:\project\b.cs", "B");

        var result = await Create(fileSystem).ResolveAsync(@"C:\project", ["a.cs", "b.cs"]);

        result.Should().Be("**File: a.cs**\n\nA\n---------\n\n**File: b.cs**\n\nB");
    }

    [Fact]
    public async Task Skips_paths_that_do_not_exist()
    {
        var fileSystem = new InMemoryFileSystem(@"C:\project");
        fileSystem.AddFile(@"C:\project\a.cs", "A");

        var result = await Create(fileSystem).ResolveAsync(@"C:\project", ["missing.cs", "a.cs"]);

        result.Should().Contain("**File: a.cs**");
        result.Should().NotContain("missing.cs");
    }

    [Fact]
    public async Task Never_reads_outside_the_project_root()
    {
        var fileSystem = new InMemoryFileSystem(@"C:\project", @"C:\secret");
        fileSystem.AddFile(@"C:\secret\keys.txt", "top secret");

        var result = await Create(fileSystem).ResolveAsync(@"C:\project", [@"..\secret\keys.txt"]);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_when_nothing_could_be_read()
    {
        var fileSystem = new InMemoryFileSystem(@"C:\project");

        var result = await Create(fileSystem).ResolveAsync(@"C:\project", ["nope.cs"]);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_for_an_empty_path_list()
    {
        var result = await Create(new InMemoryFileSystem(@"C:\project"))
            .ResolveAsync(@"C:\project", []);

        result.Should().BeNull();
    }
}
