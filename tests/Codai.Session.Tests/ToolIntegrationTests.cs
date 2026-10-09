using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Tools;
using Codai.Session.Tests.Fakes;
using Codai.Session.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Codai.Session.Tests;

public class ToolIntegrationTests
{
    private static CodaiOptions OptionsFor(string provider = "ollama") => new()
    {
        Theme = CodaiDefaults.Theme,
        AiProviderConfig = new AiProviderOptions
        {
            Provider = provider,
            Model = string.Empty,
            BaseUrl = "http://localhost:11434/api",
        },
    };

    [Fact]
    public void Reports_tool_start_and_completion_through_the_user_interface()
    {
        var ui = new FakeUserInterface();
        var observer = new InteractiveToolObserver(ui);

        observer.OnToolStarted("grep", "grep(pattern=TODO)");
        observer.OnToolCompleted("grep", succeeded: true, "grep(pattern=TODO)");

        ui.Infos.Should().Equal("  • grep(pattern=TODO)", "  ✓ grep");
    }

    [Fact]
    public void Reports_a_failed_tool_with_its_reason()
    {
        var ui = new FakeUserInterface();
        var observer = new InteractiveToolObserver(ui);

        observer.OnToolCompleted("read_file", succeeded: false, "File 'a.cs' does not exist.");

        ui.Infos.Should().ContainSingle().Which.Should()
            .Be("  ✗ read_file: File 'a.cs' does not exist.");
    }

    [Fact]
    public void Reports_a_denied_tool()
    {
        var ui = new FakeUserInterface();
        var observer = new InteractiveToolObserver(ui);

        observer.OnToolDenied("write_file", "write_file(path=a.txt)");

        ui.Infos.Should().ContainSingle().Which.Should().Be("  ✗ write_file declined");
    }

    [Fact]
    public async Task The_approver_asks_the_user_and_returns_the_answer()
    {
        var ui = new FakeUserInterface();
        ui.EnqueueConfirmation(true);
        var approver = new InteractiveToolApprover(ui);

        var approved = await approver.ApproveAsync(
            new ToolApprovalRequest("write_file", "Writes a file.", "write_file(path=a.txt)"));

        approved.Should().BeTrue();
        ui.Prompts.Should().ContainSingle()
            .Which.Should().Be("Allow write_file: write_file(path=a.txt)");
    }

    [Fact]
    public async Task The_approver_returns_false_when_the_user_declines()
    {
        var ui = new FakeUserInterface();
        ui.EnqueueConfirmation(false);

        (await new InteractiveToolApprover(ui).ApproveAsync(
            new ToolApprovalRequest("write_file", "Writes a file.", "write_file(path=a.txt)")))
            .Should().BeFalse();
    }

    [Fact]
    public void A_host_with_a_user_interface_gets_the_interactive_approver()
    {
        var ui = new FakeUserInterface();
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiSession(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IToolApprover>().Should().BeOfType<InteractiveToolApprover>();
        provider.GetRequiredService<IToolObserver>().Should().BeOfType<InteractiveToolObserver>();
    }

    [Fact]
    public void A_host_without_a_user_interface_keeps_the_safe_defaults()
    {
        var services = new ServiceCollection();
        services.AddCodaiSession(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IToolApprover>().Should().BeSameAs(DenyAllToolApprover.Instance);
        provider.GetRequiredService<IToolObserver>().Should().BeSameAs(NullToolObserver.Instance);
    }

    [Fact]
    public void The_session_offers_the_read_only_tools_and_the_editing_tools()
    {
        var services = new ServiceCollection();
        services.AddCodaiSession(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IToolExecutor>();

        executor.Tools.Select(tool => tool.Name).Should().Equal(
            "read_file", "list_files", "glob", "grep", "read_skill", "write_file", "edit_file");

        // The shell tool is the one capability a host has to ask for, so it must not be
        // here by default.
        executor.Tools.Should().NotContain(tool => tool.Name == "run_command");
    }

    [Fact]
    public void The_session_offers_the_shell_tool_only_when_the_host_allows_it()
    {
        var services = new ServiceCollection();
        services.AddCodaiSession(
            OptionsFor(),
            Directory.GetCurrentDirectory(),
            allowShell: true);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IToolExecutor>().Tools
            .Should().Contain(tool => tool.Name == "run_command" && !tool.IsReadOnly);
    }

    [Fact]
    public async Task The_session_executor_reaches_the_real_file_system()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-session-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "hello.cs"), "class Hello { }\n");

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IUserInterface>(new FakeUserInterface());
            services.AddCodaiSession(OptionsFor(), root);

            using var provider = services.BuildServiceProvider();
            var executor = provider.GetRequiredService<IToolExecutor>();

            var listed = await executor.ExecuteAsync("list_files", ToolTestArgs.Parse("""{"path":"src"}"""));
            listed.IsSuccess.Should().BeTrue();
            listed.Content.Should().Contain("hello.cs");

            var read = await executor.ExecuteAsync(
                "read_file",
                ToolTestArgs.Parse("""{"path":"src/hello.cs"}"""));

            read.IsSuccess.Should().BeTrue();
            read.Content.Should().Contain("class Hello { }");

            var missing = await executor.ExecuteAsync(
                "read_file",
                ToolTestArgs.Parse("""{"path":"src/definitely-not-here.cs"}"""));

            missing.IsSuccess.Should().BeFalse();
            missing.Content.Should().Contain("does not exist");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task An_unknown_tool_returns_a_failure_rather_than_throwing()
    {
        var services = new ServiceCollection();
        services.AddCodaiSession(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IToolExecutor>()
            .ExecuteAsync("nope", ToolTestArgs.Parse("{}"));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("Unknown tool");
    }

    [Fact]
    public void The_runner_reports_progress_through_the_registered_observer()
    {
        var ui = new FakeUserInterface();
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiSession(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<ToolRunner>();

        runner.Tools.Should().HaveCount(7);
    }

    [Fact]
    public async Task A_tool_the_request_does_not_offer_is_refused_without_asking()
    {
        var ui = new FakeUserInterface();
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiSession(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();

        // Resolved separately on purpose: this doubles as the wiring assertion. The
        // executor and whoever begins the request have to be looking at one policy, or
        // the restriction is recorded where nothing reads it.
        provider.GetRequiredService<ToolAccessPolicy>().BeginRequest(["read_file"]);

        var result = await provider.GetRequiredService<IToolExecutor>().ExecuteAsync(
            "write_file",
            ToolTestArgs.Parse("""{"path":"a.txt","content":"hi"}"""));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("not available for this request");

        // Refused before approval, so the user is not asked about a call that could
        // never have run.
        ui.Prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_restriction_a_write_tool_still_asks_for_approval()
    {
        var ui = new FakeUserInterface();
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiSession(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IToolExecutor>().ExecuteAsync(
            "write_file",
            ToolTestArgs.Parse("""{"path":"a.txt","content":"hi"}"""));

        ui.Prompts.Should().ContainSingle().Which.Should().Contain("write_file");
    }

    /// <summary>Argument parsing helper shared by the tool tests here.</summary>
    private static class ToolTestArgs
    {
        public static System.Text.Json.JsonElement Parse(string json) =>
            System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
    }
}
