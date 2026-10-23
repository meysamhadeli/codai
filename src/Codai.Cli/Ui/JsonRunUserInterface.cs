using System.Text;
using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Tokens;

namespace Codai.Cli.Ui;

internal sealed class JsonRunUserInterface(IConsoleOutput output) : IUserInterface, IAgentProgressSink
{
    private readonly StringBuilder _text = new();
    private readonly JsonDiagnosticOutput _diagnostics = new(output);
    private string? _model;
    private CliJsonUsage _usage = new(0, 0, 0);
    private bool _contentStarted;
    private int _iterations;

    public void Start() => Emit(new CliJsonEvent("run_start"));

    public void SetModel(string model) => _model = model;

    public ValueTask PublishAsync(AgentProgressEvent progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (progress.Kind is AgentProgressEventKind.HookStarted or AgentProgressEventKind.HookCompleted or AgentProgressEventKind.HookFailed)
        {
            Emit(new CliJsonEvent("hook_event")
            {
                Ts = progress.OccurredAt,
                Event = new CliJsonAgentEvent(progress.Kind.ToString())
                {
                    TaskId = progress.TaskId,
                    Description = progress.Description,
                    Text = progress.Message,
                },
            });
            return ValueTask.CompletedTask;
        }

        if (progress.Kind == AgentProgressEventKind.ModelStarted)
        {
            _iterations++;
            AgentEvent(new CliJsonAgentEvent("iteration_start"));
        }
        else if (progress.Kind == AgentProgressEventKind.ModelCompleted)
        {
            AgentEvent(new CliJsonAgentEvent("iteration_end"));
        }

        Emit(new CliJsonEvent("agent_event")
        {
            Ts = progress.OccurredAt,
            Event = new CliJsonAgentEvent("progress")
            {
                Kind = progress.Kind.ToString(),
                TaskId = progress.TaskId,
                Description = progress.Description,
                Text = progress.Message,
            },
        });
        return ValueTask.CompletedTask;
    }

    public void Complete(int exitCode, double durationMs)
    {
        if (_contentStarted)
        {
            AgentEvent(new CliJsonAgentEvent("content_end"));
        }

        var reason = exitCode switch
        {
            0 => "completed",
            124 or 130 => "aborted",
            _ => "failed",
        };
        AgentEvent(new CliJsonAgentEvent("done") { Reason = reason });
        Emit(new CliJsonEvent("run_result")
        {
            FinishReason = reason,
            ExitCode = exitCode,
            Text = _text.ToString(),
            DurationMs = durationMs,
            Model = _model,
            Usage = _usage,
            Iterations = _iterations,
        });
        if (exitCode is 124 or 130)
        {
            Emit(new CliJsonEvent("run_aborted")
            {
                Reason = exitCode == 124 ? "timeout" : "cancelled",
                ExitCode = exitCode,
            });
        }
    }

    public Task<string?> ReadInputAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }

    public Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WriteError($"Approval unavailable in non-interactive mode: {question}");
        return Task.FromResult(false);
    }

    public void WriteLine(string message)
    {
        if (!string.IsNullOrEmpty(message))
        {
            WriteInfo(message);
        }
    }

    public void WriteError(string message) => _diagnostics.WriteError(message);

    public void WriteSuccess(string message) => WriteInfo(message);

    public void WriteInfo(string message) => AgentEvent(new CliJsonAgentEvent("notice") { Text = message });

    public void WriteMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return;
        }

        if (!_contentStarted)
        {
            AgentEvent(new CliJsonAgentEvent("content_start"));
            _contentStarted = true;
        }

        _text.Append(markdown);
        AgentEvent(new CliJsonAgentEvent("content_update") { Text = markdown });
    }

    public Task<T> RunWithProgressAsync<T>(string label, Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        WriteInfo(label);
        return work(cancellationToken);
    }

    public void ShowTokens(string model, TokenUsage session, TokenUsage last, decimal cost)
    {
        _model = model;
        _usage = new CliJsonUsage(session.InputTokens, session.OutputTokens, cost);
        AgentEvent(new CliJsonAgentEvent("usage")
        {
            Model = model,
            Usage = new CliJsonUsage(last.InputTokens, last.OutputTokens, cost),
        });
    }

    public void Clear()
    {
    }

    private void AgentEvent(CliJsonAgentEvent progress) =>
        Emit(new CliJsonEvent("agent_event") { Event = progress });

    private void Emit(CliJsonEvent progress) =>
        output.WriteLine(JsonSerializer.Serialize(progress, CliJsonContext.Default.CliJsonEvent));
}
