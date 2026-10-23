using System.Text.Json;
using Codai.Cli;
using Codai.Cli.Ui;
using Codai.Core.Tools;
using FluentAssertions;
using Spectre.Console;

namespace Codai.Session.Tests;

public class ApprovalFlowProbeTests
{
    [Fact]
    public async Task Approval_completes_from_inside_a_turn()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 100;
        console.Profile.Height = 30;
        var input = new DelayedKeyInput(TimeSpan.FromMilliseconds(200));
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();

        var request = ToolApprovalRequest.Create(
            "run_command",
            "Run a shell command in the project.",
            "run_command(command=python --version)",
            JsonDocument.Parse("{\"command\":\"python --version\"}").RootElement);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var decision = await ui.RunTurnAsync(token => ui.ApproveAsync(request, token), deadline.Token);

        decision.Should().Be(ApprovalDecision.AllowOnce);
    }

    private sealed class DelayedKeyInput(TimeSpan delay) : IConsoleInput
    {
        private readonly DateTime _start = DateTime.UtcNow;
        private bool _sent;
        private int _polls;

        public int Polls => _polls;

        public string? ReadLine() => "first";

        public bool TryReadKey(out ConsoleKeyInfo key)
        {
            Interlocked.Increment(ref _polls);
            key = default;
            if (_sent || DateTime.UtcNow - _start < delay)
            {
                return false;
            }

            _sent = true;
            key = new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
            return true;
        }
    }
}
