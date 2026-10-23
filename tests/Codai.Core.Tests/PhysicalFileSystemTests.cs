using Codai.Core.IO;
using FluentAssertions;

namespace Codai.Core.Tests;

public sealed class PhysicalFileSystemTests
{
    [Fact]
    public async Task Atomic_write_replaces_existing_text_and_cleans_temporary_files()
    {
        var root = Directory.CreateTempSubdirectory("codai-atomic-");
        try
        {
            var path = Path.Combine(root.FullName, "state.json");
            await PhysicalFileSystem.Instance.WriteAllTextAtomicallyAsync(path, "old");
            await PhysicalFileSystem.Instance.WriteAllTextAtomicallyAsync(path, "new");

            (await File.ReadAllTextAsync(path)).Should().Be("new");
            Directory.GetFiles(root.FullName, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Atomic_write_retries_temporary_Windows_replacement_lock()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("codai-atomic-lock-");
        try
        {
            var path = Path.Combine(root.FullName, "state.json");
            await File.WriteAllTextAsync(path, "old");
            using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var write = PhysicalFileSystem.Instance.WriteAllTextAtomicallyAsync(path, "new");
            await Task.WhenAny(write, Task.Delay(100));
            write.IsCompleted.Should().BeFalse();

            reader.Dispose();
            await write;

            (await File.ReadAllTextAsync(path)).Should().Be("new");
            Directory.GetFiles(root.FullName, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Atomic_write_exhausted_retries_preserve_original_and_clean_temporary_files()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("codai-atomic-exhausted-");
        try
        {
            var path = Path.Combine(root.FullName, "state.json");
            await File.WriteAllTextAsync(path, "old");
            using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var write = () => PhysicalFileSystem.Instance.WriteAllTextAtomicallyAsync(path, "new", deadline.Token);

            await write.Should().ThrowAsync<Exception>()
                .Where(exception => exception is IOException || exception is UnauthorizedAccessException);
            reader.Dispose();

            (await File.ReadAllTextAsync(path)).Should().Be("old");
            Directory.GetFiles(root.FullName, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Atomic_write_cancellation_preserves_locked_original_and_cleans_temporary_files()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("codai-atomic-cancel-");
        try
        {
            var path = Path.Combine(root.FullName, "state.json");
            await File.WriteAllTextAsync(path, "old");
            using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var cancellation = new CancellationTokenSource();
            var write = PhysicalFileSystem.Instance.WriteAllTextAtomicallyAsync(path, "new", cancellation.Token);
            await Task.WhenAny(write, Task.Delay(100));
            write.IsCompleted.Should().BeFalse();

            await cancellation.CancelAsync();
            var action = async () => await write;
            await action.Should().ThrowAsync<OperationCanceledException>();
            reader.Dispose();

            (await File.ReadAllTextAsync(path)).Should().Be("old");
            Directory.GetFiles(root.FullName, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
