using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Models;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codai.Cli.Ui;

/// <summary>
/// <see cref="IUserInterface"/> implementation on top of Spectre.Console.
/// </summary>
/// <remarks>
/// Markdown is rendered incrementally: chunks are buffered until a full line is
/// available, code fences toggle syntax colouring and, inside a code block, lines
/// starting with <c>+</c>/<c>-</c> are coloured green/red — matching the original
/// Go terminal renderer.
/// </remarks>
public sealed class SpectreUserInterface : IUserInterface
{
    private readonly IAnsiConsole _console;
    private readonly StringBuilder _pendingLine = new();
    private bool _inCodeBlock;

    public SpectreUserInterface(IAnsiConsole? console = null)
    {
        _console = console ?? AnsiConsole.Console;
    }

    /// <inheritdoc />
    public Task<string?> ReadInputAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FlushPending();

        _console.Markup("[deepskyblue1]> [/]");
        var line = Console.ReadLine();

        return Task.FromResult(line);
    }

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FlushPending();

        if (!_console.Profile.Capabilities.Interactive)
        {
            // Non-interactive hosts (pipes, CI) cannot answer prompts: reject by default.
            WriteInfo(question);
            return Task.FromResult(false);
        }

        return Task.FromResult(_console.Confirm(Markup.Escape(question), defaultValue: false));
    }

    /// <inheritdoc />
    public void WriteLine(string message)
    {
        FlushPending();
        _console.WriteLine(message);
    }

    /// <inheritdoc />
    public void WriteError(string message)
    {
        FlushPending();
        _console.MarkupLineInterpolated($"[red]{message}[/]");
    }

    /// <inheritdoc />
    public void WriteSuccess(string message)
    {
        FlushPending();
        _console.MarkupLineInterpolated($"[green]{message}[/]");
    }

    /// <inheritdoc />
    public void WriteInfo(string message)
    {
        FlushPending();
        _console.MarkupLineInterpolated($"[grey]{message}[/]");
    }

    /// <inheritdoc />
    public void WriteMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return;
        }

        _pendingLine.Append(markdown);

        while (true)
        {
            var buffered = _pendingLine.ToString();
            var newlineIndex = buffered.IndexOf('\n', StringComparison.Ordinal);

            if (newlineIndex < 0)
            {
                return;
            }

            var line = buffered[..newlineIndex].TrimEnd('\r');
            _pendingLine.Remove(0, newlineIndex + 1);

            RenderLine(line);
        }
    }

    /// <inheritdoc />
    public async Task<T> RunWithProgressAsync<T>(
        string label,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        FlushPending();

        if (!_console.Profile.Capabilities.Interactive)
        {
            return await work(cancellationToken).ConfigureAwait(false);
        }

        return await _console
            .Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("deepskyblue1"))
            .StartAsync(label, _ => work(cancellationToken))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void ShowTokens(string provider, string model, TokenUsage session, TokenUsage last, decimal cost)
    {
        FlushPending();

        var text = new StringBuilder()
            .Append("Token Used: ").Append(session.TotalTokens)
            .Append(" - Cost: ").Append(cost.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)).Append(" $")
            .Append(" - Chat Model: ").Append(model)
            .ToString();

        var panel = new Panel(Markup.Escape(text))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.DeepSkyBlue1),
            Padding = new Padding(1, 0, 1, 0),
        };

        _console.Write(panel);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _pendingLine.Clear();
        _inCodeBlock = false;

        if (!_console.Profile.Capabilities.Ansi)
        {
            return;
        }

        _console.Write(new ControlCode("\u001b[2J\u001b[H"));
    }

    private void RenderLine(string line)
    {
        if (line.StartsWith("```", StringComparison.Ordinal))
        {
            _inCodeBlock = !_inCodeBlock;
        }

        if (_inCodeBlock && line.StartsWith('+'))
        {
            _console.MarkupLineInterpolated($"[green]{line}[/]");
        }
        else if (_inCodeBlock && line.StartsWith('-'))
        {
            _console.MarkupLineInterpolated($"[red]{line}[/]");
        }
        else if (_inCodeBlock)
        {
            _console.MarkupLineInterpolated($"[aqua]{line}[/]");
        }
        else
        {
            _console.MarkupLineInterpolated($"[default]{line}[/]");
        }
    }

    private void FlushPending()
    {
        if (_pendingLine.Length == 0)
        {
            return;
        }

        var remaining = _pendingLine.ToString();
        _pendingLine.Clear();
        RenderLine(remaining);
    }
}
