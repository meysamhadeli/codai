using System.Diagnostics;
using Codai.Core.IO;
using FluentAssertions;

namespace Codai.Core.Tests;

public sealed class PhysicalFileSystemTests
{
    [Fact]
    public async Task Canonical_paths_resolve_parent_links_and_preserve_missing_leaf_paths()
    {
        var root = Directory.CreateTempSubdirectory("codai-canonical-");
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root.FullName, "target", "nested"));
            var alias = Path.Combine(root.FullName, "alias");
            if (OperatingSystem.IsWindows())
            {
                var start = new ProcessStartInfo("cmd.exe")
                {
                    WorkingDirectory = root.FullName,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var argument in new[] { "/c", "mklink", "/J", "alias", Path.Combine(root.FullName, "target") })
                {
                    start.ArgumentList.Add(argument);
                }
                using var process = Process.Start(start)!;
                await process.WaitForExitAsync();
                process.ExitCode.Should().Be(0);
            }
            else
            {
                Directory.CreateSymbolicLink(alias, Path.Combine(root.FullName, "target"));
            }

            PhysicalFileSystem.Instance.GetCanonicalPath(Path.Combine(alias, "nested", "missing.txt"))
                .Should().Be(Path.Combine(target.FullName, "missing.txt"));
            await File.WriteAllTextAsync(Path.Combine(target.FullName, "existing.txt"), "Content.");
            PhysicalFileSystem.Instance.GetCanonicalPath(Path.Combine(alias, "nested", "existing.txt"))
                .Should().Be(Path.Combine(target.FullName, "existing.txt"));
        }
        finally
        {
            var alias = Path.Combine(root.FullName, "alias");
            if (Directory.Exists(alias))
            {
                Directory.Delete(alias);
            }
            root.Delete(recursive: true);
        }
    }

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
