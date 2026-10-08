using Codai.Core.Chat;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

public class FileSessionStoreTests
{
    private const string Root = @"C:\project";

    private static (FileSessionStore Store, InMemoryFileSystem FileSystem) CreateShared()
    {
        var fileSystem = new InMemoryFileSystem();
        return (new FileSessionStore(fileSystem), fileSystem);
    }

    [Fact]
    public async Task Loading_without_a_session_file_returns_nothing()
    {
        var (store, _) = CreateShared();

        (await store.LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task Saves_and_loads_a_conversation()
    {
        var (store, _) = CreateShared();
        var timestamp = DateTimeOffset.Parse("2024-05-01T12:34:56Z");
        var turns = new[]
        {
            new ChatTurn(timestamp, "first", "answer one"),
            new ChatTurn(timestamp.AddMinutes(1), "second", "answer two"),
        };

        await store.SaveAsync(Root, turns);
        var loaded = await store.LoadAsync(Root);

        loaded.Should().HaveCount(2);
        loaded[0].UserRequest.Should().Be("first");
        loaded[0].AiResponse.Should().Be("answer one");
        loaded[0].Timestamp.Should().Be(timestamp);
        loaded[1].UserRequest.Should().Be("second");
    }

    [Fact]
    public async Task Writes_the_session_outside_the_scanned_project_files()
    {
        var (store, fileSystem) = CreateShared();

        await store.SaveAsync(Root, [new ChatTurn(DateTimeOffset.UtcNow, "q", "a")]);

        fileSystem.FileExists(FileSessionStore.GetPath(Root)).Should().BeTrue();
        FileSessionStore.GetPath(Root).Should().Contain(".codai");
    }

    [Fact]
    public async Task Saving_an_empty_conversation_forgets_the_stored_session()
    {
        var (store, fileSystem) = CreateShared();

        await store.SaveAsync(Root, [new ChatTurn(DateTimeOffset.UtcNow, "q", "a")]);
        await store.SaveAsync(Root, []);

        fileSystem.FileExists(FileSessionStore.GetPath(Root)).Should().BeFalse();
        (await store.LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task Saving_twice_overwrites_rather_than_appends()
    {
        var (store, _) = CreateShared();

        await store.SaveAsync(Root, [new ChatTurn(DateTimeOffset.UtcNow, "first", "a")]);
        await store.SaveAsync(Root, [new ChatTurn(DateTimeOffset.UtcNow, "second", "b")]);

        var loaded = await store.LoadAsync(Root);

        loaded.Should().ContainSingle().Subject.UserRequest.Should().Be("second");
    }

    [Fact]
    public async Task A_corrupt_session_file_is_treated_as_no_session()
    {
        var (store, fileSystem) = CreateShared();
        fileSystem.AddFile(FileSessionStore.GetPath(Root), "{ not json at all");

        (await store.LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_session_file_from_an_incompatible_version_is_ignored()
    {
        var (store, fileSystem) = CreateShared();
        fileSystem.AddFile(
            FileSessionStore.GetPath(Root),
            """{"version":99,"savedAt":"2024-01-01T00:00:00+00:00","turns":[]}""");

        (await store.LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task Saves_the_current_format_version()
    {
        var (store, fileSystem) = CreateShared();

        await store.SaveAsync(Root, [new ChatTurn(DateTimeOffset.UtcNow, "q", "a")]);

        fileSystem.Files[InMemoryFileSystem.Normalize(FileSessionStore.GetPath(Root))]
            .Should().Contain($"\"version\": {FileSessionStore.CurrentVersion}");
    }

    [Fact]
    public async Task An_implausibly_large_session_file_is_not_read()
    {
        var (store, fileSystem) = CreateShared();
        fileSystem.AddFile(
            FileSessionStore.GetPath(Root),
            new string('x', (int)FileSessionStore.MaxFileSizeBytes + 1));

        (await store.LoadAsync(Root)).Should().BeEmpty();
    }
}
