using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Context;
using Codai.Core.Tokens;
using Codai.Core.Tools;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codai.Cli.Ui;

/// <summary>
/// <see cref="IUserInterface"/> implementation on top of Spectre.Console.
/// </summary>
public sealed class SpectreUserInterface : IUserInterface, IDisposable
{
    private readonly IAnsiConsole _console;
    private readonly IConsoleInput _input;
    private readonly string? _provider;
    private readonly string? _model;
    private readonly AgentModeState _modeState;
    private readonly ApprovalState _approvalState;
    private readonly string? _projectRoot;
    private readonly ChatTranscript _transcript = new();
    private readonly object _renderLock = new();
    private readonly SessionDisplayState _state = new();
    private const int ComposerRows = 6;
    private const int MinimumLabelWidth = 12;
    private const int FrameIntervalMs = 60;
    private static readonly TimeSpan MinimumDrawInterval = TimeSpan.FromMilliseconds(45);
    private bool WideWelcome => _console.Profile.Width >= 64 && TerminalHeight() >= 16;
    private int HeaderRows => WideWelcome ? 7 : 4;

    private bool _welcomeShown;
    private bool _viewportInitialized;
    private int _composerTop;
    private int _composerRows = ComposerRows;
    private int _renderWidth;
    private int _renderHeight;
    private int _busyDepth;
    private int _activityFrame;
    private long _lastDrawTicks;
    private readonly ActivityStatus _activity = new();
    private string _loadingLabel = "Loading";
    private volatile bool _waitingForResponse;
    private ComposerBuffer _draft = new([]);
    private readonly ComposerCompletion _draftCompletion = new();
    private readonly Queue<string> _queuedInputs = new();
    private IOverlay? _palette;
    private string? _paletteCommand;
    private bool _restorePaletteBackground;
    private bool _draggingScrollbar;
    private TaskCompletionSource<string?>? _dialogResult;
    private string _theme = "graphite";
    private string Accent => _theme switch { "forest" => "green", "ember" => "yellow", _ => "deepskyblue1" };
    internal bool ConnectionSetup { get; init; }
    public bool UsesAccountPlan { get; init; }
    private IOverlay? _setupPaintedPalette;
    private string _setupNotice = string.Empty;
    private string _setupNoticeStyle = "grey";
    private static readonly Spinner _unicodePulse = new PulseSpinner(true);
    private static readonly Spinner _asciiPulse = new PulseSpinner(false);
    private Spinner WorkSpinner => _console.Profile.Capabilities.Unicode ? _unicodePulse : _asciiPulse;
    private string WorkFrame => WorkSpinner.Frames[_activityFrame / 2 % WorkSpinner.Frames.Count];

    private Markup RenderActivity(string activity, int width, ActivitySnapshot? snapshot = null)
    {
        var glyphColor = snapshot?.Stalled == true ? "yellow" : Accent;
        var parts = new List<string>();
        if (snapshot is { ShowMetrics: true, Tokens: > 0 })
        {
            parts.Insert(0, (WorkSpinner.IsUnicode ? "↓ " : "v ") + ActivityStatus.FormatTokens(snapshot.Tokens) + " tokens");
        }

        if (snapshot is { ShowMetrics: true })
        {
            parts.Insert(0, ActivityStatus.FormatElapsed(snapshot.Elapsed));
        }

        if (snapshot?.Interruptible == true)
        {
            parts.Insert(0, "esc to interrupt");
        }

        // Width decides what survives: tokens go first, then the timer, then the interrupt hint,
        // so a narrow terminal still says what the agent is doing before it says anything else.
        var available = Math.Max(4, width - 3);
        while (parts.Count > 0 && SuffixWidth(parts) + MinimumLabelWidth > available)
        {
            parts.RemoveAt(parts.Count - 1);
        }

        var suffix = parts.Count == 0
            ? string.Empty
            : " [grey](" + string.Join("[grey] · [/]", parts.Select(Markup.Escape)) + ")[/]";
        var label = ComposerLayout.Clip(TranscriptMarkdown.Clean(activity).Replace('\n', ' '),
            Math.Max(1, available - (suffix.Length == 0 ? 0 : SuffixWidth(parts) + 1)));
        var highlight = label.Length == 0 ? 0 : _activityFrame % (label.Length + 3);
        var start = Math.Min(highlight, label.Length);
        var end = Math.Min(highlight + 3, label.Length);
        var dots = parts.Count == 0 ? new string('.', _activityFrame / 3 % 4).PadRight(3) : string.Empty;
        return new Markup($"[{glyphColor}]{WorkFrame} [/][grey]{Markup.Escape(label[..start])}[/]"
            + $"[bold {glyphColor}]{Markup.Escape(label[start..end])}[/][grey]{Markup.Escape(label[end..])}[/]{suffix}[{glyphColor}]{dots}[/]");
    }

    private static int SuffixWidth(List<string> parts) =>
        parts.Sum(part => part.Length) + Math.Max(0, parts.Count - 1) * 3 + 2;

    private sealed class PulseSpinner(bool unicode) : Spinner
    {
        public override TimeSpan Interval => TimeSpan.FromMilliseconds(160);
        public override bool IsUnicode => unicode;
        public override IReadOnlyList<string> Frames { get; } = unicode
            ? ["·", "✧", "✦", "✶", "✦", "✧"]
            : [".", "+", "*", "+", ".", "*"];
    }

    private sealed class ActivityColumn(SpectreUserInterface owner) : ProgressColumn
    {
        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            owner._activityFrame = (int)(task.ElapsedTime?.TotalMilliseconds ?? 0) / 80;
            return owner.RenderActivity(task.Description.TrimEnd('.'), owner._console.Profile.Width);
        }
    }

    public Task<string?> ChooseAsync(string title, IReadOnlyList<SessionMenuItem> items, CancellationToken cancellationToken = default) =>
        RunDialogAsync(new SettingsPalette(title, items), cancellationToken);

    public Task<string?> ReadValueAsync(string title, string initialValue = "", bool secret = false, CancellationToken cancellationToken = default) =>
        RunDialogAsync(new SettingsPalette(title, initialValue: initialValue, secret: secret), cancellationToken);

    public void SetTheme(string name)
    {
        _theme = name;
        _restorePaletteBackground = true;
        RefreshComposer();
    }

    public void InsertInput(string text)
    {
        if (_input is SystemConsoleInput input)
        {
            input.PendingInsertion = text;
        }
        else
        {
            _draft.Insert(text);
        }
    }

    private async Task<string?> RunDialogAsync(IOverlay palette, CancellationToken cancellationToken)
    {
        if (!_console.Profile.Capabilities.Ansi || !_console.Profile.Capabilities.Interactive)
        {
            WriteInfo("Interactive settings require a terminal.");
            return null;
        }
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (ConnectionSetup)
        {
            InitializeViewport();
        }
        var waiting = _waitingForResponse;
        lock (_renderLock)
        {
            _palette = palette;
            _dialogResult = completion;
            _waitingForResponse = true;
            RedrawInput(_draft.Text, _draft.Cursor);
        }
        try
        {
            while (!completion.Task.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_input.TryReadKey(out var key))
                {
                    HandlePaletteKey(key);
                    RedrawInput(_draft.Text, _draft.Cursor);
                }
                else
                {
                    await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                    if (_renderWidth != _console.Profile.Width || _renderHeight != TerminalHeight())
                    {
                        RedrawInput(_draft.Text, _draft.Cursor);
                    }
                }
            }
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_renderLock)
            {
                _palette = null;
                _dialogResult = null;
                _waitingForResponse = waiting;
                _restorePaletteBackground = true;
                RedrawInput(_draft.Text, _draft.Cursor);
            }
        }
    }

    /// <summary>Creates the user interface.</summary>
    /// <param name="console">Console to render to. Defaults to <see cref="AnsiConsole.Console"/>.</param>
    /// <param name="input">Input source. Defaults to standard input.</param>
    /// <param name="provider">Provider label shown in the session header.</param>
    /// <param name="model">Model label shown in the session header.</param>
    /// <param name="mode">Initial agent mode shown in the session header.</param>
    /// <param name="autoApprove">Whether the session skips approval prompts.</param>
    /// <param name="projectRoot">Workspace path shown in the session header.</param>
    /// <param name="modeState">Shared mutable mode state for keyboard shortcuts.</param>
    /// <param name="approvalState">Shared mutable approval state for keyboard shortcuts.</param>
    /// <param name="theme">Initial appearance selected by configuration.</param>
    public SpectreUserInterface(
        IAnsiConsole? console = null,
        IConsoleInput? input = null,
        string? provider = null,
        string? model = null,
        AgentMode mode = AgentMode.Agent,
        bool autoApprove = false,
        string? projectRoot = null,
        AgentModeState? modeState = null,
        ApprovalState? approvalState = null,
        string theme = "graphite")
    {
        _console = console ?? AnsiConsole.Console;
        _input = input ?? SystemConsoleInput.Instance;
        _provider = provider;
        _model = model;
        _modeState = modeState ?? new AgentModeState(mode);
        _approvalState = approvalState ?? new ApprovalState(autoApprove);
        _projectRoot = projectRoot;
        _theme = theme;
        if (_input is SystemConsoleInput systemInput)
        {
            systemInput.KeyHandler = HandleShortcut;
            systemInput.MouseHandler = HandleMouse;
            systemInput.OverlayKeyHandler = HandlePaletteKey;
            systemInput.PaletteCommandHandler = TakePaletteCommand;
            systemInput.RedrawHandler = RedrawInput;
            systemInput.ComposerRedrawHandler = RedrawComposer;
            systemInput.CompletionRoot = _projectRoot;
            systemInput.Placeholder = "Ask anything...";
        }
    }

    /// <inheritdoc />
    public async Task<string?> ReadInputAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            InitializeViewport();
            await RenderWelcomeIfNeededAsync(cancellationToken).ConfigureAwait(false);
            var homeView = IsHomeView();
            _state.Activity = "ready";
            if (_console.Profile.Capabilities.Ansi && _console.Profile.Capabilities.Interactive)
            {
                RedrawInput(_draft.Text, _draft.Cursor);
            }
            else
            {
                _console.Write("> ");
            }
            var input = await ReadAndTrackInputAsync(cancellationToken).ConfigureAwait(false);
            if (_console.Profile.Capabilities.Interactive && !string.IsNullOrWhiteSpace(input))
            {
                lock (_renderLock)
                {
                    if (_viewportInitialized && homeView)
                    {
                        _console.Write(new ControlCode("\u001b[r\u001b[2J\u001b[H"));
                        _composerTop = 0;
                    }
                    _transcript.AddUser(input);
                    RefreshComposer();
                }
            }
            return input;
        }
        catch
        {
            ClearComposer();
            RestoreTerminal();
            throw;
        }
    }

    public async Task<string?> AskQuestionAsync(string question, IReadOnlyList<string> options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_console.Profile.Capabilities.Ansi && _console.Profile.Capabilities.Interactive)
        {
            if (options.Count == 0)
            {
                return await RunDialogAsync(new SettingsPalette(question, question: true), cancellationToken).ConfigureAwait(false);
            }
            var items = options.Select((option, index) => new SessionMenuItem($"{index + 1}. {option}", string.Empty,
                index.ToString(CultureInfo.InvariantCulture))).ToList();
            items.Add(new SessionMenuItem("Write another answer", string.Empty, "custom"));
            var selected = await RunDialogAsync(new SettingsPalette(question, items, question: true), cancellationToken).ConfigureAwait(false);
            if (selected == "custom")
            {
                return await RunDialogAsync(new SettingsPalette(question, question: true), cancellationToken).ConfigureAwait(false);
            }
            return int.TryParse(selected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0 && index < options.Count ? options[index] : null;
        }
        var waiting = _waitingForResponse;
        _waitingForResponse = true;
        try
        {
            WriteLine(question);
            for (var index = 0; index < options.Count; index++)
            {
                WriteLine($"{index + 1}. {options[index]}");
            }
            MoveToTranscriptEnd();
            var answer = await _input.ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return int.TryParse(answer, NumberStyles.Integer, CultureInfo.InvariantCulture, out var selected)
                && selected > 0 && selected <= options.Count ? options[selected - 1] : answer;
        }
        finally
        {
            _waitingForResponse = waiting;
            RefreshComposer();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_console.Profile.Capabilities.Interactive)
        {
            // Non-interactive hosts (pipes, CI) cannot answer prompts: reject by default.
            WriteInfo(question);
            return false;
        }

        _state.Activity = "waiting for approval";
        _waitingForResponse = true;
        Present(question + " [y/N]", "yellow");
        MoveToTranscriptEnd();
        try
        {
            var answer = await _input.ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            var accepted = string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
            Present(accepted ? "Approved" : "Declined", accepted ? "green" : "yellow");
            return accepted;
        }
        finally
        {
            _waitingForResponse = false;
            _state.Activity = "ready";
            RefreshComposer();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The panel replaces the one-line prompt instead of joining it, because the text being approved
    /// is often the whole point: a clipped command or a bare path is not enough to decide on. It is
    /// also drawn over the transcript rather than appended to it, so the question does not scroll
    /// away while the user reads it, and the answer is reported back into the transcript afterwards
    /// so the record of what was allowed survives.
    /// </remarks>
    public async Task<ApprovalDecision> ApproveAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_console.Profile.Capabilities.Interactive || !_console.Profile.Capabilities.Ansi || _viewportInitialized == false)
        {
            return await ConfirmAsync(
                $"Allow {request.ToolName}: {request.DetailOrDefault}",
                cancellationToken).ConfigureAwait(false)
                ? ApprovalDecision.AllowOnce
                : ApprovalDecision.Deny;
        }

        _state.Activity = "waiting for approval";
        var answer = await RunDialogAsync(
            new ApprovalPanel(request, _console.Profile.Capabilities.Unicode),
            cancellationToken).ConfigureAwait(false);
        var decision = answer switch
        {
            ApprovalPanel.Allow => ApprovalDecision.AllowOnce,
            ApprovalPanel.AllowAlways => ApprovalDecision.AllowAlways,
            _ => ApprovalDecision.Deny,
        };
        WriteInfo(decision switch
        {
            ApprovalDecision.AllowOnce => $"Approved {request.ToolName}.",
            ApprovalDecision.AllowAlways => $"Approved {request.ToolName} for this session.",
            _ => $"Declined {request.ToolName}.",
        });
        return decision;
    }

    /// <inheritdoc />
    public void WriteLine(string message)
    {
        _state.Activity = "working";
        Present(message);
    }

    /// <inheritdoc />
    public void WriteError(string message)
    {
        _state.Activity = "error";
        lock (_renderLock)
        {
            _transcript.CompleteActivity("failed");
            Present("! " + message, "red");
        }
    }

    /// <inheritdoc />
    public void WriteSuccess(string message)
    {
        _state.Activity = "ready";
        Present(message, "green");
    }

    /// <inheritdoc />
    public void WriteInfo(string message)
    {
        if (message.StartsWith("Auto-approved: ", StringComparison.Ordinal))
        {
            return;
        }
        if (message is "Thinking..." or "Reasoning..." or "Loading..." or "Working...")
        {
            lock (_renderLock)
            {
                _state.Activity = message switch
                {
                    "Reasoning..." => "reasoning",
                    "Loading..." => "loading",
                    "Working..." => "working",
                    _ => "thinking",
                };
                _transcript.StartActivity(message.TrimEnd('.'));
                RefreshComposer();
            }
            return;
        }
        Present(message, "grey");
    }

    /// <inheritdoc />
    public void WriteMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return;
        }

        lock (_renderLock)
        {
            _state.Activity = "generating";
            _activity.Observe(markdown.Length);
            if (!_viewportInitialized)
            {
                _console.Write(TranscriptMarkdown.Render(markdown));
                return;
            }
            _transcript.AppendMarkdown(markdown);
            RequestRefresh();
        }
    }

    public void WriteToolStarted(string toolName, string summary)
    {
        lock (_renderLock)
        {
            _state.Activity = "running " + toolName;
            _activity.ClearOutput();
            _activity.Pulse();
            _transcript.StartTool(toolName, summary);
            RefreshComposer();
        }
    }

    public void WriteToolStarted(string toolName, string summary, JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            WriteToolStarted(toolName, string.Empty);
            return;
        }
        var fields = new[] { "path", "paths", "command", "pattern", "query", "url" };
        var labels = fields.Where(name => arguments.TryGetProperty(name, out _)).Select(name =>
        {
            var value = arguments.GetProperty(name);
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
        });
        WriteToolStarted(toolName, string.Join("  ", labels));
    }

    public void WriteToolCompleted(string toolName, bool succeeded, string detail)
    {
        lock (_renderLock)
        {
            _transcript.CompleteTool(toolName, succeeded, detail);
            _state.Activity = succeeded ? "thinking" : "tool failed";
            _activity.Pulse();
            RefreshComposer();
        }
    }

    public void WriteToolDenied(string toolName, string summary)
    {
        WriteToolStarted(toolName, summary);
        WriteToolCompleted(toolName, false, "Not approved");
    }

    public void WriteToolOutput(string toolName, ProcessOutput output)
    {
        lock (_renderLock)
        {
            _activity.Pulse();
            _transcript.ToolOutput(toolName, output.Text);
            RequestRefresh();
        }
    }

    public void WriteToolResult(string toolName, JsonElement arguments, ToolResult result)
    {
        lock (_renderLock)
        {
            _transcript.ToolResult(toolName, result);
            RefreshComposer();
        }
    }

    /// <inheritdoc />
    public async Task<T> RunWithProgressAsync<T>(
        string label,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var previousLabel = _loadingLabel;
        _loadingLabel = string.IsNullOrWhiteSpace(label) ? "Loading" : label.TrimEnd('.');
        _state.Activity = "loading";

        try
        {
            if (!_console.Profile.Capabilities.Interactive)
            {
                return await work(cancellationToken).ConfigureAwait(false);
            }
            if (_viewportInitialized)
            {
                if (ConnectionSetup)
                {
                    using var setupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    _busyDepth++;
                    try
                    {
                        RefreshComposer();
                        var pending = Task.Run(() => work(setupCancellation.Token), setupCancellation.Token);
                        while (!pending.IsCompleted)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await Task.WhenAny(pending, Task.Delay(FrameIntervalMs, CancellationToken.None)).ConfigureAwait(false);
                            if (!pending.IsCompleted && _input.TryReadKey(out var key) && key.Key == ConsoleKey.Escape)
                            {
                                setupCancellation.Cancel();
                            }
                            _activityFrame++;
                            RefreshComposer();
                        }
                        return await pending.ConfigureAwait(false);
                    }
                    finally
                    {
                        _busyDepth--;
                        RefreshComposer();
                    }
                }
                return _busyDepth > 0
                    ? await work(cancellationToken).ConfigureAwait(false)
                    : await RunTurnAsync(token =>
                    {
                        _state.Activity = "loading";
                        RefreshComposer();
                        return work(token);
                    }, cancellationToken).ConfigureAwait(false);
            }
            return await _console
                .Progress()
                .AutoClear(true)
                .Columns(new ActivityColumn(this))
                .StartAsync(async context =>
                {
                    var task = context.AddTask(string.IsNullOrWhiteSpace(label) ? "Working" : label);
                    task.IsIndeterminate = true;
                    try
                    {
                        return await Task.Run(() => work(cancellationToken), cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        task.StopTask();
                    }
                })
                .ConfigureAwait(false);
        }
        finally
        {
            _loadingLabel = previousLabel;
            _state.Activity = "ready";
        }
    }

    public async Task<T> RunTurnAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!_viewportInitialized)
        {
            return await work(cancellationToken).ConfigureAwait(false);
        }
        using var turnCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _busyDepth++;
        var stopped = false;
        var completed = false;
        try
        {
            _state.Activity = "thinking";
            _activity.Begin("Thinking");
            RefreshComposer();
            var pending = Task.Run(() => work(turnCancellation.Token), turnCancellation.Token);
            while (!pending.IsCompleted)
            {
                await Task.WhenAny(pending, Task.Delay(FrameIntervalMs, CancellationToken.None)).ConfigureAwait(false);
                if (_waitingForResponse || pending.IsCompleted)
                {
                    continue;
                }
                if (_input.TryReadKey(out var key))
                {
                    if (HandlePaletteKey(key))
                    {
                    }
                    else if (_draftCompletion.HandleKey(key, _draft))
                    {
                    }
                    else if (key.Key == ConsoleKey.Escape)
                    {
                        stopped = true;
                        turnCancellation.Cancel();
                    }
                    else if (key.Key is ConsoleKey.PageUp or ConsoleKey.PageDown or ConsoleKey.Tab ||
                        key.Key == ConsoleKey.O && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        HandleShortcut(key);
                    }
                    else if (key.Key == ConsoleKey.Enter && key.Modifiers == ConsoleModifiers.None)
                    {
                        if (!string.IsNullOrWhiteSpace(_draft.Text) && _queuedInputs.Count < 8)
                        {
                            _queuedInputs.Enqueue(_draft.Text);
                            _draft = new ComposerBuffer([]);
                            _draftCompletion.Update(_draft, []);
                        }
                    }
                    else if (_draft.Text.Length < 16384 || char.IsControl(key.KeyChar))
                    {
                        _draft.HandleKey(key);
                        _draftCompletion.Update(_draft, []);
                    }
                }
                _activityFrame++;
                RefreshComposer();
            }
            var result = await pending.ConfigureAwait(false);
            completed = true;
            return result;
        }
        finally
        {
            _busyDepth--;
            _state.Activity = "ready";
            lock (_renderLock)
            {
                _transcript.CompleteActivity(stopped || cancellationToken.IsCancellationRequested ? "stopped"
                    : completed ? "done" : "failed");
                if (stopped || !completed)
                {
                    _transcript.StopRunningTools();
                }
            }
            if (stopped && !cancellationToken.IsCancellationRequested)
            {
                Present("Request stopped. Session remains open.", "yellow");
            }
            RefreshComposer();
        }
    }

    /// <inheritdoc />
    public void ShowTokens(string model, TokenUsage session, TokenUsage last, decimal cost)
    {
        _state.Model = model;
        _state.TotalTokens = session.TotalTokens;
        _state.ContextTokens = last.TotalTokens;
        _state.Cost = cost;
        _state.Activity = "ready";
        RefreshComposer();
    }

    private void RefreshComposer()
    {
        if (!_viewportInitialized)
        {
            return;
        }
        lock (_renderLock)
        {
            RedrawComposer(_draft.Text, _draft.Cursor, _busyDepth > 0 ? _draftCompletion.Options : [], _draftCompletion.Selected);
        }
    }

    /// <summary>Redraws at most once per frame while work is running.</summary>
    /// <remarks>
    /// Streamed answers and command output arrive as a burst of small writes. Drawing each one
    /// separately would repaint the transcript hundreds of times a second, which is both visible as
    /// tearing and slower than the work it is trying to show. The busy loop redraws on its own
    /// cadence, so anything skipped here is on screen one frame later.
    /// </remarks>
    private void RequestRefresh()
    {
        if (!_viewportInitialized)
        {
            return;
        }

        if (_busyDepth == 0)
        {
            RefreshComposer();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_lastDrawTicks != 0 && Stopwatch.GetElapsedTime(_lastDrawTicks, now) < MinimumDrawInterval)
        {
            return;
        }

        _lastDrawTicks = now;
        RefreshComposer();
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_renderLock)
        {
            _transcript.Clear();
            RefreshComposer();
        }

        if (!_console.Profile.Capabilities.Ansi)
        {
            return;
        }

        if (IsHomeView())
        {
            _console.Write(new ControlCode("\u001b[2J\u001b[H"));
        }
    }

    private void Present(string message, string style = "default")
    {
        if (ConnectionSetup)
        {
            _setupNotice = message;
            _setupNoticeStyle = style;
            RefreshComposer();
            return;
        }
        lock (_renderLock)
        {
            if (_viewportInitialized && !IsHomeView())
            {
                _transcript.Add(message, style);
                RefreshComposer();
            }
            else
            {
                _console.Write(new Text(TranscriptMarkdown.Clean(message), Style.Parse(style)));
                _console.WriteLine();
            }
        }
    }

    private async Task RenderWelcomeIfNeededAsync(CancellationToken cancellationToken)
    {
        if (_welcomeShown)
        {
            return;
        }

        _welcomeShown = true;

        var project = string.IsNullOrWhiteSpace(_projectRoot)
            ? "current workspace"
            : Path.TrimEndingDirectorySeparator(_projectRoot);
        var provider = string.IsNullOrWhiteSpace(_provider) ? "configured provider" : _provider;
        var model = string.IsNullOrWhiteSpace(_model) ? "configured model" : _model;
        _state.Workspace = project;
        _state.Provider = provider;
        _state.Model = model;

        if (_console.Profile.Capabilities.Ansi)
        {
            _console.Write(new ControlCode($"\u001b[{HomeTop()};1H"));
        }

        if (WideWelcome && _input is SystemConsoleInput input && !input.IsRedirected &&
            _console.Profile.Capabilities.Ansi && _console.Profile.Capabilities.Interactive)
        {
            var width = _console.Profile.Width;
            var height = TerminalHeight();
            try
            {
                for (var frame = 0; frame < 30; frame++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var animationFrame = Console.KeyAvailable || !WideWelcome ? -1 : frame;
                    lock (_renderLock)
                    {
                        _console.Write(new ControlCode("\u001b[?2026h\u001b[?25l\u001b[r"));
                        if (width != _console.Profile.Width || height != TerminalHeight())
                        {
                            width = _console.Profile.Width;
                            height = TerminalHeight();
                            _console.Write(new ControlCode("\u001b[2J"));
                        }
                        _console.Write(new ControlCode($"\u001b[{HomeTop()};1H"));
                        RenderHeaderBlock(animationFrame, clearRows: true);
                        _console.Write(new ControlCode("\u001b[?2026l"));
                    }
                    if (animationFrame < 0)
                    {
                        break;
                    }
                    if (frame < 29)
                    {
                        await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _console.Write(new ControlCode("\u001b[?25h\u001b[?2026l"));
            }
        }
        else
        {
            RenderHeaderBlock();
        }
    }

    private void RenderHeaderBlock(int animationFrame = -1, bool clearRows = false)
    {
        string[] mark = WideWelcome && (!ConnectionSetup || TerminalHeight() >= 28)
            ? [" ___   ___   ___    _    ___ ", "/ __| / _ \\ |   \\  /_\\  |_ _|", "| (__| (_) || |) |/ _ \\  | | ", "\\___| \\___/ |___//_/ \\_\\|___|"]
            : ["CODAI"];
        var markWidth = mark.Max(value => value.GetCellWidth());
        var available = Math.Max(1, _console.Profile.Width - 2);
        var hint = available >= 34 ? "Commands /  Files @  Menu Ctrl+P" : "/  @  Ctrl+P";
        var animated = animationFrame >= 0 && WideWelcome;
        var bounceFrame = animated && animationFrame is >= 8 and < 14 ? animationFrame - 8 : -1;
        int[] sway = [-3, 2, -2, 1, -1, 0];
        var indent = Math.Max(0, (_console.Profile.Width - markWidth) / 2 + (bounceFrame >= 0 ? sway[bounceFrame] : 0));
        var drop = bounceFrame is 0 or 2 ? 1 : 0;
        var revealedColumns = animated && animationFrame < 8 ? (animationFrame + 1) * 4 : int.MaxValue;
        var sweep = animated && animationFrame is >= 14 and < 28 ? (animationFrame - 14) * 3 - 3 : -10;
        for (var row = 0; row <= mark.Length; row++)
        {
            if (clearRows)
            {
                _console.Write(new ControlCode("\u001b[2K"));
            }
            _console.Write(new string(' ', indent));
            var markRow = row - drop;
            if (markRow >= 0 && markRow < mark.Length)
            {
                var line = ComposerLayout.Clip(mark[markRow], Math.Min(available, revealedColumns));
                var highlightStart = Math.Clamp(sweep, 0, line.Length);
                var highlightEnd = Math.Clamp(sweep + 3, highlightStart, line.Length);
                _console.Write(new Text(line[..highlightStart], Style.Parse("bold " + Accent)));
                _console.Write(new Text(line[highlightStart..highlightEnd], Style.Parse("bold white")));
                _console.Write(new Text(line[highlightEnd..], Style.Parse("bold " + Accent)));
            }
            else if (animated && row == mark.Length && animationFrame is >= 14 and < 28)
            {
                var sparkleColumn = Math.Clamp(sweep + 1, 0, markWidth - 1);
                _console.Write(new string(' ', sparkleColumn));
                _console.Write(new Text(animationFrame % 3 == 0 ? "+" : "*", Style.Parse("bold " + Accent)));
            }
            _console.WriteLine();
        }
        CenteredLine($"Codai v{CodaiDefaults.Version}", "bold");
        if (!ConnectionSetup)
        {
            CenteredLine(hint, "italic grey");
        }

        void CenteredLine(string value, string style)
        {
            var text = ComposerLayout.Clip(value, available);
            _console.Write(new string(' ', Math.Max(0, (_console.Profile.Width - text.GetCellWidth()) / 2)));
            _console.Write(new Text(text, Style.Parse(style)));
            _console.WriteLine();
        }
    }

    private int HomeTop()
    {
        var area = Math.Max(1, TerminalHeight() - ComposerRows);
        var block = HeaderRows + 1 + ComposerRows;
        return Math.Max(1, (area - block) / 2);
    }

    private void RedrawInput(string text, int cursor) => RedrawComposer(text, cursor, [], 0);

    private void RedrawComposer(string text, int cursor, IReadOnlyList<string> suggestions, int selected)
    {
        lock (_renderLock)
        {
            var ansi = _console.Profile.Capabilities.Ansi;
            if (ansi)
            {
                _console.Write(new ControlCode("\u001b[?2026h\u001b[?25l"));
            }
            try
            {
                RenderComposer(text, cursor, suggestions, selected);
            }
            finally
            {
                if (ansi)
                {
                    _console.Write(new ControlCode("\u001b[?25h\u001b[?2026l"));
                }
            }
        }
    }

    private void RenderComposer(string text, int cursor, IReadOnlyList<string> suggestions, int selected)
    {
        if (ConnectionSetup)
        {
            RenderConnectionPage();
            return;
        }
        var homeView = IsHomeView();
        var resized = _renderWidth != 0 && (_renderWidth != _console.Profile.Width || _renderHeight != TerminalHeight());
        if (resized && _palette is not null)
        {
            _restorePaletteBackground = true;
        }
        _renderWidth = _console.Profile.Width;
        _renderHeight = TerminalHeight();
        if (_restorePaletteBackground && _console.Profile.Capabilities.Ansi)
        {
            _console.Write(new ControlCode("\u001b[r\u001b[2J\u001b[H"));
            _composerTop = 0;
            _transcript.InvalidateDisplay();
            _restorePaletteBackground = false;
            if (homeView)
            {
                _console.Write(new ControlCode($"\u001b[{HomeTop()};1H"));
                RenderHeaderBlock();
            }
        }
        else if (resized && homeView && _console.Profile.Capabilities.Ansi)
        {
            _console.Write(new ControlCode($"\u001b[r\u001b[2J\u001b[{HomeTop()};1H"));
            _composerTop = 0;
            RenderHeaderBlock();
        }
        else
        {
            ClearComposer();
        }
        var width = homeView ? ComposerWidth() : ChatWidth();
        var layout = ComposerLayout.Create(text, cursor, width - 2, Math.Min(3, Math.Max(1, TerminalHeight() - 9)));
        var optionCount = Math.Min(suggestions.Count, Math.Max(0, TerminalHeight() - layout.Lines.Count - 9));
        var activityRows = _busyDepth > 0 && !_waitingForResponse ? 1 : 0;
        _composerRows = 5 + layout.Lines.Count + optionCount + activityRows;
        var indent = Math.Max(0, (_console.Profile.Width - width) / 2);
        var top = homeView
            ? Math.Min(HomeTop() + HeaderRows + 1, Math.Max(1, TerminalHeight() - _composerRows + 1))
            : Math.Max(1, TerminalHeight() - _composerRows + 1);
        _composerTop = top;

        if (_console.Profile.Capabilities.Ansi)
        {
            _console.Write(new ControlCode($"\u001b[r\u001b[{top};1H"));
        }

        if (activityRows > 0)
        {
            _activity.Label = ActivityLabel();
            _activity.Interruptible = true;
            _console.Write(new string(' ', indent));
            _console.Write(RenderActivity(_activity.Label, width, _activity.Snapshot()));
            _console.WriteLine();
        }
        var rule = new string('─', width);
        _console.Write(new string(' ', indent));
        _console.MarkupLine($"[grey]{rule}[/]");
        var promptReady = _busyDepth == 0 && !_waitingForResponse;
        for (var row = 0; row < layout.Lines.Count; row++)
        {
            _console.Write(new string(' ', indent));
            var marker = promptReady ? "›" : " ";
            _console.Markup(row == 0 ? $"[bold {Accent}]{marker}[/] " : "  ");
            if (text.Length == 0)
            {
                var placeholder = promptReady ? "What shall we work on?" : string.Empty;
                _console.Write(new Text(ComposerLayout.Clip(placeholder, width - 2), Style.Parse("grey")));
            }
            else
            {
                _console.Write(layout.Lines[row]);
            }
            _console.WriteLine();
        }
        var firstOption = Math.Clamp(selected - optionCount + 1, 0, Math.Max(0, suggestions.Count - optionCount));
        for (var row = firstOption; row < firstOption + optionCount; row++)
        {
            _console.Write(new string(' ', indent));
            var label = (row == selected ? "> " : "  ") + suggestions[row];
            _console.Write(new Text(ComposerLayout.Clip(label, width), Style.Parse(row == selected ? "bold " + Accent : "grey")));
            _console.WriteLine();
        }
        _console.Write(new string(' ', indent));
        _console.MarkupLine($"[grey]{rule}[/]");
        RenderStatus(width, indent);

        if (_console.Profile.Capabilities.Ansi)
        {
            var transcriptBottom = Math.Max(1, TerminalHeight() - _composerRows);
            if (_viewportInitialized && !homeView)
            {
                _transcript.Draw(_console, top - 1);
            }
            _console.Write(new ControlCode($"\u001b[1;{transcriptBottom}r\u001b[{top + activityRows + 1 + layout.CursorRow};{indent + 3 + layout.CursorColumn}H"));
            if (_palette is not null)
            {
                _console.Write(new ControlCode("\u001b[r"));
                _palette.Draw(_console, _theme);
            }
        }
    }

    private void RenderConnectionPage()
    {
        var resized = _renderWidth != _console.Profile.Width || _renderHeight != TerminalHeight();
        var changed = !ReferenceEquals(_setupPaintedPalette, _palette);
        _renderWidth = _console.Profile.Width;
        _renderHeight = TerminalHeight();
        _setupPaintedPalette = _palette;
        var wide = WideWelcome && TerminalHeight() >= 28;
        var menuTop = wide ? 9 : 5;
        if (resized || changed)
        {
            _console.Write(new ControlCode($"\u001b[r\u001b[2J\u001b[{(wide ? 2 : 1)};1H"));
            RenderHeaderBlock();
        }
        var notice = ComposerLayout.Clip(TranscriptMarkdown.Clean(_setupNotice).Replace('\n', ' '), Math.Max(1, _console.Profile.Width - 4));
        _console.Write(new ControlCode($"\u001b[{Math.Max(1, TerminalHeight() - 1)};1H\u001b[2K"));
        _console.Write(new Text("  " + notice, Style.Parse(_setupNoticeStyle)));
        if (_palette is not null)
        {
            _palette.Draw(_console, _theme, Math.Min(menuTop, Math.Max(1, TerminalHeight() - 6)));
        }
        else
        {
            _console.Write(new ControlCode($"\u001b[{Math.Min(menuTop + 1, TerminalHeight() - 2)};1H\u001b[2K"));
            if (_busyDepth > 0)
            {
                var width = Math.Max(6, Math.Min(40, _console.Profile.Width - 4));
                _console.Write(new string(' ', Math.Max(0, (_console.Profile.Width - width) / 2)));
                _console.Write(RenderActivity(_loadingLabel, width));
            }
        }
    }

    private bool IsHomeView() => !_welcomeShown || _state.Turn == 0;

    private string ActivityLabel() => _state.Activity switch
    {
        "reasoning" => "Reasoning",
        "loading" => _loadingLabel,
        "working" => "Working",
        "generating" => "Responding",
        var value when value.StartsWith("running ", StringComparison.Ordinal) => "Running " + value[8..],
        _ => "Thinking",
    };

    private void ClearComposer()
    {
        if (!_console.Profile.Capabilities.Ansi || _composerTop <= 0)
        {
            return;
        }

        var rows = Math.Min(_composerRows, Math.Max(0, TerminalHeight() - _composerTop + 1));
        for (var row = 0; row < rows; row++)
        {
            _console.Write(new ControlCode($"\u001b[{_composerTop + row};1H\u001b[2K"));
        }
    }

    private void InitializeViewport()
    {
        if (_viewportInitialized || !_console.Profile.Capabilities.Ansi)
        {
            return;
        }

        if (_input is SystemConsoleInput input)
        {
            input.StartMouseTracking();
        }
        _viewportInitialized = true;
        var transcriptBottom = Math.Max(1, TerminalHeight() - ComposerRows);
        _console.Write(new ControlCode($"\u001b[?1049h\u001b[?1002h\u001b[?1006h\u001b[2J\u001b[1;1H\u001b[1;{transcriptBottom}r"));
    }

    private void MoveToTranscriptEnd()
    {
        if (_console.Profile.Capabilities.Ansi)
        {
            _console.Write(new ControlCode($"\u001b[{Math.Max(1, TerminalHeight() - _composerRows)};1H"));
        }
    }

    public void RestoreTerminal()
    {
        if (_viewportInitialized && _console.Profile.Capabilities.Ansi)
        {
            _viewportInitialized = false;
            _draggingScrollbar = false;
            try
            {
                if (_input is SystemConsoleInput input)
                {
                    input.StopMouseTracking();
                }
            }
            finally
            {
                _console.Write(new ControlCode("\u001b[?2026l\u001b[r\u001b[0m\u001b[?25h\u001b[?1002l\u001b[?1006l\u001b[?1049l"));
            }
        }
        _composerTop = 0;
    }

    public void Dispose()
    {
        RestoreTerminal();
        if (_input is SystemConsoleInput input && input.KeyHandler == HandleShortcut)
        {
            input.KeyHandler = null;
            input.MouseHandler = null;
            input.OverlayKeyHandler = null;
            input.PaletteCommandHandler = null;
            input.RedrawHandler = null;
            input.ComposerRedrawHandler = null;
            input.CompletionRoot = null;
            input.Placeholder = null;
            input.InitialText = string.Empty;
        }
    }

    private int TerminalHeight() => Math.Max(8, _console.Profile.Height);

    private int ComposerWidth()
    {
        return Math.Max(3, Math.Min(80, _console.Profile.Width - 2));
    }

    private int ChatWidth()
    {
        return Math.Max(3, _console.Profile.Width - 2);
    }

    private void RenderStatus(int width, int indent)
    {
        var mode = _modeState.Mode;
        var modes = string.Join("  ", Enum.GetValues<AgentMode>().Select(value => value == mode ? $"[{value}]" : value.ToString()));

        void StatusLine(string value, string style = "grey", bool newline = true)
        {
            _console.Write(new string(' ', indent));
            _console.Write(new Text(ComposerLayout.Clip(value, width), Style.Parse(style)));
            if (newline)
            {
                _console.WriteLine();
            }
        }

        var model = _state.Model;
        var usage = UsesAccountPlan
            ? $"({_state.TotalTokens.ToString("N0", CultureInfo.InvariantCulture)}) ChatGPT plan"
            : $"({_state.TotalTokens.ToString("N0", CultureInfo.InvariantCulture)}) ${_state.Cost.ToString("0.00", CultureInfo.InvariantCulture)}";
        var modeLabel = width >= 60 ? modes + " (Tab)" : $"[{mode}]";
        var usageWidth = Math.Max(0, width - modeLabel.GetCellWidth() - 2);
        var barWidth = usageWidth >= usage.GetCellWidth() + 10 ? 6 : 0;
        var modelWidth = Math.Max(0, usageWidth - usage.GetCellWidth() - (barWidth > 0 ? barWidth + 2 : 1));
        var modelLabel = ComposerLayout.Clip(model, modelWidth);
        var leftWidth = 0;
        _console.Write(new string(' ', indent));
        if (modelLabel.Length > 0)
        {
            _console.Write(new Text(modelLabel + " ", Style.Parse("grey")));
            leftWidth += modelLabel.GetCellWidth() + 1;
        }
        if (barWidth > 0)
        {
            var fraction = Math.Clamp((double)_state.ContextTokens / ContextWindow.For(model), 0, 1);
            var filled = (int)Math.Ceiling(fraction * barWidth);
            _console.Write(new Text(new string(' ', filled), Style.Parse("on grey")));
            _console.Write(new Text(new string(' ', barWidth - filled), Style.Parse("on grey23")));
            _console.Write(" ");
            leftWidth += barWidth + 1;
        }
        var usageLabel = ComposerLayout.Clip(usage, Math.Max(0, usageWidth - leftWidth));
        _console.Write(UsesAccountPlan
            ? new Markup("[grey link=https://chatgpt.com/settings/usage]" + Markup.Escape(usageLabel) + "[/]")
            : new Text(usageLabel, Style.Parse("grey")));
        leftWidth += usageLabel.GetCellWidth();
        _console.Write(new Text(new string(' ', Math.Max(1, width - leftWidth - modeLabel.GetCellWidth())), Style.Parse("grey")));
        var activeLabel = $"[{mode}]";
        var activeStart = modeLabel.IndexOf(activeLabel, StringComparison.Ordinal);
        _console.Write(new Text(modeLabel[..activeStart], Style.Parse("grey")));
        _console.Write(new Text(ComposerLayout.Clip(activeLabel, width), Style.Parse("deepskyblue1")));
        _console.Write(new Text(modeLabel[(activeStart + activeLabel.Length)..], Style.Parse("grey")));
        _console.WriteLine();
        StatusLine(string.IsNullOrWhiteSpace(_projectRoot) ? "current workspace" : _projectRoot);
        var approvalLabel = ComposerLayout.Clip(_approvalState.AutoApprove ? "Auto-approval ON" : "Approval required", width);
        _console.Write(new string(' ', indent));
        _console.Write(new Text(approvalLabel, Style.Parse(_approvalState.AutoApprove ? "green" : "grey")));
        _console.Write(new Text(ComposerLayout.Clip("  (Shift+Tab)", Math.Max(0, width - approvalLabel.GetCellWidth())), Style.Parse("grey")));
    }

    private async Task<string?> ReadAndTrackInputAsync(CancellationToken cancellationToken)
    {
        string? input;
        if (_queuedInputs.TryDequeue(out var queued))
        {
            input = queued;
        }
        else
        {
            if (_input is SystemConsoleInput systemInput)
            {
                systemInput.InitialText = _draft.Text;
            }
            _draft = new ComposerBuffer([]);
            input = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
        if (!string.IsNullOrWhiteSpace(input))
        {
            _state.Turn++;
            _state.Activity = "working";
        }

        return input;
    }

    internal bool HandleMouse(TerminalMouseEvent mouse)
    {
        lock (_renderLock)
        {
            if (mouse.Action == TerminalMouseAction.Release)
            {
                _draggingScrollbar = false;
                return false;
            }
            if (!_viewportInitialized || IsHomeView() || ConnectionSetup || _palette is not null)
            {
                _draggingScrollbar = false;
                return false;
            }
            var rows = Math.Max(1, _composerTop - 1);
            if (mouse.Action is TerminalMouseAction.ScrollUp or TerminalMouseAction.ScrollDown)
            {
                _transcript.Scroll(mouse.Action == TerminalMouseAction.ScrollUp ? 3 : -3);
            }
            else if (mouse.Action == TerminalMouseAction.Press &&
                mouse.Column >= _console.Profile.Width - 1 && mouse.Row > 0 && mouse.Row <= rows)
            {
                _draggingScrollbar = true;
                _transcript.ScrollToRow(mouse.Row - 1, rows);
            }
            else if (mouse.Action == TerminalMouseAction.Drag && _draggingScrollbar)
            {
                _transcript.ScrollToRow(mouse.Row - 1, rows);
            }
            else if (mouse.Action == TerminalMouseAction.Press && mouse.Column > 0 &&
                mouse.Column < _console.Profile.Width - 1 && mouse.Row > 0 && mouse.Row <= rows &&
                _transcript.ToggleStepAtRow(mouse.Row, rows))
            {
                _draggingScrollbar = false;
            }
            else
            {
                return false;
            }
            if (_input is not SystemConsoleInput { IsReadingComposer: true })
            {
                RefreshComposer();
            }
            return true;
        }
    }

    private bool HandleShortcut(ConsoleKeyInfo key)
    {
        if (HandlePaletteKey(key))
        {
            return true;
        }
        if (key.Key is ConsoleKey.PageUp or ConsoleKey.PageDown)
        {
            lock (_renderLock)
            {
                _transcript.Scroll(key.Key == ConsoleKey.PageUp ? Math.Max(1, TerminalHeight() - _composerRows - 2) : -Math.Max(1, TerminalHeight() - _composerRows - 2));
                RefreshComposer();
            }
            return true;
        }
        if (key.Key == ConsoleKey.O && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            lock (_renderLock)
            {
                _transcript.ToggleTool();
                RefreshComposer();
            }
            return true;
        }
        if (key.Key == ConsoleKey.Tab && key.Modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            _approvalState.AutoApprove = !_approvalState.AutoApprove;
            return true;
        }

        if (key.Key == ConsoleKey.Tab && key.Modifiers == ConsoleModifiers.None)
        {
            _modeState.Mode = _modeState.Mode switch
            {
                AgentMode.Plan => AgentMode.Agent,
                _ => AgentMode.Plan,
            };
            return true;
        }

        return false;
    }

    private string? TakePaletteCommand()
    {
        var command = _paletteCommand;
        _paletteCommand = null;
        return command;
    }

    private bool HandlePaletteKey(ConsoleKeyInfo key)
    {
        lock (_renderLock)
        {
            var shortcut = key.Key == ConsoleKey.F1 ||
                key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key is ConsoleKey.M or ConsoleKey.Enter or ConsoleKey.P;
            if (_palette is null)
            {
                if (!shortcut || !_console.Profile.Capabilities.Ansi)
                {
                    return false;
                }
                _palette = new SettingsPalette(_modeState.Mode);
                return true;
            }
            if (shortcut)
            {
                _palette = null;
                _restorePaletteBackground = true;
                _dialogResult?.TrySetResult(null);
                return true;
            }
            _palette.HandleKey(key);
            if (!_palette.Closed)
            {
                return true;
            }
            var command = _palette.Command;
            _palette = null;
            _restorePaletteBackground = true;
            if (_dialogResult is not null)
            {
                _dialogResult.TrySetResult(command);
                return true;
            }
            if (command == "approval")
            {
                _approvalState.AutoApprove = !_approvalState.AutoApprove;
            }
            else if (command?.StartsWith("mode:", StringComparison.Ordinal) == true)
            {
                _modeState.Mode = Enum.Parse<AgentMode>(command[5..]);
            }
            else if (command is not null)
            {
                if (_busyDepth > 0)
                {
                    if (_queuedInputs.Count < 8)
                    {
                        _queuedInputs.Enqueue(command);
                    }
                    else
                    {
                        Present("Request queue is full. Try the setting after this response.", "yellow");
                    }
                }
                else
                {
                    _paletteCommand = command;
                }
            }
            return true;
        }
    }

    private sealed class SessionDisplayState
    {
        public int Turn { get; set; }

        public long TotalTokens { get; set; }

        public long ContextTokens { get; set; }

        public decimal Cost { get; set; }

        public string Workspace { get; set; } = "current workspace";

        public string Provider { get; set; } = "configured provider";

        public string Model { get; set; } = "configured model";

        public string Activity { get; set; } = "ready";
    }
}
