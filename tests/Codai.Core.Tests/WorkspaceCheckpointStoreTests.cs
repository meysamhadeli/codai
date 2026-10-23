using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.IO;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

public sealed class WorkspaceCheckpointStoreTests
{
    [Fact]
    public async Task Git_command_failure_keeps_diagnostics_bounded_and_preserves_leading_error()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-checkpoint-diagnostic-" + Guid.NewGuid().ToString("N"));
        var detail = "fatal: checkpoint index unavailable\n" + new string('x', 5000) + "diagnostic-tail";
        var store = new GitWorkspaceCheckpointStore(new InMemoryFileSystem(), new FailingGitRunner(detail));

        var capture = () => store.CaptureAsync(root, "task-1", "Before task");
        var error = await capture.Should().ThrowAsync<IOException>();

        error.Which.Message.Should().StartWith("Git checkpoint command failed: fatal: checkpoint index unavailable")
            .And.EndWith("...")
            .And.NotContain("diagnostic-tail");
        error.Which.Message.Length.Should().BeLessThanOrEqualTo(850);
    }

    private sealed class FailingGitRunner(string detail) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string commandLine, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(commandLine.EndsWith(" add -A", StringComparison.Ordinal)
                ? new ProcessResult(1, string.Empty, detail, false)
                : new ProcessResult(0, string.Empty, string.Empty, false));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Git_store_restores_binary_created_and_deleted_files_without_touching_codai(bool ignoredState)
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-git-checkpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".codai"));
            if (ignoredState)
            {
                await File.WriteAllTextAsync(Path.Combine(root, ".gitignore"), ".codai/\n");
            }
            await File.WriteAllTextAsync(Path.Combine(root, "tracked.txt"), "before");
            await File.WriteAllBytesAsync(Path.Combine(root, "binary.dat"), [0, 1, 2, 255]);
            await File.WriteAllTextAsync(Path.Combine(root, ".codai", "state.txt"), "keep");

            var store = new GitWorkspaceCheckpointStore(PhysicalFileSystem.Instance, new ProcessRunner());
            var checkpoint = await store.CaptureAsync(root, "task-1", "Before task");
            await File.WriteAllTextAsync(Path.Combine(root, "tracked.txt"), "after");
            File.Delete(Path.Combine(root, "binary.dat"));
            await File.WriteAllTextAsync(Path.Combine(root, "created.txt"), "remove");
            await File.WriteAllTextAsync(Path.Combine(root, ".codai", "state.txt"), "changed state");

            await store.RestoreAsync(root, "task-1", checkpoint.Id);

            (await File.ReadAllTextAsync(Path.Combine(root, "tracked.txt"))).Should().Be("before");
            (await File.ReadAllBytesAsync(Path.Combine(root, "binary.dat"))).Should().Equal(0, 1, 2, 255);
            File.Exists(Path.Combine(root, "created.txt")).Should().BeFalse();
            (await File.ReadAllTextAsync(Path.Combine(root, ".codai", "state.txt"))).Should().Be("changed state");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Restore_returns_workspace_to_checkpoint_without_touching_codai_state()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-checkpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".codai"));
            await File.WriteAllTextAsync(Path.Combine(root, "tracked.txt"), "before");
            await File.WriteAllTextAsync(Path.Combine(root, ".codai", "state.txt"), "keep");

            var store = new FileWorkspaceCheckpointStore(PhysicalFileSystem.Instance);
            var checkpoint = await store.CaptureAsync(root, "task-1", "Before task");
            await File.WriteAllTextAsync(Path.Combine(root, "tracked.txt"), "after");
            await File.WriteAllTextAsync(Path.Combine(root, "created.txt"), "remove");
            await File.WriteAllTextAsync(Path.Combine(root, ".codai", "state.txt"), "changed state");

            await store.RestoreAsync(root, "task-1", checkpoint.Id);

            (await File.ReadAllTextAsync(Path.Combine(root, "tracked.txt"))).Should().Be("before");
            File.Exists(Path.Combine(root, "created.txt")).Should().BeFalse();
            (await File.ReadAllTextAsync(Path.Combine(root, ".codai", "state.txt"))).Should().Be("changed state");
            (await store.ListAsync(root, "task-1")).Should().ContainSingle(item => item.Id == checkpoint.Id);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Git_store_compare_returns_file_content_diff()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-git-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "tracked.txt"), "before\n");

            var store = new GitWorkspaceCheckpointStore(PhysicalFileSystem.Instance, new ProcessRunner());
            var checkpoint = await store.CaptureAsync(root, "task-1", "Before change");
            await File.WriteAllTextAsync(Path.Combine(root, "tracked.txt"), "after\n");
            await File.WriteAllTextAsync(Path.Combine(root, "created.txt"), "new\n");

            var diff = await store.CompareAsync(root, "task-1", checkpoint.Id);

            diff.Should().Contain("tracked.txt")
                .And.Contain("-before")
                .And.Contain("+after")
                .And.Contain("created.txt")
                .And.Contain("+new");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task File_store_compare_returns_file_content_diff()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-file-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "tracked.txt"), "before\n");

            var store = new FileWorkspaceCheckpointStore(PhysicalFileSystem.Instance);
            var checkpoint = await store.CaptureAsync(root, "task-1", "Before change");
            await File.WriteAllTextAsync(Path.Combine(root, "tracked.txt"), "after\n");

            var diff = await store.CompareAsync(root, "task-1", checkpoint.Id);

            diff.Should().Contain("tracked.txt")
                .And.Contain("-before")
                .And.Contain("+after");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Task_only_restore_restores_task_metadata_without_workspace_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-task-checkpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var taskStore = new FileAgentTaskStore(PhysicalFileSystem.Instance);
            var task = AgentTask.Create("before").Start().Complete();
            await taskStore.SaveAsync(root, task);
            var sessionStore = new FileSessionStore(PhysicalFileSystem.Instance);
            await sessionStore.SaveAsync(root, task.Id, [new ChatTurn(DateTimeOffset.UtcNow, "before", "answer")]);
            await File.WriteAllTextAsync(Path.Combine(root, "tracked.txt"), "before");

            var store = new FileWorkspaceCheckpointStore(PhysicalFileSystem.Instance);
            var checkpoint = await store.CaptureAsync(root, task.Id, "After task");
            await taskStore.SaveAsync(root, AgentTask.Create("after").Start().Complete() with { Id = task.Id });
            await sessionStore.SaveAsync(root, task.Id, [new ChatTurn(DateTimeOffset.UtcNow, "after", "answer")]);
            await File.WriteAllTextAsync(Path.Combine(root, "tracked.txt"), "changed");

            await store.RestoreAsync(root, task.Id, checkpoint.Id, WorkspaceCheckpointRestoreMode.TaskOnly);

            (await taskStore.LoadAsync(root))!.Request.Should().Be("before");
            (await File.ReadAllTextAsync(Path.Combine(root, "tracked.txt"))).Should().Be("changed");
            (await sessionStore.LoadAsync(root, task.Id)).Single().UserRequest.Should().Be("before");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task File_store_retains_only_newest_configured_checkpoints()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileWorkspaceCheckpointStore(PhysicalFileSystem.Instance, retention: 2);
            var first = await store.CaptureAsync(root, "task-1", "first");
            var second = await store.CaptureAsync(root, "task-1", "second");
            var third = await store.CaptureAsync(root, "task-1", "third");

            (await store.ListAsync(root, "task-1")).Should().HaveCount(2)
                .And.Contain(item => item.Id == second.Id)
                .And.Contain(item => item.Id == third.Id)
                .And.NotContain(item => item.Id == first.Id);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Git_store_retains_only_newest_metadata_checkpoints()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-git-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "tracked.txt"), "content");
            var store = new GitWorkspaceCheckpointStore(PhysicalFileSystem.Instance, new ProcessRunner(), retention: 2);
            var first = await store.CaptureAsync(root, "task-1", "first");
            var second = await store.CaptureAsync(root, "task-1", "second");
            var third = await store.CaptureAsync(root, "task-1", "third");

            (await store.ListAsync(root, "task-1")).Should().HaveCount(2)
                .And.Contain(item => item.Id == second.Id)
                .And.Contain(item => item.Id == third.Id)
                .And.NotContain(item => item.Id == first.Id);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task File_store_reports_retained_checkpoint_count_and_bytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileWorkspaceCheckpointStore(PhysicalFileSystem.Instance);
            await store.CaptureAsync(root, "task-1", "first");
            await store.CaptureAsync(root, "task-1", "second");

            var diagnostics = await store.GetStorageDiagnosticsAsync(root);

            diagnostics.CheckpointCount.Should().Be(2);
            diagnostics.RetainedBytes.Should().BeGreaterThan(0);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
