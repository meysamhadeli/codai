using Codai.Core.Abstractions;
using Codai.Core.Chat;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the archive that a summary points at. Writing it is best effort, so the
/// failure matters as much as the success: compaction has to work on a file system that
/// will not take the file.
/// </summary>
public class TranscriptStoreTests
{
    private const string Root = @"C:\project";

    private static (TranscriptStore Store, InMemoryFileSystem FileSystem) CreateShared()
    {
        var fileSystem = new InMemoryFileSystem();
        return (new TranscriptStore(fileSystem), fileSystem);
    }

    private static ChatTurn[] Turns() =>
    [
        new(DateTimeOffset.Parse("2024-05-01T12:34:56Z"), "first question", "first answer"),
        new(DateTimeOffset.Parse("2024-05-01T12:36:00Z"), "second question", "second answer"),
    ];

    [Fact]
    public async Task An_empty_conversation_is_not_archived()
    {
        var (store, fileSystem) = CreateShared();

        (await store.ArchiveAsync(Root, [])).Should().BeNull();
        fileSystem.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task The_archive_is_named_by_its_path_under_the_project()
    {
        var (store, _) = CreateShared();

        var path = await store.ArchiveAsync(Root, Turns());

        path.Should().StartWith(".codai/transcripts/");
        path.Should().EndWith(".md");
    }

    [Fact]
    public async Task The_archive_holds_every_turn_it_was_given()
    {
        var (store, fileSystem) = CreateShared();

        await store.ArchiveAsync(Root, Turns());

        var archived = fileSystem.Files.Should().ContainSingle().Which.Value;

        archived.Should().Contain("# Conversation archived before compaction");
        archived.Should().Contain("first question").And.Contain("first answer");
        archived.Should().Contain("second question").And.Contain("second answer");
    }

    [Fact]
    public async Task Each_turn_is_given_a_heading_so_the_archive_can_be_navigated()
    {
        var (store, fileSystem) = CreateShared();

        await store.ArchiveAsync(Root, Turns());

        var archived = fileSystem.Files.Should().ContainSingle().Which.Value;

        archived.Should().Contain("## Turn 1").And.Contain("## Turn 2");
        archived.Should().Contain("### User").And.Contain("### Assistant");
    }

    [Fact]
    public async Task The_archive_is_written_under_the_project_root()
    {
        var (store, fileSystem) = CreateShared();

        var path = await store.ArchiveAsync(Root, Turns());

        fileSystem.Files.Should().ContainKey(InMemoryFileSystem.Normalize(Path.Combine(Root, path!)));
    }

    [Fact]
    public async Task A_file_system_that_refuses_the_write_does_not_fail_the_compaction()
    {
        var store = new TranscriptStore(new UnwritableFileSystem());

        (await store.ArchiveAsync(Root, Turns())).Should().BeNull();
    }

    /// <summary>A file system that refuses to create the folder the archive goes in.</summary>
    private sealed class UnwritableFileSystem : IFileSystem
    {
        public bool FileExists(string path) => false;

        public bool DirectoryExists(string path) => false;

        public long GetFileSize(string path) => 0;

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
            throw new FileNotFoundException("File not found.", path);

        public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("Read-only file system.");

        public Task WriteAllTextAtomicallyAsync(string path, string contents, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("Read-only file system.");

        public void CreateDirectory(string path) =>
            throw new UnauthorizedAccessException("Read-only file system.");

        public void DeleteFile(string path)
        {
            // Nothing is stored, so there is nothing to delete.
        }

        public void DeleteDirectory(string path, bool recursive)
        {
            // Nothing is stored, so there is nothing to delete.
        }

        public IEnumerable<string> EnumerateFiles(string path) => [];

        public IEnumerable<string> EnumerateDirectories(string path) => [];
    }
}
