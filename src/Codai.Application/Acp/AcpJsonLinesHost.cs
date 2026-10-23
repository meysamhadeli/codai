using System.Text.Json;
using System.Threading.Channels;
using Codai.Core.Agents;

namespace Codai.Application.Acp;

/// <summary>Host-neutral JSON-lines adapter for Codai's ACP compatibility subset.</summary>
/// <remarks>
/// Supports initialize, session creation/resume, prompts, mode switching, permission
/// responses, progress/output events, cancellation, and structured errors. It does not
/// implement the full ACP/JSON-RPC specification, authentication, binary content, or
/// capability negotiation.
/// </remarks>
public sealed class AcpJsonLinesHost : IDisposable
{
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly IAcpSessionFactory _factory;
    private readonly Dictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);
    private readonly object _writeGate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<JsonDocument> _messages = Channel.CreateUnbounded<JsonDocument>();
    private readonly List<Task> _operations = [];
    private int _nextSession;

    public AcpJsonLinesHost(TextReader input, TextWriter output, IAcpSessionFactory factory)
    {
        _input = input;
        _output = output;
        _factory = factory;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var reader = ReadMessagesAsync(linked.Token);
        try
        {
            while (await _messages.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false))
            {
                while (_messages.Reader.TryRead(out var document))
                {
                    try
                    {
                        await DispatchAsync(document.RootElement, linked.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
                    {
                        EmitError("invalid_request", ex.Message);
                    }
                    finally
                    {
                        document.Dispose();
                    }
                }
            }

            await Task.WhenAll(_operations).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            Emit(new { type = "cancelled" });
            return cancellationToken.IsCancellationRequested ? 130 : 0;
        }
        catch (Exception ex)
        {
            EmitError("host_error", ex.Message);
            return 1;
        }
        finally
        {
            _stop.Cancel();
            foreach (var session in _sessions.Values)
            {
                session.Handle.Lifetime?.Dispose();
            }
            try { await reader.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    public void Dispose() => _stop.Dispose();

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    _messages.Writer.TryWrite(JsonDocument.Parse(line));
                }
                catch (JsonException ex)
                {
                    EmitError("invalid_json", ex.Message);
                }
            }
        }
        finally
        {
            _messages.Writer.TryComplete();
        }
    }

    private async Task DispatchAsync(JsonElement message, CancellationToken cancellationToken)
    {
        var type = RequiredString(message, "type");
        switch (type)
        {
            case "initialize":
                Emit(new { type = "initialized", protocol = "codai-acp-compat", version = 1 });
                break;
            case "new_session":
                CreateSession(message);
                break;
            case "set_mode":
                var modeState = GetSession(message).Handle.Mode;
                modeState.Mode = ParseMode(RequiredString(message, "mode"));
                Emit(new { type = "mode_changed", sessionId = RequiredString(message, "sessionId"), mode = AgentModeParser.ToName(modeState.Mode) });
                break;
            case "set_model":
                EmitError("unsupported_model_change", "Create a new session with provider/model to change model selection.", RequiredString(message, "sessionId"));
                break;
            case "prompt":
                _operations.Add(RunPromptAsync(message, cancellationToken));
                break;
            case "cancel":
                GetSession(message).Cancel();
                Emit(new { type = "cancel_requested", sessionId = RequiredString(message, "sessionId") });
                break;
            case "permission_response":
                GetSession(message).Permission.TrySetResult(message.TryGetProperty("approved", out var approved) && approved.GetBoolean());
                break;
            case "shutdown":
                _stop.Cancel();
                break;
            default:
                EmitError("unknown_message", $"Unsupported message type '{type}'.");
                break;
        }
    }

    private void CreateSession(JsonElement message)
    {
        var id = message.TryGetProperty("sessionId", out var supplied) && supplied.ValueKind == JsonValueKind.String
            ? supplied.GetString()!
            : $"session-{Interlocked.Increment(ref _nextSession)}";
        if (_sessions.ContainsKey(id))
        {
            throw new InvalidOperationException($"Session '{id}' already exists.");
        }

        var state = new SessionState(id, Emit);
        var request = new AcpSessionRequest(
            RequiredString(message, "cwd"),
            OptionalString(message, "provider"),
            OptionalString(message, "model"),
            message.TryGetProperty("mode", out var mode) ? ParseMode(mode.GetString()!) : AgentModeParser.Default,
            message.TryGetProperty("resume", out var resume) && resume.GetBoolean(),
            OptionalString(message, "taskId"),
            OptionalString(message, "dataDirectory"));
        state.Handle = _factory.Create(request, state.Ui);
        _sessions.Add(id, state);
        Emit(new { type = "session_created", sessionId = id, mode = AgentModeParser.ToName(state.Handle.Mode.Mode), provider = state.Handle.Options.Provider, model = state.Handle.Options.Model });
    }

    private async Task RunPromptAsync(JsonElement message, CancellationToken cancellationToken)
    {
        var state = GetSession(message);
        state.CancelSource.Dispose();
        state.CancelSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var code = await state.Handle.Session.RunTaskAsync(RequiredString(message, "text"), state.CancelSource.Token).ConfigureAwait(false);
            Emit(new { type = "prompt_complete", sessionId = state.Id, exitCode = code });
        }
        catch (OperationCanceledException)
        {
            Emit(new { type = "prompt_cancelled", sessionId = state.Id });
        }
        catch (Exception ex)
        {
            EmitError("session_error", ex.Message, state.Id);
        }
    }

    private SessionState GetSession(JsonElement message) =>
        _sessions.TryGetValue(RequiredString(message, "sessionId"), out var state)
            ? state
            : throw new KeyNotFoundException("Unknown session.");

    private static AgentMode ParseMode(string value) =>
        AgentModeParser.TryParse(value, out var mode) ? mode : throw new ArgumentException($"Unknown mode '{value}'.");

    private static string RequiredString(JsonElement message, string name) =>
        message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new ArgumentException($"Message requires non-empty '{name}'.");

    private static string? OptionalString(JsonElement message, string name) =>
        message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private void Emit(object value)
    {
        lock (_writeGate)
        {
            _output.WriteLine(JsonSerializer.Serialize(value));
            _output.Flush();
        }
    }

    private void EmitError(string code, string message, string? sessionId = null) => Emit(new { type = "error", code, message, sessionId });

    private sealed class SessionState
    {
        public SessionState(string id, Action<object> emit)
        {
            Id = id;
            Emit = emit;
            Permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Ui = new AcpJsonLinesUserInterface(emit, ConfirmAsync);
        }

        public string Id { get; }
        private Action<object> Emit { get; }
        public AcpSessionHandle Handle { get; set; } = null!;
        public AcpJsonLinesUserInterface Ui { get; }
        public TaskCompletionSource<bool> Permission { get; set; }
        public CancellationTokenSource CancelSource { get; set; } = new();

        public void Cancel() => CancelSource.Cancel();

        private async Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken)
        {
            Emit(new { type = "permission_request", sessionId = Id, question });
            var approved = await Permission.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            Permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return approved;
        }
    }
}