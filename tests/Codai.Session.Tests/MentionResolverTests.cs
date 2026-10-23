using Codai.Session.Tests.Fakes;
using FluentAssertions;

namespace Codai.Session.Tests;

public class MentionResolverTests
{
    private const string Root = @"C:\project";

    private static (MentionResolver Resolver, InMemoryFileSystem FileSystem) Create()
    {
        var fileSystem = new InMemoryFileSystem(Root);
        fileSystem.AddFile(Path.Combine(Root, "src", "a.cs"), "class A { }");
        fileSystem.AddFile(Path.Combine(Root, "src", "b.cs"), "class B { }");
        fileSystem.AddFile(Path.Combine(Root, "docs", "readme.md"), "# Readme");
        fileSystem.AddFile(Path.Combine(Root, "bin", "generated.cs"), "class Generated { }");

        return (new MentionResolver(fileSystem), fileSystem);
    }

    [Fact]
    public async Task Returns_nothing_when_the_request_has_no_mentions()
    {
        var (resolver, _) = Create();

        var resolution = await resolver.ResolveAsync(Root, "just refactor the parser");

        resolution.Context.Should().BeNull();
        resolution.Files.Should().BeEmpty();
        resolution.Unresolved.Should().BeEmpty();
    }

    [Fact]
    public async Task Attaches_a_mentioned_file()
    {
        var (resolver, _) = Create();

        var resolution = await resolver.ResolveAsync(Root, "explain @src/a.cs");

        resolution.Files.Should().Equal("src/a.cs");
        resolution.Context.Should().Contain("**File: src/a.cs**");
        resolution.Context.Should().Contain("class A { }");
        resolution.Unresolved.Should().BeEmpty();
    }

    [Fact]
    public async Task Attaches_every_file_under_a_mentioned_directory()
    {
        var (resolver, _) = Create();

        var resolution = await resolver.ResolveAsync(Root, "review @src");

        resolution.Files.Should().Equal("src/a.cs", "src/b.cs");
        resolution.Context.Should().Contain("**File: src/a.cs**").And.Contain("**File: src/b.cs**");
    }

    [Fact]
    public async Task A_trailing_slash_names_a_directory_too()
    {
        var (resolver, _) = Create();

        var resolution = await resolver.ResolveAsync(Root, "review @src/");

        resolution.Files.Should().Equal("src/a.cs", "src/b.cs");
    }

    [Fact]
    public async Task Attaches_files_matching_a_glob_mention()
    {
        var (resolver, _) = Create();

        var resolution = await resolver.ResolveAsync(Root, "review @**/*.md");

        resolution.Files.Should().Equal("docs/readme.md");
    }

    [Fact]
    public async Task Deduplicates_a_file_matched_twice()
    {
        var (resolver, _) = Create();

        var resolution = await resolver.ResolveAsync(Root, "compare @src/a.cs with @src");

        resolution.Files.Should().Equal("src/a.cs", "src/b.cs");
    }

    [Fact]
    public async Task Reports_a_mention_that_matches_nothing()
    {
        var (resolver, _) = Create();

        var resolution = await resolver.ResolveAsync(Root, "explain @src/missing.cs");

        resolution.Context.Should().BeNull();
        resolution.Unresolved.Should().Equal("src/missing.cs");
    }

    [Fact]
    public async Task Reports_an_ignored_file_as_unresolved()
    {
        var (resolver, _) = Create();

        var resolution = await resolver.ResolveAsync(Root, "explain @bin/generated.cs");

        resolution.Files.Should().BeEmpty();
        resolution.Unresolved.Should().Equal("bin/generated.cs");
    }

    [Fact]
    public async Task Refuses_a_mention_that_escapes_the_project_root()
    {
        var (resolver, fileSystem) = Create();
        fileSystem.AddFile(@"C:\outside.txt", "secret");

        var resolution = await resolver.ResolveAsync(Root, "explain @../outside.txt");

        resolution.Context.Should().BeNull();
        resolution.Unresolved.Should().Equal("../outside.txt");
    }

    [Fact]
    public async Task Keeps_only_the_first_files_when_too_many_are_mentioned()
    {
        var fileSystem = new InMemoryFileSystem(Root);

        for (var i = 0; i < MentionResolver.MaxFiles + 5; i++)
        {
            fileSystem.AddFile(Path.Combine(Root, "src", $"file{i:00}.cs"), $"class C{i} {{ }}");
        }

        var resolver = new MentionResolver(fileSystem);

        var resolution = await resolver.ResolveAsync(Root, "review @src");

        resolution.Files.Should().HaveCount(MentionResolver.MaxFiles);
    }

    [Fact]
    public async Task Resolves_a_mixed_request_and_keeps_the_failures()
    {
        var (resolver, _) = Create();

        var resolution = await resolver.ResolveAsync(Root, "@docs/readme.md and @nope.txt");

        resolution.Files.Should().Equal("docs/readme.md");
        resolution.Unresolved.Should().Equal("nope.txt");
    }
}
