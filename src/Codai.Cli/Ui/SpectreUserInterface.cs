using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Codai.Application;
using Codai.Core;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
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
    private readonly Action<Uri> _openBrowser;
    private const string DocumentationUrl = "https://meysamhadeli.github.io/codai";
    private readonly string? _provider;
    private readonly string? _model;
    private readonly AgentModeState _modeState;
    private readonly ApprovalState _approvalState;
    private readonly string? _projectRoot;
    private readonly ChatTranscript _transcript = new();
    private readonly object _renderLock = new();
    private readonly SessionDisplayState _state = new();
    private const int ComposerRows = 8;
    private const int FrameIntervalMs = 60;
    private static readonly TimeSpan MinimumDrawInterval = TimeSpan.FromMilliseconds(45);
    private bool WideWelcome => _console.Profile.Width >= 64 && TerminalHeight() >= 16;
    private int HeaderRows => WideWelcome ? 6 : 3;

    private bool _welcomeShown;
    private bool _viewportInitialized;
    private int _composerTop;
    private int _composerRows = ComposerRows;
    private int _renderWidth;
    private int _renderHeight;
    private int _busyDepth;
    private int _activityFrame;
    private long _lastDrawTicks;
    private ActivityStatus _activity = new();
    private readonly List<(Guid Id, ActivityStatus Status)> _modelRequests = [];
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
    private string _theme = TerminalThemes.Default.Id;
    private string Accent => TerminalThemes.Find(_theme).Accent;
    internal bool ConnectionSetup { get; init; }
    public bool UsesAccountPlan { get; init; }
    private IOverlay? _setupPaintedPalette;
    private string _setupNotice = string.Empty;
    private string _setupNoticeStyle = "grey";
    private readonly DotMatrixSpinner _unicodeDots = new(true);
    private readonly DotMatrixSpinner _asciiDots = new(false);
    private DotMatrixSpinner WorkSpinner => _console.Profile.Capabilities.Unicode ? _unicodeDots : _asciiDots;
    private string WorkFrame => WorkSpinner.GetFrame(_activityFrame / 2);

    /// <inheritdoc />
    public void ModelRequestStarted(Guid requestId)
    {
        lock (_renderLock)
        {
            var label = _state.Activity == "loading" ? _loadingLabel : "Thinking";
            var status = new ActivityStatus();
            status.Begin(label);
            _modelRequests.Add((requestId, status));
            _activity = status;
            _transcript.StartActivity(label);
            RefreshComposer();
        }
    }

    /// <inheritdoc />
    public void ModelRequestUpdated(Guid requestId, ChatStreamUpdate update)
    {
        lock (_renderLock)
        {
            var index = _modelRequests.FindIndex(request => request.Id == requestId);
            if (index < 0)
            {
                return;
            }
            var status = _modelRequests[index].Status;
            if (!string.IsNullOrEmpty(update.ContentDelta))
            {
                status.Observe(update.ContentDelta.Length);
            }
            if (update.IsReasoning)
            {
                status.SetReasoning(true);
            }
            if (update.Usage.HasValue)
            {
                status.ReportUsage(update.Usage);
            }
            if (update.IsCompleted)
            {
                status.SetReasoning(false);
            }
            if (ReferenceEquals(status, _activity))
            {
                RequestRefresh();
            }
        }
    }

    /// <inheritdoc />
    public void ModelRequestCompleted(Guid requestId)
    {
        lock (_renderLock)
        {
            var index = _modelRequests.FindIndex(request => request.Id == requestId);
            if (index < 0)
            {
                return;
            }
            _modelRequests[index].Status.SetReasoning(false);
            _modelRequests.RemoveAt(index);
            if (_modelRequests.Count > 0)
            {
                _activity = _modelRequests[^1].Status;
            }
            RefreshComposer();
        }
    }

    private Markup RenderActivity(string activity, int width)
    {
        var snapshot = _activity.Snapshot();
        var metrics = FormatActivityMetrics(snapshot.Elapsed, snapshot.Tokens,
            _console.Profile.Capabilities.Unicode, _viewportInitialized && _busyDepth > 0, width,
            snapshot.TokensEstimated, _modelRequests.Count > 0 || snapshot.Tokens > 0 || snapshot.InputTokens > 0,
            snapshot.InputTokens);
        var label = ComposerLayout.Clip(TranscriptMarkdown.Clean(activity).Replace('\n', ' ').TrimEnd('.') + "...",
            Math.Max(0, width - WorkFrame.GetCellWidth() - metrics.GetCellWidth() - 2));
        return new Markup($"[{Accent}]{WorkFrame}[/] [{Accent}]{Markup.Escape(label)}[/] [grey]{Markup.Escape(metrics)}[/]");
    }

    internal static string FormatActivityMetrics(TimeSpan elapsed, long tokens, bool unicode, bool interruptible, int width,
        bool estimated = true, bool showTokens = true, long inputTokens = 0)
    {
        var separator = unicode ? " · " : " - ";
        var metrics = ActivityStatus.FormatElapsed(elapsed);
        if (showTokens && width >= 64)
        {
            if (inputTokens > 0 && width >= 96)
            {
                metrics += separator + (unicode ? "\u2191 " : "in ") + ActivityStatus.FormatTokens(inputTokens) + (unicode ? " in" : string.Empty);
            }
            metrics += separator + (unicode ? "\u2193 " : "out ") + (estimated ? "~" : string.Empty) + ActivityStatus.FormatTokens(tokens) + " tokens";
        }
        if (interruptible)
        {
            metrics += separator + (width >= 40 ? "esc interrupt" : "esc");
        }
        return "(" + metrics + ")";
    }

    internal sealed class DotMatrixSpinner : Spinner
    {
        private readonly Random _random;
        private string[] _frames;
        private int _cycle;
        private int _previousPattern = -1;

        public DotMatrixSpinner(bool unicode, Random? random = null)
        {
            IsUnicode = unicode;
            _random = random ?? Random.Shared;
            _frames = CreateFrames();
        }

        public override TimeSpan Interval => TimeSpan.FromMilliseconds(160);
        public override bool IsUnicode { get; }
        public override IReadOnlyList<string> Frames => _frames;

        public string GetFrame(int frame)
        {
            var cycle = frame / _frames.Length;
            if (cycle != _cycle)
            {
                _frames = CreateFrames();
                _cycle = cycle;
            }
            return _frames[frame % _frames.Length];
        }

        private string[] CreateFrames()
        {
            var count = IsUnicode ? 9 : 4;
            var frames = new string[count * 2];
            var mask = 0;
            frames[0] = Encode(mask);
            var fill = NextOrder(count);
            for (var index = 0; index < count; index++)
            {
                mask |= 1 << fill[index];
                frames[index + 1] = Encode(mask);
            }
            var empty = NextOrder(count);
            for (var index = 0; index < count - 1; index++)
            {
                mask &= ~(1 << empty[index]);
                frames[count + index + 1] = Encode(mask);
            }
            return frames;
        }

        private int[] NextOrder(int count)
        {
            var pattern = _previousPattern < 0 ? _random.Next(5) : (_previousPattern + _random.Next(1, 5)) % 5;
            _previousPattern = pattern;
            int[] order = pattern switch
            {
                0 => [0, 1, 2, 3, 4, 5, 6, 7, 8],
                1 => [0, 3, 6, 1, 4, 7, 2, 5, 8],
                2 => [0, 1, 2, 5, 8, 7, 6, 3, 4],
                3 => [4, 1, 5, 7, 3, 0, 2, 8, 6],
                _ => [0, 1, 2, 3, 4, 5, 6, 7, 8],
            };
            order = order.Where(point => point < count).ToArray();
            if (pattern == 4)
            {
                _random.Shuffle(order);
            }
            else if (_random.Next(2) == 1)
            {
                Array.Reverse(order);
            }
            return order;
        }

        private string Encode(int mask)
        {
            if (!IsUnicode)
            {
                const string dots = " ..:";
                return $"{dots[mask & 3]}{dots[mask >> 2]}";
            }
            var left = 0;
            var right = 0;
            for (var point = 0; point < 9; point++)
            {
                if ((mask & (1 << point)) == 0)
                {
                    continue;
                }
                var row = point / 3;
                var column = point % 3;
                if (column < 2)
                {
                    left |= 1 << (row + column * 3);
                }
                else
                {
                    right |= 1 << row;
                }
            }
            return $"{(char)('\u2800' + left)}{(char)('\u2800' + right)}";
        }
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

    internal async Task<string?> RequestWorkspaceTrustAsync(string directory, CancellationToken cancellationToken)
    {
        if (_console.Profile.Capabilities.Ansi && _console.Profile.Capabilities.Interactive)
        {
            return await RunDialogAsync(new WorkspaceTrustPanel(directory), cancellationToken).ConfigureAwait(false);
        }

        new WorkspaceTrustPanel(directory).Draw(_console, _theme);
        _console.WriteLine();
        _console.WriteLine("Trust this directory? [Y/n]");
        var response = await _input.ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        return response is not null && (string.IsNullOrWhiteSpace(response) ||
            string.Equals(response.Trim(), "y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(response.Trim(), "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(response.Trim(), "trust", StringComparison.OrdinalIgnoreCase)) ? "trust" : "leave";
    }

    public Task<string?> ChooseAsync(string title, IReadOnlyList<SessionMenuItem> items, SessionMenuOptions options, CancellationToken cancellationToken = default) =>
        RunDialogAsync(new SettingsPalette(title, items, options: options), cancellationToken);

    public Task<string?> ReadValueAsync(string title, string initialValue = "", bool secret = false, CancellationToken cancellationToken = default) =>
        RunDialogAsync(new SettingsPalette(title, initialValue: initialValue, secret: secret), cancellationToken);

    public async Task ShowDiffAsync(string workingTree, string staged, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_console.Profile.Capabilities.Ansi || !_console.Profile.Capabilities.Interactive)
        {
            WriteLine("Working-tree changes");
            WriteLine(string.IsNullOrWhiteSpace(workingTree) ? "No diff!" : workingTree);
            WriteLine("Staged changes");
            WriteLine(string.IsNullOrWhiteSpace(staged) ? "No diff!" : staged);
            return;
        }
        await RunDialogAsync(new DiffViewer(workingTree, staged), cancellationToken).ConfigureAwait(false);
    }

    public void SetTheme(string name)
    {
        _theme = name;
        _transcript.Accent = Accent;
        _transcript.Invalidate();
        _restorePaletteBackground = true;
        RefreshComposer();
    }

    /// <inheritdoc />
    public void SetSkills(IReadOnlyList<string> skillNames)
    {
        ArgumentNullException.ThrowIfNull(skillNames);
        var names = skillNames.ToArray();
        _draftCompletion.SkillNames = names;
        if (_input is SystemConsoleInput input)
        {
            input.SkillNames = names;
        }
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

    /// <summary>
    /// Routes a paste shortcut to an overlay with a text field. The payload is consumed
    /// only after the overlay opts in, so an unhandled paste is not dropped silently.
    /// </summary>
    internal static bool TryRoutePalettePaste(IOverlay palette, IConsoleInput input, ConsoleKeyInfo key)
    {
        if (!IConsoleInput.IsPasteKey(key) || !palette.AcceptsPaste)
        {
            return false;
        }
        if (!input.TryConsumePaste(out var paste) || paste is not { Length: > 0 })
        {
            return false;
        }
        palette.HandlePaste(paste);
        return true;
    }

    private async Task<string?> RunDialogAsync(IOverlay palette, CancellationToken cancellationToken)
    {
        if (!_console.Profile.Capabilities.Ansi || !_console.Profile.Capabilities.Interactive)
        {
            WriteInfo("Interactive settings require a terminal.");
            return null;
        }
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        InitializeViewport();
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
                    if (!TryRoutePalettePaste(_palette, _input, key))
                    {
                        HandlePaletteKey(key);
                    }
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
                _restorePaletteBackground = palette is not IInlineDecision;
                _transcript.InvalidateDisplay();
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
    /// <param name="openBrowser">Optional browser launcher for the command palette's documentation action.</param>
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
        string theme = "graphite",
        Action<Uri>? openBrowser = null)
    {
        _console = console ?? AnsiConsole.Console;
        _input = input ?? SystemConsoleInput.Instance;
        _openBrowser = openBrowser ?? (address =>
        {
            using var browser = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
        });
        _provider = provider;
        _model = model;
        _state.Model = string.IsNullOrWhiteSpace(model) ? "configured model" : model;
        _modeState = modeState ?? new AgentModeState(mode);
        _approvalState = approvalState ?? new ApprovalState(autoApprove);
        _projectRoot = projectRoot;
        _theme = theme;
        _transcript.Accent = Accent;
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
            var command = SlashCommandParser.Parse(input);
            var localControl = command == SlashCommand.Settings ||
                input?.TrimStart().StartsWith('/') == true && command is not (SlashCommand.None or SlashCommand.Init);
            if (_console.Profile.Capabilities.Interactive && !string.IsNullOrWhiteSpace(input) && !localControl)
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
                return await RunDialogAsync(new DecisionPanel("Question", question), cancellationToken).ConfigureAwait(false);
            }
            var items = options.Select((option, index) => new SessionMenuItem(option, string.Empty,
                index.ToString(CultureInfo.InvariantCulture))).ToList();
            items.Add(new SessionMenuItem("Write another answer", string.Empty, "custom"));
            var selected = await RunDialogAsync(new DecisionPanel("Question", question, items), cancellationToken).ConfigureAwait(false);
            if (selected == "custom")
            {
                return await RunDialogAsync(new DecisionPanel("Question", question), cancellationToken).ConfigureAwait(false);
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

        var waiting = _waitingForResponse;
        var activity = _state.Activity;
        _state.Activity = "waiting for approval";
        try
        {
            bool accepted;
            if (_console.Profile.Capabilities.Ansi)
            {
                var answer = await RunDialogAsync(new DecisionPanel("Confirm action", question,
                    [new SessionMenuItem("Yes", "Continue", "yes"), new SessionMenuItem("No", "Keep current state", "no")]),
                    cancellationToken).ConfigureAwait(false);
                accepted = answer == "yes";
            }
            else
            {
                _waitingForResponse = true;
                Present(question + " [Y/n]", Accent);
                MoveToTranscriptEnd();
                var answer = await _input.ReadResponseAsync(cancellationToken).ConfigureAwait(false);
                accepted = answer is not null && (string.IsNullOrWhiteSpace(answer) ||
                    string.Equals(answer.Trim(), "y", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(answer.Trim(), "yes", StringComparison.OrdinalIgnoreCase));
            }
            cancellationToken.ThrowIfCancellationRequested();
            Present(accepted ? "Approved" : "Declined", Accent);
            return accepted;
        }
        finally
        {
            _waitingForResponse = waiting;
            _state.Activity = activity;
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
        Present(message, Accent);
    }

    /// <inheritdoc />
    public void WriteInfo(string message)
    {
        if (message.StartsWith("Auto-approved: ", StringComparison.Ordinal))
        {
            return;
        }
        if (message is "Thinking..." or "Reasoning..." or "Loading..." or "Working..." or "Considering..." or "Ruminating..."
            or "Planning..." or "Analyzing..." or "Searching..." or "Reviewing..." or "Compacting...")
        {
            lock (_renderLock)
            {
                if (message == "Reasoning...")
                {
                    _activity.SetReasoning(true);
                }
                _state.Activity = message switch
                {
                    "Reasoning..." => "reasoning",
                    "Loading..." => "loading",
                    "Working..." => "working",
                    _ => message.TrimEnd('.').ToLowerInvariant(),
                };
                _transcript.StartActivity(message.TrimEnd('.'));
                RefreshComposer();
            }
            return;
        }
        Present(message, "white");
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
            if (_modelRequests.Count == 0)
            {
                _activity.Observe(markdown.Length);
            }
            if (!_viewportInitialized)
            {
                _console.Write(TranscriptMarkdown.Render(markdown));
                return;
            }
            _transcript.AppendMarkdown(markdown, completeActivity: _busyDepth == 0);
            RequestRefresh();
        }
    }

    public void WriteToolStarted(string toolName, string summary)
    {
        lock (_renderLock)
        {
            _state.Activity = "running " + toolName;
            if (_modelRequests.Count == 0)
            {
                _activity.ClearOutput();
            }
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
            if (_busyDepth == 0)
            {
                _activity.Begin(_loadingLabel);
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
            var initialActivity = _state.Activity == "loading" ? _loadingLabel : "Thinking";
            _state.Activity = "thinking";
            _activity.Begin("Thinking");
            lock (_renderLock)
            {
                _transcript.StartActivity(initialActivity);
            }
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
                    else if (_input is SystemConsoleInput systemInput && systemInput.TryPaste(key, _draft))
                    {
                        _draftCompletion.Update(_draft, []);
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
                WriteInfo("Request stopped. Session remains open.");
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
                    _console.Write(new ControlCode(_palette is IInlineDecision { ShowCursor: false } or SettingsPalette { ShowCursor: false }
                        ? "\u001b[?25l\u001b[?2026l" : "\u001b[2 q\u001b[?25h\u001b[?2026l"));
                }
            }
        }
    }

    private void RenderComposer(string text, int cursor, IReadOnlyList<string> suggestions, int selected)
    {
        var activity = _activity.Snapshot();
        _transcript.OutputTokens = _busyDepth > 0 ? activity.Tokens : 0;
        _transcript.ModelMetrics = _busyDepth > 0 ? activity : null;
        _transcript.ShowActivityTokens = _modelRequests.Count > 0 || activity.Tokens > 0 || activity.InputTokens > 0;
        if (_palette is IInlineDecision decision && _console.Profile.Capabilities.Ansi)
        {
            ClearComposer();
            _renderWidth = _console.Profile.Width;
            _renderHeight = TerminalHeight();
            var height = decision.MeasureHeight(_console.Profile.Width, Math.Max(1, TerminalHeight() - 4));
            var transcriptRows = _transcript.GetLines(Math.Max(4, _console.Profile.Width - 3)).Count;
            var decisionTop = Math.Max(2, Math.Min(transcriptRows + 2, TerminalHeight() - height));
            _composerTop = decisionTop;
            _composerRows = TerminalHeight() - decisionTop + 1;
            _console.Write(new ControlCode("\u001b[r"));
            _transcript.Draw(_console, decisionTop - 1);
            decision.Draw(_console, _theme, decisionTop);
            return;
        }
        if (ConnectionSetup)
        {
            RenderConnectionPage();
            return;
        }
        var homeView = IsHomeView() && _busyDepth == 0;
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
        _transcript.Unicode = _console.Profile.Capabilities.Unicode;
        var layout = ComposerLayout.Create(text, cursor, PromptBlock.ContentWidth(width), Math.Min(12, Math.Max(1, TerminalHeight() - 10)));
        var contentRows = Math.Max(layout.Lines.Count, 1);
        var inputPadding = TerminalHeight() >= 12 ? 1 : 0;
        var optionCount = Math.Min(suggestions.Count, Math.Max(0, TerminalHeight() - contentRows - 10));
        _composerRows = 2 + inputPadding * 2 + contentRows + optionCount;
        var indent = Math.Max(0, (_console.Profile.Width - width) / 2);
        var top = homeView
            ? Math.Min(HomeTop() + HeaderRows + 1, Math.Max(1, TerminalHeight() - _composerRows + 1))
            : Math.Min(Math.Max(1, TerminalHeight() - _composerRows + 1),
                _transcript.GetLines(Math.Max(4, _console.Profile.Width - 3)).Count + 2);
        var moved = _composerTop != top;
        _composerTop = top;

        if (_console.Profile.Capabilities.Ansi)
        {
            if (resized || moved)
            {
                for (var row = top; row <= TerminalHeight(); row++)
                {
                    _console.Write(new ControlCode($"\u001b[{row};1H\u001b[2K"));
                }
            }
            _console.Write(new ControlCode($"\u001b[r\u001b[{top};1H"));
        }

        var unicode = _console.Profile.Capabilities.Unicode;
        var promptReady = homeView && _busyDepth == 0 && !_waitingForResponse;
        if (inputPadding > 0)
        {
            _console.Write(new string(' ', indent));
            _console.Write(PromptBlock.Border(width, unicode));
            _console.WriteLine();
        }
        for (var row = 0; row < contentRows; row++)
        {
            _console.Write(new string(' ', indent));
            var content = row < layout.Lines.Count ? layout.Lines[row] : string.Empty;
            if (row == 0 && text.Length == 0 && promptReady)
            {
                content = "What shall we work on?";
            }
            _console.Write(PromptBlock.Row(content, width, Accent, unicode, text.Length == 0 ? "grey62" : "white", isInput: true, showPrompt: row == 0));
            _console.WriteLine();
        }
        var firstOption = Math.Clamp(selected - optionCount + 1, 0, Math.Max(0, suggestions.Count - optionCount));
        for (var row = firstOption; row < firstOption + optionCount; row++)
        {
            _console.Write(new string(' ', indent));
            var label = (row == selected ? "> " : "  ") + suggestions[row];
            _console.Write(suggestions[row].StartsWith('/')
                ? PromptBlock.SuggestionRow(suggestions[row], width, Accent, row == selected, unicode)
                : PromptBlock.Row(label, width, Accent, unicode, row == selected ? "bold " + Accent : "grey62", isInput: true, showPrompt: false));
            _console.WriteLine();
        }
        if (inputPadding > 0)
        {
            _console.Write(new string(' ', indent));
            _console.Write(PromptBlock.Border(width, unicode));
            _console.WriteLine();
        }
        _console.WriteLine();
        RenderStatus(width, indent);

        if (_console.Profile.Capabilities.Ansi)
        {
            var transcriptBottom = homeView ? Math.Max(1, TerminalHeight() - _composerRows) : Math.Max(1, top - 1);
            if (_viewportInitialized && !homeView)
            {
                _transcript.Draw(_console, top - 1);
            }
            _console.Write(new ControlCode($"\u001b[1;{transcriptBottom}r\u001b[{top + inputPadding + layout.CursorRow};{indent + PromptBlock.ContentOffset + 1 + layout.CursorColumn}H"));
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
        _console.Write(new ControlCode($"\u001b[?1049h\u001b[2 q\u001b[?1002h\u001b[?1006h\u001b[?2004h\u001b[2J\u001b[1;1H\u001b[1;{transcriptBottom}r"));
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
                _console.Write(new ControlCode("\u001b[?2026l\u001b[r\u001b[0m\u001b[0 q\u001b[?25h\u001b[?2004l\u001b[?1002l\u001b[?1006l\u001b[?1049l"));
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
            input.SkillNames = [];
            input.Placeholder = null;
            input.InitialText = string.Empty;
        }
    }

    private int TerminalHeight() => Math.Max(8, _console.Profile.Height);

    private int ComposerWidth()
    {
        return Math.Max(3, _console.Profile.Width - 2);
    }

    private int ChatWidth()
    {
        return Math.Max(3, _console.Profile.Width - 2);
    }

    private void RenderStatus(int width, int indent)
    {
        var model = _state.Model;
        var percentage = (Math.Clamp((double)_state.ContextTokens / ContextWindow.For(model), 0, 1) * 100)
            .ToString("0", CultureInfo.InvariantCulture) + "%";
        var costLabel = UsesAccountPlan ? "ChatGPT plan" : "$" + _state.Cost.ToString("0.00", CultureInfo.InvariantCulture);
        var separator = _console.Profile.Capabilities.Unicode ? " · " : " - ";
        var shortLabels = width < 60;
        var usage = shortLabels ? percentage + " " + (UsesAccountPlan ? "plan" : costLabel) :
            $"{ActivityStatus.FormatTokens(_state.TotalTokens).ToUpperInvariant()} ({percentage}){separator}{costLabel}";
        var approval = _approvalState.AutoApprove ? "auto-approval" : "manual-approval";
        var shortApproval = _approvalState.AutoApprove ? "auto" : "manual";
        var mode = _modeState.Mode == AgentMode.Plan ? "plan" : "agent";
        var controlParts = new (string Value, string Style)[]
        {
            (approval, "white on grey7"),
            (" (shift-tab)", "grey62 on grey7"),
            (separator, "grey35 on grey7"),
            (mode, "white on grey7"),
            (" (tab)", "grey62 on grey7"),
        };
        var controls = string.Concat(controlParts.Select(part => part.Value));
        if (controls.GetCellWidth() + usage.GetCellWidth() + 7 > width)
        {
            controlParts = [(shortApproval, "white on grey7"), (" (S-T) ", "grey62 on grey7"),
                (mode, "white on grey7"), (" (T)", "grey62 on grey7")];
            controls = string.Concat(controlParts.Select(part => part.Value));
        }
        if (controls.GetCellWidth() + usage.GetCellWidth() + 7 > width)
        {
            controlParts = [((_approvalState.AutoApprove ? "auto" : width < 32 ? "man" : "manual"), "white on grey7"),
                (" ", "grey62 on grey7"), (mode, "white on grey7")];
            controls = string.Concat(controlParts.Select(part => part.Value));
        }
        if (controls.GetCellWidth() + usage.GetCellWidth() + 6 > width)
        {
            usage = UsesAccountPlan ? "plan" : costLabel;
        }
        if (controls.GetCellWidth() + usage.GetCellWidth() + 4 > width)
        {
            usage = string.Empty;
        }
        controls = ComposerLayout.Clip(controls, Math.Max(0, width - usage.GetCellWidth() - 4));
        var modelGap = usage.Length > 0 ? shortLabels ? " " : "  " : string.Empty;
        var modelWidth = Math.Max(1, Math.Min(Math.Min(40, width / 3), width - controls.GetCellWidth() - usage.GetCellWidth() - modelGap.Length - 1));
        var parts = new (string Value, string Style)[]
        {
            (usage, "grey62 on grey7"),
            (modelGap, "grey62 on grey7"),
            (ComposerLayout.Clip(model, modelWidth), "grey62 on grey7"),
        };
        var rightWidth = parts.Sum(part => part.Value.GetCellWidth());
        var background = Style.Parse("on grey7");
        _console.Write(new Text(new string(' ', indent), background));
        var remainingControls = controls.GetCellWidth();
        foreach (var part in controlParts)
        {
            var value = ComposerLayout.Clip(part.Value, remainingControls);
            _console.Write(new Text(value, Style.Parse(part.Style)));
            remainingControls -= value.GetCellWidth();
        }
        var used = controls.GetCellWidth();
        var gap = Math.Max(0, width - used - rightWidth);
        _console.Write(new Text(new string(' ', gap), background));
        used += gap;
        foreach (var part in parts)
        {
            var value = ComposerLayout.Clip(part.Value, Math.Max(0, width - used));
            _console.Write(UsesAccountPlan && part.Value == usage
                ? new Markup("[grey62 on grey7 link=https://chatgpt.com/settings/usage]" + Markup.Escape(value) + "[/]")
                : new Text(value, Style.Parse(part.Style)));
            used += value.GetCellWidth();
        }
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
                mouse.Column == _console.Profile.Width && mouse.Row > 0 && mouse.Row <= rows)
            {
                _draggingScrollbar = true;
                _transcript.ScrollToRow(mouse.Row - 1, rows);
            }
            else if (mouse.Action == TerminalMouseAction.Drag && _draggingScrollbar)
            {
                _transcript.ScrollToRow(mouse.Row - 1, rows);
            }
            else if (mouse.Action == TerminalMouseAction.Press && mouse.Column > 0 &&
                mouse.Column < _console.Profile.Width && mouse.Row > 0 && mouse.Row <= rows &&
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
                _palette = new SettingsPalette();
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
            if (command == ":docs")
            {
                try
                {
                    _openBrowser(new Uri(DocumentationUrl));
                }
                catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    WriteError($"Could not open documentation: {exception.Message}");
                }
                WriteInfo(DocumentationUrl);
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
                        Present("Request queue is full. Try the setting after this response.", Accent);
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
