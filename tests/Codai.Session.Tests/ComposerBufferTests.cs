using Codai.Cli;
using Codai.Cli.Ui;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Tokens;
using FluentAssertions;
using Spectre.Console;

namespace Codai.Session.Tests;

public class ComposerBufferTests
{
    [Theory]
    [InlineData(" Hi ")]
    [InlineData(" [topic] ")]
    [InlineData(" e\u0301 topic ")]
    public void Tab_hover_changes_text_style_without_reusing_selected_background(string title)
    {
        var segment = new SessionTabBar.Segment(title, "/session tab:first");
        List<string> frames = [];
        foreach (var (active, hovered) in new[] { (true, false), (true, true), (false, true) })
        {
            using var writer = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes,
            });
            console.Write(SessionTabBar.Render(segment with { Active = active }, hovered));
            PlainOutput(writer).Should().Be(title);
            frames.Add(writer.ToString());
        }
        frames[0].Should().Be(frames[1]);
        frames.Distinct().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(44, 22)]
    [InlineData(110, 40)]
    public async Task Tab_hover_highlights_only_under_pointer_and_never_switches_session(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = width;
        console.Profile.Height = height;
        using var ui = new SpectreUserInterface(console, new SubmittedInput("question"));
        SessionTab[] tabs = [new("first", "Hi"), new("second", "New session")];
        ui.SetSessionTabs(tabs, "second", []);
        await ui.ReadInputAsync();
        writer.GetStringBuilder().Clear();

        ui.HandleMouse(new(4, 1, TerminalMouseAction.Move)).Should().BeFalse();
        var hovered = writer.ToString();
        hovered.Should().StartWith("\u001b[?2026h\u001b[?25l")
            .And.EndWith("\u001b[?25h\u001b[?2026l");
        hovered.Should().Contain("\u001b[1;1H").And.NotContain("\u001b[2K").And.NotContain("\u001b[2;1H");
        PlainOutput(writer).Should().Contain("Hi").And.Contain("question");
        writer.GetStringBuilder().Clear();
        ui.HandleMouse(new(5, 1, TerminalMouseAction.Move));
        writer.ToString().Should().BeEmpty();

        ui.HandleMouse(new(5, 2, TerminalMouseAction.Move));
        var restored = writer.ToString();
        restored.Should().StartWith("\u001b[?2026h\u001b[?25l")
            .And.EndWith("\u001b[?25h\u001b[?2026l");
        restored.Should().NotBe(hovered);
        writer.GetStringBuilder().Clear();
        ui.HandleMouse(new(5, 3, TerminalMouseAction.Move));
        writer.ToString().Should().BeEmpty();
        ui.HandleMouse(new(4, 1, TerminalMouseAction.Press)).Should().BeTrue();
        writer.ToString().Should().BeEmpty();
        ui.SetSessionTabs(tabs, "first", []);
        var switched = writer.ToString();
        switched.Should().StartWith("\u001b[?2026h\u001b[?25l")
            .And.Contain("\u001b[2;1H\u001b[J")
            .And.NotContain("\u001b[2J")
            .And.NotContain("\u001b[1;1H\u001b[2K")
            .And.EndWith("\u001b[?2026l");
    }

    [Fact]
    public async Task Suspended_tab_draft_restores_cursor_before_typing()
    {
        var keys = new Queue<ConsoleKeyInfo>([new('X', ConsoleKey.X, false, false, false), new('\r', ConsoleKey.Enter, false, false, false)]);
        var input = new SystemConsoleInput(() => null, () => keys.TryDequeue(out var key) ? key : null)
        {
            SuspendedDraft = ("draft", 2),
            ComposerRedrawHandler = (_, _, _, _) => { },
        };

        (await input.ReadInteractiveLineAsync(true, CancellationToken.None)).Should().Be("drXaft");
        input.SuspendedDraft.Should().BeNull();
    }

    [Fact]
    public void Session_tab_views_restore_tools_and_suspended_draft_without_leaking_other_tab()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 100;
        console.Profile.Height = 30;
        var input = new SystemConsoleInput(() => null);
        using var ui = new SpectreUserInterface(console, input);
        SessionTab[] tabs = [new("first", "First topic"), new("second", "Second topic")];
        ui.SetSessionTabs(tabs, "first", []).Should().BeTrue();
        ui.WriteToolStarted("read_file", "first-file.cs");
        ui.WriteToolCompleted("read_file", true, "first result");
        input.SuspendedDraft = ("first draft", 3);

        ui.SetSessionTabs(tabs, "second", []).Should().BeTrue();
        input.SuspendedDraft.Should().BeNull();
        ui.WriteToolStarted("read_file", "second-file.cs");
        ui.WriteToolCompleted("read_file", true, "second result");
        input.SuspendedDraft = ("second draft", 5);
        ui.SetSessionTabs(tabs, "first", []).Should().BeTrue();

        input.SuspendedDraft.Should().Be(("first draft", 3));
        var field = typeof(SpectreUserInterface).GetField("_transcript", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var transcript = (ChatTranscript)field.GetValue(ui)!;
        var restored = string.Join('\n', transcript.GetLines(90));
        restored.Should().Contain("first-file.cs").And.NotContain("second-file.cs");
        input.KeyHandler!(new ConsoleKeyInfo('\0', ConsoleKey.PageDown, false, false, true)).Should().BeTrue();
        input.PaletteCommandHandler!().Should().Be("/session next");
        input.KeyHandler!(new ConsoleKeyInfo('\0', ConsoleKey.T, false, false, true)).Should().BeTrue();
        input.PaletteCommandHandler!().Should().Be("/session open");
    }

    [Theory]
    [InlineData(24, 12)]
    [InlineData(80, 30)]
    public async Task Session_tab_strip_stays_on_first_row_and_mouse_selects_new_tab(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = width;
        console.Profile.Height = height;
        using var ui = new SpectreUserInterface(console, new SubmittedInput("question"));
        SessionTab[] tabs = [new("first", "First topic"), new("second", "Second topic")];
        ui.SetSessionTabs(tabs, "first", []);
        await ui.ReadInputAsync();

        var layout = SessionTabBar.Layout(width, tabs, "first");
        var plusColumn = layout.Take(layout.Count - 1).Sum(segment => segment.Text.GetCellWidth()) + 2;
        ui.HandleMouse(new(plusColumn, 1, TerminalMouseAction.Press)).Should().BeTrue();
        PlainOutput(writer).Should().Contain("First");
        writer.GetStringBuilder().Clear();
        ui.WriteMarkdown("answer");
        writer.ToString().Should().Contain("\u001b[2;1H").And.Contain("\u001b[1;1H");
    }

    [Theory]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(80)]
    [InlineData(160)]
    public void Session_tabs_keep_active_title_and_actions_within_terminal_width(int width)
    {
        SessionTab[] tabs = Enumerable.Range(0, 10)
            .Select(index => new SessionTab("tab-" + index, "Topic " + index + " with a long title\nsecond line")).ToArray();

        var segments = SessionTabBar.Layout(width, tabs, "tab-8");

        segments.Sum(segment => segment.Text.GetCellWidth()).Should().BeLessThanOrEqualTo(width);
        segments.Should().Contain(segment => segment.Command == "/session tab:tab-8" && segment.Active);
        segments.Should().Contain(segment => segment.Command == "/session close");
        segments.Should().Contain(segment => segment.Command == "/session open");
        string.Concat(segments.Select(segment => segment.Text)).Should().NotContain("\n");
        var column = 1;
        foreach (var segment in segments)
        {
            SessionTabBar.HitTest(column, segments).Should().Be(segment.Command);
            column += segment.Text.GetCellWidth();
        }
        SessionTabBar.HitTest(column, segments).Should().BeNull();
    }

    [Fact]
    public async Task Interactive_reader_keeps_pasted_newlines_and_waits_for_real_enter()
    {
        var keys = new Queue<ConsoleKeyInfo>("\u001b[200~first\r\nsecond\n\u001b[<2;4;5M\u0003last\u001b[201~"
            .Select(character => new ConsoleKeyInfo(character, character is '\r' or '\n' ? ConsoleKey.Enter : ConsoleKey.NoName, false, false, false)));
        keys.Enqueue(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        var clipboardReads = 0;
        var input = new SystemConsoleInput(() => { clipboardReads++; return "wrong clipboard"; },
            () => keys.TryDequeue(out var key) ? key : null);
        var draws = new List<string>();
        input.ComposerRedrawHandler = (text, _, _, _) => draws.Add(text);

        var result = await input.ReadInteractiveLineAsync(true, CancellationToken.None);

        result.Should().Be("first\nsecond\n[<2;4;5Mlast");
        draws.Should().HaveCount(2);
        clipboardReads.Should().Be(0);
        keys.Should().BeEmpty();
    }

    [Fact]
    public async Task Interactive_ctrl_v_inserts_clipboard_once_then_enter_submits()
    {
        var keys = new Queue<ConsoleKeyInfo>([TerminalPasteParser.PasteKey, new('\r', ConsoleKey.Enter, false, false, false)]);
        var input = new SystemConsoleInput(() => "first\r\nsecond", () => keys.TryDequeue(out var key) ? key : null);
        input.ComposerRedrawHandler = (_, _, _, _) => { };

        (await input.ReadInteractiveLineAsync(true, CancellationToken.None)).Should().Be("first\nsecond");
    }

    [Fact]
    public void Bracketed_paste_is_one_literal_insertion_not_enter_or_mouse_actions()
    {
        var parser = new TerminalPasteParser();
        const string text = "first\r\nsecond\n\u001b[<2;4;5M\u0003last";
        foreach (var character in "\u001b[200~" + text)
        {
            parser.Push(new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false));
        }
        parser.TryRead(out _, out _).Should().BeFalse();
        foreach (var character in "\u001b[201~")
        {
            parser.Push(new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false));
        }
        parser.TryRead(out var key, out var paste).Should().BeTrue();
        key.Should().Be(TerminalPasteParser.PasteKey);
        paste.Should().Be(text);
        parser.TryRead(out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Paste_preserves_surrounding_key_order_and_reset_discards_incomplete_paste()
    {
        var parser = new TerminalPasteParser();
        foreach (var character in "x\u001b[200~hello\u001b[201~y")
        {
            parser.Push(new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false));
        }
        parser.TryRead(out var first, out _).Should().BeTrue();
        first.KeyChar.Should().Be('x');
        parser.TryRead(out _, out var paste).Should().BeTrue();
        paste.Should().Be("hello");
        parser.TryRead(out var last, out _).Should().BeTrue();
        last.KeyChar.Should().Be('y');
        foreach (var character in "\u001b[200~unfinished\n")
        {
            parser.Push(new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false));
        }
        parser.Reset();
        parser.TryRead(out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Clipboard_paste_normalizes_newlines_inserts_at_cursor_and_never_submits()
    {
        var reads = 0;
        var input = new SystemConsoleInput(() => { reads++; return "one\r\ntwo\rthree\tfour\u0003"; });
        var editor = new ComposerBuffer([]);
        editor.Insert("before after");
        editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, true));
        for (var index = 0; index < 7; index++)
        {
            editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        }

        input.TryPaste(TerminalPasteParser.PasteKey, editor).Should().BeTrue();

        editor.Text.Should().Be("before one\ntwo\nthree    fourafter");
        reads.Should().Be(1);
        input.TryPaste(new ConsoleKeyInfo('v', ConsoleKey.V, false, false, false), editor).Should().BeFalse();
        reads.Should().Be(1);
    }

    [Fact]
    public void Unavailable_clipboard_preserves_draft_and_large_paste_is_not_truncated()
    {
        var editor = new ComposerBuffer([]);
        editor.Insert("draft");
        new SystemConsoleInput(() => null).TryPaste(TerminalPasteParser.PasteKey, editor).Should().BeTrue();
        editor.Text.Should().Be("draft");
        var large = string.Concat(Enumerable.Repeat("long line\n", 20_000));
        new SystemConsoleInput(() => large).TryPaste(TerminalPasteParser.PasteKey, editor).Should().BeTrue();
        editor.Text.Should().Be("draft" + large);
    }

    [Theory]
    [InlineData(80, 30)]
    [InlineData(24, 16)]
    public void Large_drafts_expand_composer_beyond_three_rows_without_losing_start(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input);
        const string draft = "first line\nsecond line\nthird line\nfourth line\nfifth line";

        input.ComposerRedrawHandler!(draft, draft.Length, [], 0);

        PlainOutput(writer).Should().Contain("first line").And.Contain("fifth line");
    }

    [Fact]
    public void Large_draft_viewport_tracks_cursor_and_shrinks_without_discarding_text()
    {
        var editor = new ComposerBuffer([]);
        var draft = string.Join('\n', Enumerable.Range(1, 100).Select(number => $"line {number:D3}"));
        editor.InsertPaste(draft);

        var tail = ComposerLayout.Create(editor.Text, editor.Cursor, 21, 12);
        tail.Lines.Should().HaveCount(12).And.EndWith("line 100");
        tail.CursorRow.Should().Be(11);

        editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, true));
        var start = ComposerLayout.Create(editor.Text, editor.Cursor, 21, 12);
        start.Lines.Should().HaveCount(12).And.StartWith("line 001");
        start.CursorRow.Should().Be(0);
        editor.Text.Should().Be(draft);

        editor.HandleKey(new ConsoleKeyInfo('\u0015', ConsoleKey.U, false, false, true));
        ComposerLayout.Create(editor.Text, editor.Cursor, 21, 12).Lines.Should().Equal("");
    }

    [Fact]
    public async Task Model_activity_counts_hidden_summary_output_and_restores_parent_request()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 120;
        console.Profile.Height = 30;
        console.Profile.Capabilities.Unicode = true;
        using var ui = new SpectreUserInterface(console, new QueuedTurnInput());
        Codai.Core.Abstractions.IUserInterface observer = new AutoApprovingUserInterface(ui, new ApprovalState(true));
        await ui.ReadInputAsync();
        await ui.RunTurnAsync(_ =>
        {
            var parent = Guid.NewGuid();
            observer.ModelRequestStarted(parent);
            var visible = new string('z', 400);
            observer.ModelRequestUpdated(parent, Codai.Core.Chat.ChatStreamUpdate.Content(visible));
            ui.WriteMarkdown(visible);
            for (var frame = 0; frame < 40; frame++)
            {
                ui.WriteInfo("Working...");
            }
            writer.GetStringBuilder().Clear();
            ui.WriteInfo("Thinking...");
            PlainOutput(writer).Should().Contain("\u2193 ~100 tokens").And.NotContain("~200 tokens");
            ui.WriteToolStarted("read_file", "example.cs");
            ui.WriteToolCompleted("read_file", true, "read");
            writer.GetStringBuilder().Clear();
            ui.WriteInfo("Considering...");
            PlainOutput(writer).Should().Contain("\u2193 ~100 tokens");
            observer.ModelRequestUpdated(parent, Codai.Core.Chat.ChatStreamUpdate.Completed(new TokenUsage(100, 5_000)));
            var child = Guid.NewGuid();
            observer.ModelRequestStarted(child);
            observer.ModelRequestUpdated(child, Codai.Core.Chat.ChatStreamUpdate.Content("private summary text"));
            observer.ModelRequestUpdated(child, Codai.Core.Chat.ChatStreamUpdate.Completed(new TokenUsage(3_000, 2_100)));
            writer.GetStringBuilder().Clear();
            ui.WriteInfo("Ruminating...");
            PlainOutput(writer).Should().Contain("Ruminating...").And.Contain("\u2193 2.1k tokens")
                .And.Contain("\u2191 3.0k in").And.NotContain("~2.1k").And.NotContain("private summary text");

            observer.ModelRequestCompleted(child);
            writer.GetStringBuilder().Clear();
            ui.WriteInfo("Working...");
            PlainOutput(writer).Should().Contain("\u2193 5.0k tokens").And.NotContain("2.1k tokens");
            observer.ModelRequestCompleted(parent);
            return Task.FromResult(true);
        });
    }

    [Theory]
    [InlineData(24)]
    [InlineData(80)]
    public void Auto_compact_horizontal_picker_adjusts_applies_and_fits_terminal(int width)
    {
        var palette = new SettingsPalette("Auto-compact window",
            [new("Auto", "", "0"), new("800k tokens", "", "800000"), new("1m tokens", "", "1000000")],
            options: new Codai.Core.Abstractions.SessionMenuOptions
            {
                HorizontalSelection = true,
                InitialValue = "800000",
                Placeholder = "Select auto-compact window:",
                Description = "Current setting for deepseek-flash: 800k tokens; capped by model.\n\nAuto uses model context.",
            });
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No });
        console.Profile.Width = width;
        console.Profile.Height = 25;

        palette.Draw(console, "violet");

        PlainOutput(writer).Should().Contain("800k tokens").And.Contain("Enter").And.Contain("Esc");
        PlainOutput(writer).Split('\n').Should().OnlyContain(line => line.GetCellWidth() <= width);
        palette.ShowCursor.Should().BeFalse();
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be("800000");
        palette.Closed.Should().BeTrue();
    }

    [Fact]
    public void Auto_compact_horizontal_picker_escape_discards_adjustments()
    {
        var palette = new SettingsPalette("Auto-compact window", [new("Auto", "", "0"), new("1m tokens", "", "1000000")],
            options: new Codai.Core.Abstractions.SessionMenuOptions { HorizontalSelection = true });
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        palette.Closed.Should().BeTrue();
        palette.Command.Should().BeNull();
    }

    [Fact]
    public void Skills_keep_multiple_checkmarks_while_searching_and_commit_all_choices()
    {
        var palette = new SettingsPalette("Skills",
        [
            new("audit", "Review changes", "audit") { IsCurrent = true },
            new("testing", "Run tests", "testing"),
        ], options: new Codai.Core.Abstractions.SessionMenuOptions { MultiSelect = true });
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Closed.Should().BeFalse();
        foreach (var character in "audit")
        {
            palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.A, false, false, false));
        }
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        palette.Closed.Should().BeTrue();
        System.Text.Json.JsonSerializer.Deserialize<string[]>(palette.Command!)!.Should().Equal("audit", "testing");
    }

    [Fact]
    public void Skills_enter_checks_and_unchecks_without_closing_and_escape_saves()
    {
        var palette = new SettingsPalette("Skills", [new("audit", "Review changes", "audit") { IsCurrent = true }],
            options: new Codai.Core.Abstractions.SessionMenuOptions { MultiSelect = true });
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Closed.Should().BeFalse();
        palette.Command.Should().BeNull();
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        palette.Closed.Should().BeTrue();
        System.Text.Json.JsonSerializer.Deserialize<string[]>(palette.Command!)!.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Skills", true, true)]
    [InlineData("Skills", false, true)]
    [InlineData("Skills", true, false)]
    [InlineData("Skills", false, false)]
    [InlineData("MCPs", true, true)]
    [InlineData("MCPs", false, true)]
    [InlineData("MCPs", true, false)]
    [InlineData("MCPs", false, false)]
    public void Multi_select_only_enabled_rows_use_theme_color_and_plain_checkmarks(string title, bool enabledSelected, bool unicode)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 110;
        console.Profile.Height = 40;
        console.Profile.Capabilities.Unicode = unicode;
        var palette = new SettingsPalette(title,
        [
            new("audit", "Review changes", "audit") { IsCurrent = true },
            new("testing", "Run tests", "testing"),
        ], options: new Codai.Core.Abstractions.SessionMenuOptions { MultiSelect = true, InitialValue = enabledSelected ? "audit" : "testing" });

        palette.Draw(console, "violet");
        var output = writer.ToString().Replace("\u001b", "<ESC>", StringComparison.Ordinal);
        PlainOutput(writer).Should().Contain(unicode ? "✔ audit" : "x audit")
            .And.NotContain("☑").And.NotContain("☐").And.NotContain("[x]").And.NotContain("[ ]")
            .And.NotContain("Space to toggle").And.Contain("Enter to confirm").And.Contain("Esc to close");

        writer.GetStringBuilder().Clear();
        var enabledPrefix = (enabledSelected ? unicode ? "❯ " : "> " : "  ") + (unicode ? "✔ " : "x ") + "audit";
        console.Write(new Text(enabledPrefix, Style.Parse("bold purple" + (enabledSelected ? " on grey19" : string.Empty))));
        output.Should().Contain(writer.ToString().Replace("\u001b[0m", string.Empty, StringComparison.Ordinal).Replace("\u001b", "<ESC>", StringComparison.Ordinal));

        writer.GetStringBuilder().Clear();
        var disabledPrefix = (enabledSelected ? "  " : unicode ? "❯ " : "> ") + "  testing";
        console.Write(new Text(disabledPrefix, Style.Parse("bold default" + (enabledSelected ? string.Empty : " on grey19"))));
        output.Should().Contain(writer.ToString().Replace("\u001b[0m", string.Empty, StringComparison.Ordinal).Replace("\u001b", "<ESC>", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(24, 16, false)]
    [InlineData(44, 22, true)]
    [InlineData(110, 40, true)]
    public void Skills_show_enabled_count_descriptions_and_sources_with_existing_search(int width, int height, bool unicode)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;
        console.Profile.Capabilities.Unicode = unicode;
        var palette = new SettingsPalette("Skills", [new("audit", "Review changes", "audit") { IsCurrent = true, RightLabel = "Repository" }],
            options: new Codai.Core.Abstractions.SessionMenuOptions { MultiSelect = true, Placeholder = "Search skills..." });

        palette.Draw(console, "violet");

        PlainOutput(writer).Should().Contain("Skills").And.Contain("1/1").And.Contain("Search skills")
            .And.Contain("audit").And.Contain("Review changes").And.NotContain("1. audit");
        if (width >= 44)
        {
            PlainOutput(writer).Should().Contain("Repository").And.Contain("enabled");
        }
    }

    [Theory]
    [InlineData("/sessions")]
    [InlineData("/models")]
    [InlineData("/agents")]
    [InlineData("/auth")]
    [InlineData("/skills")]
    [InlineData("/diff")]
    [InlineData(":preferences plugins")]
    public async Task Local_controls_do_not_leave_user_message_blocks(string command)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 110;
        console.Profile.Height = 40;
        using var ui = new SpectreUserInterface(console, new SubmittedInput(command));

        (await ui.ReadInputAsync()).Should().Be(command);

        PlainOutput(writer).Should().NotContain(command);
    }

    [Theory]
    [InlineData("Normal request")]
    [InlineData("/init focus on tests")]
    [InlineData("/audit parser")]
    public async Task Prompt_requests_keep_user_message_blocks(string request)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 110;
        console.Profile.Height = 40;
        using var ui = new SpectreUserInterface(console, new SubmittedInput(request));

        (await ui.ReadInputAsync()).Should().Be(request);

        PlainOutput(writer).Should().Contain(request);
    }

    [Theory]
    [InlineData(44, 22)]
    [InlineData(110, 40)]
    public void Model_picker_keeps_selection_visible_without_group_headings(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;
        var palette = new SettingsPalette("Select model", Enumerable.Range(0, 30)
            .Select(index => new Codai.Core.Abstractions.SessionMenuItem($"Model-{index:D2}", "Provider", $"model:{index}")
            { Group = $"Group {index / 3}" }).ToArray(), options: new Codai.Core.Abstractions.SessionMenuOptions
            { Footer = "Connect provider ctrl+a   Favorite ctrl+f" });
        for (var index = 0; index < 30; index++)
        {
            writer.GetStringBuilder().Clear();
            palette.Draw(console);
            PlainOutput(writer).Should().Contain($"Model-{index:D2}").And.NotContain("Group ").And.NotContain("Connect provider");
            palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        }
    }

    [Theory]
    [InlineData(44, 22)]
    [InlineData(110, 40)]
    public void Model_picker_hides_groups_and_extra_footer_without_disabling_shortcuts(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;
        var palette = new SettingsPalette("Select model",
        [
            new("current", "ollama", "model:current") { Group = "Recent", IsCurrent = true },
            new("other", "ollama", "model:other") { Group = "ollama" },
        ], options: new Codai.Core.Abstractions.SessionMenuOptions
        {
            Footer = "Connect provider ctrl+a   Favorite ctrl+f",
            Shortcuts = [new(ConsoleKey.F, "favorite:", true)],
        });
        palette.Draw(console);
        PlainOutput(writer).Should().StartWith("Select model").And.Contain("1. current").And.Contain("2. other").And.Contain("Enter to confirm").And.Contain("Esc to cancel")
            .And.NotContain("Enter to change").And.NotContain("Esc to close")
            .And.NotContain("Recent").And.NotContain("ctrl+a").And.NotContain("ctrl+f").And.NotContain("Connect provider");
        palette.HandleKey(new ConsoleKeyInfo('\u0006', ConsoleKey.F, false, false, true));
        palette.Command.Should().Be("favorite:model:current");
    }

    [Theory]
    [InlineData(ConsoleKey.Tab, false, "@settings:usage")]
    [InlineData(ConsoleKey.Tab, true, "@settings:status")]
    [InlineData(ConsoleKey.LeftArrow, false, "@settings:status")]
    [InlineData(ConsoleKey.RightArrow, false, "@settings:usage")]
    public void Settings_local_tab_keys_dispatch_real_sections(ConsoleKey key, bool shift, string command)
    {
        var palette = new SettingsPalette("Codai / Edit configuration", [new("Model", "Saved model", "model")]);

        palette.HandleKey(new ConsoleKeyInfo('\0', key, shift, false, false));

        palette.Closed.Should().BeTrue();
        palette.Command.Should().Be(command);
    }

    [Theory]
    [InlineData(44, 22)]
    [InlineData(110, 40)]
    public void Configuration_search_uses_numbered_rows_aligned_values_and_inline_footer(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;
        console.Profile.Capabilities.Unicode = true;
        var palette = new SettingsPalette("Codai / Edit configuration",
        [
            new("Model", "Saved model", "model") { RightLabel = "deepseek-flash" },
            new("Stream responses", "Saved streaming policy", "stream") { RightLabel = "true" },
        ]);
        foreach (var character in "/model")
        {
            palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.M, false, false, false));
        }

        palette.Draw(console);

        var output = PlainOutput(writer);
        output.Should().StartWith("Edit configuration").And.Contain("❯ 1. Model").And.Contain("deepseek-flash")
            .And.Contain("Enter to confirm").And.Contain("Esc to cancel")
            .And.NotContain("Stream responses").And.NotContain("Type to filter");
        palette.Query.Should().Be("model");
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        palette.HandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false));
        palette.Command.Should().Be("model");
    }

    [Theory]
    [InlineData(null, 44)]
    [InlineData(null, 110)]
    [InlineData("Codai / Edit configuration", 110)]
    [InlineData("Codai / Settings / Status", 110)]
    [InlineData("Codai / Settings / Usage", 110)]
    [InlineData("Codai / Settings / Stats", 110)]
    public void Settings_header_has_no_section_tabs(string? title, int width)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = 40;
        var palette = title is null ? new SettingsPalette() : new SettingsPalette(title, [new("Model", "Saved model", "model")]);

        palette.Draw(console);

        PlainOutput(writer).Should().StartWith(title?.Replace("Codai / ", string.Empty, StringComparison.Ordinal) ?? "Settings")
            .And.Contain("Enter to confirm").And.Contain("Esc to cancel")
            .And.NotContain("Enter/Space").And.NotContain("to search").And.NotContain("to switch");
        if (title is null)
        {
            PlainOutput(writer).Should().NotContain("Status").And.NotContain("Config").And.NotContain("Usage").And.NotContain("Stats");
        }
    }

    [Theory]
    [InlineData("MCPs", false)]
    [InlineData("MCPs", true)]
    [InlineData("Skills", false)]
    [InlineData("Skills", true)]
    [InlineData("Integration policy", false)]
    [InlineData("Integration policy", true)]
    public void Picker_footer_uses_only_consistent_actions_without_extra_toggle_guidance(string title, bool unicode)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 44;
        console.Profile.Height = 22;
        console.Profile.Capabilities.Unicode = unicode;
        var palette = new SettingsPalette(title, [new("Server", "Enabled", "server")],
            options: new Codai.Core.Abstractions.SessionMenuOptions { Footer = "toggle space   restart to apply" });

        palette.Draw(console);

        PlainOutput(writer).Should().Contain("Enter to confirm").And.Contain("Esc to cancel").And.NotContain("toggle space").And.NotContain("restart to apply")
            .And.NotContain("Enter select").And.NotContain("esc cancel");
    }

    [Theory]
    [InlineData(24, "graphite")]
    [InlineData(44, "graphite")]
    [InlineData(110, "graphite")]
    [InlineData(24, "forest")]
    [InlineData(44, "forest")]
    [InlineData(110, "forest")]
    [InlineData(24, "sunset")]
    [InlineData(44, "sunset")]
    [InlineData(110, "sunset")]
    public void Settings_search_uses_neutral_flat_band_without_border_or_icon(int width, string theme)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = 30;
        console.Profile.Capabilities.Unicode = true;
        console.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
        var palette = new SettingsPalette();
        var search = "  " + ComposerLayout.Clip("Search settings...", width - 8);
        console.Write(new Text(search.PadRight(width - 4), Style.Parse("grey62 on grey7")));
        var expectedSearch = writer.ToString();
        writer.GetStringBuilder().Clear();

        palette.Draw(console, theme);

        PlainOutput(writer).Should().Contain("  Search").And.NotContain("⌕").And.NotContain("╭").And.NotContain("╯").And.NotContain("│");
        writer.ToString().Should().Contain(expectedSearch);
        writer.GetStringBuilder().Clear();
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        palette.Draw(console, theme);
        writer.ToString().Should().Contain(expectedSearch);
    }

    [Theory]
    [InlineData(24, 30)]
    [InlineData(44, 30)]
    [InlineData(110, 30)]
    [InlineData(24, 12)]
    [InlineData(44, 12)]
    [InlineData(110, 12)]
    [InlineData(24, 10)]
    [InlineData(44, 10)]
    [InlineData(110, 10)]
    public void Settings_search_is_one_row_with_aligned_placeholder_and_cursor(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;
        console.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
        console.Write(new Text(new string(' ', width - 4), Style.Parse("grey62 on grey7")));
        var expectedPadding = writer.ToString();
        writer.GetStringBuilder().Clear();
        var palette = new SettingsPalette();
        var searchRow = height >= 18 ? 4 : 2;

        palette.Draw(console);

        writer.ToString().Should().EndWith($"\u001b[{searchRow};5H");
        PlainOutput(writer).Should().Contain("Search");
        writer.GetStringBuilder().Clear();
        palette.HandleKey(new ConsoleKeyInfo('m', ConsoleKey.M, false, false, false));

        palette.Draw(console);

        writer.ToString().Should().EndWith($"\u001b[{searchRow};6H");
        writer.ToString().Should().NotContain(expectedPadding);
    }

    [Fact]
    public void Search_bands_match_settings_across_commands_submenus_and_picker_modes()
    {
        foreach (var (width, height) in new[] { (24, 10), (24, 12), (44, 22), (110, 40) })
        {
            foreach (var title in new[] { "Select model", "Themes", "Skills", "Sessions", "Codai / Edit configuration", "Codai / Settings / Usage" })
            {
                using var writer = new StringWriter();
                var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
                console.Profile.Width = width;
                console.Profile.Height = height;
                console.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
                console.Write(new Text(new string(' ', width - 4), Style.Parse("grey62 on grey7")));
                var padding = writer.ToString();
                writer.GetStringBuilder().Clear();
                console.Write(new Text("  m".PadRight(width - 4), Style.Parse("grey62 on grey7")));
                var field = writer.ToString();
                writer.GetStringBuilder().Clear();
                var palette = new SettingsPalette(title, [new("Model", "Current model", "model")],
                    options: new Codai.Core.Abstractions.SessionMenuOptions { MultiSelect = title == "Skills" });
                palette.HandleKey(new ConsoleKeyInfo('m', ConsoleKey.M, false, false, false));

                palette.Draw(console);

                var rendered = writer.ToString();
                rendered.Should().Contain(field, $"{title} must use the settings search width, padding and colors at {width}x{height}");
                rendered.Should().NotContain(padding,
                    $"{title} must keep the search field one row high at {width}x{height}");
            }
        }
    }

    [Fact]
    public void Mcp_picker_toggles_with_enter_and_saves_on_escape_without_space_toggle()
    {
        var options = new Codai.Core.Abstractions.SessionMenuOptions { MultiSelect = true };
        var picker = new SettingsPalette("MCPs", [new("server", "Enabled", "server")], options: options);
        picker.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        picker.Closed.Should().BeFalse();
        picker.Command.Should().BeNull();
        picker.HandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false));
        picker.Query.Should().Be(" ");
        picker.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        picker.Command.Should().Be("[\"server\"]");
        var empty = new SettingsPalette("MCPs", [], options: options);
        empty.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        empty.Closed.Should().BeFalse();
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 110;
        console.Profile.Height = 40;
        empty.Draw(console);
        PlainOutput(writer).Should().Contain("No results found").And.Contain("Esc to close").And.NotContain("toggle space");
        empty.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        empty.Command.Should().Be("[]");
    }

    [Theory]
    [InlineData("graphite")]
    [InlineData("forest")]
    [InlineData("sunset")]
    public void Model_picker_heading_uses_active_theme_accent(string theme)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 110;
        console.Profile.Height = 40;
        console.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
        console.Write(new Text("Select model", Style.Parse("bold " + Codai.Core.TerminalThemes.Find(theme).Accent)));
        var expectedHeading = writer.ToString();
        writer.GetStringBuilder().Clear();
        var palette = new SettingsPalette("Select model", [new("model", "Provider model", "model")]);

        palette.Draw(console, theme);

        PlainOutput(writer).Should().StartWith("Select model");
        writer.ToString().Should().Contain(expectedHeading).And.NotContain("38;2;180;190;254");
    }

    [Theory]
    [InlineData("Select model", "graphite", 44)]
    [InlineData("Select agent", "graphite", 110)]
    [InlineData("Codai / Theme", "forest", 44)]
    [InlineData("Skills", "sunset", 110)]
    [InlineData("MCPs", "graphite", 44)]
    [InlineData("Codai / Edit configuration", "forest", 110)]
    public void Submenus_share_numbered_inline_rows_and_color_only_selected_choice(string title, string theme, int width)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = 22;
        console.Profile.Capabilities.Unicode = true;
        console.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
        console.Write(new Text("❯ 1. Current ✔", Style.Parse(Codai.Core.TerminalThemes.Find(theme).Accent)));
        var expectedCurrent = writer.ToString();
        writer.GetStringBuilder().Clear();
        console.Write(new Text("❯ 2. Other", Style.Parse(Codai.Core.TerminalThemes.Find(theme).Accent)));
        var expectedOther = writer.ToString();
        writer.GetStringBuilder().Clear();
        var palette = new SettingsPalette(title,
        [
            new("Current", "Current description", "current") { IsCurrent = true, Group = "Hidden group" },
            new("Other", "Other description", "other"),
        ]);

        palette.Draw(console, theme);

        PlainOutput(writer).Should().StartWith(title.Replace("Codai / ", string.Empty, StringComparison.Ordinal))
            .And.Contain("1. Current ✔").And.Contain("2. Other").And.Contain("Current description")
            .And.NotContain("╭").And.NotContain("╮").And.NotContain("╰").And.NotContain("╯").And.Contain("  Search...")
            .And.Contain("Enter to confirm").And.Contain("Esc to cancel").And.NotContain("Hidden group");
        writer.ToString().Should().Contain(expectedCurrent).And.NotContain("\u001b[32m").And.NotContain("48;5;235");
        writer.ToString().Should().Contain("  2. Other");
        writer.GetStringBuilder().Clear();
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));

        palette.Draw(console, theme);

        writer.ToString().Should().Contain("  1. Current ✔").And.Contain(expectedOther).And.NotContain(expectedCurrent)
            .And.NotContain("\u001b[32m").And.NotContain("48;5;235");
    }

    [Fact]
    public void Submenu_rows_preserve_distinct_values_and_ticks_when_commands_match()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 110;
        console.Profile.Height = 22;
        console.Profile.Capabilities.Unicode = true;
        var palette = new SettingsPalette("Settings / Status",
        [
            new("Provider", "", "readonly") { RightLabel = "ollama" },
            new("Model", "", "readonly") { RightLabel = "qwen", IsCurrent = true },
        ]);

        palette.Draw(console);

        PlainOutput(writer).Should().Contain("1. Provider").And.Contain("2. Model ✔").And.Contain("ollama").And.Contain("qwen")
            .And.NotContain("Provider ✔");
    }

    [Theory]
    [InlineData(false, "graphite")]
    [InlineData(true, "graphite")]
    [InlineData(true, "forest")]
    [InlineData(true, "sunset")]
    public void Submenu_search_uses_flat_band_and_positions_typing_cursor(bool unicode, string theme)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 24;
        console.Profile.Height = 12;
        console.Profile.Capabilities.Unicode = unicode;
        console.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
        console.Write(new Text("  tool".PadRight(20), Style.Parse("grey62 on grey7")));
        var expectedSearch = writer.ToString();
        writer.GetStringBuilder().Clear();
        var palette = new SettingsPalette("MCPs", [new("Tool", "Enabled", "tool")]);
        foreach (var character in "tool")
        {
            palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.T, false, false, false));
        }

        palette.Draw(console, theme);

        PlainOutput(writer).Should().Contain("  tool").And.NotContain("⌕").And.NotContain("│").And.NotContain("| / ");
        writer.ToString().Should().Contain(expectedSearch).And.EndWith("\u001b[2;9H");
    }

    [Theory]
    [InlineData("Skills")]
    [InlineData("MCPs")]
    public void Submenu_multiline_labels_and_descriptions_stay_on_single_rows(string title)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 110;
        console.Profile.Height = 22;
        var palette = new SettingsPalette(title, [new("Choice\ncontinued", "First\nsecond", "choice")]);

        palette.Draw(console);

        PlainOutput(writer).Should().Contain("1. Choice continued").And.Contain("First second");
    }

    [Theory]
    [InlineData(24, 12)]
    [InlineData(44, 22)]
    public void Grouped_name_only_picker_keeps_every_selection_visible(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;
        var picker = new SettingsPalette("Grouped choices", Enumerable.Range(0, 30)
            .Select(index => new Codai.Core.Abstractions.SessionMenuItem($"Option-{index:D2}", string.Empty, index.ToString()) { Group = "Provider" }).ToArray());
        for (var index = 0; index < 30; index++)
        {
            writer.GetStringBuilder().Clear();
            picker.Draw(console);
            PlainOutput(writer).Should().Contain($"Option-{index:D2}");
            picker.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        }
    }

    private static string PlainOutput(StringWriter writer) =>
        System.Text.RegularExpressions.Regex.Replace(writer.ToString(), "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);

    [Theory]
    [InlineData(44)]
    [InlineData(110)]
    public void Prompt_surfaces_frame_text_between_matching_horizontal_rules(int width)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = 30;
        console.Profile.Capabilities.Unicode = true;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input);
        input.ComposerRedrawHandler!("Focus here", 5, [], 0);

        var output = PlainOutput(writer);
        var borders = output.Split('\n').Where(line => line.Contains('\u2500')).ToArray();
        borders.Should().HaveCount(2);
        borders[0].Trim().Should().Be(borders[1].Trim());
        output.Should().Contain("\u276f Focus here");
        output.Should().MatchRegex("\u2500[^\\r\\n]*\\r?\\n\\r?\\n[^\\r\\n]*manual[^\\r\\n]*config");
        output.Should().NotContain("Ctrl+P settings").And.NotContain("Ctrl+P commands");
        var transcript = new ChatTranscript();
        transcript.AddUser("Earlier request");
        var historyRows = transcript.GetLines(width);
        historyRows.Count.Should().Be(1);
        historyRows[0].Should().Contain("Earlier request");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(44)]
    [InlineData(110)]
    public void Prompt_input_uses_prompt_marker_and_keeps_usage_below_block(int width)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = 30;
        console.Profile.Capabilities.Unicode = true;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input);
        ui.ShowTokens("outside-model", new TokenUsage(10, 2), new TokenUsage(10, 2), 0);
        writer.GetStringBuilder().Clear();

        input.ComposerRedrawHandler!("Hello there", 5, [], 0);

        var output = PlainOutput(writer);
        output.Should().Contain("\u276f Hello there").And.Contain("──").And.NotContain("▎");
        var footerLabel = width >= 80 ? "(0%)" : width >= 40 ? "0%" : "$0.00";
        output.LastIndexOf('\u2500').Should().BeLessThan(output.IndexOf(footerLabel, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(24, 30, 3)]
    [InlineData(44, 22, 3)]
    [InlineData(110, 40, 3)]
    [InlineData(44, 12, 3)]
    [InlineData(44, 10, 1)]
    public void Input_uses_unfilled_rules_and_history_retains_its_dark_background(int width, int height, int rows)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;
        console.Profile.Capabilities.Unicode = true;
        console.Profile.Capabilities.ColorSystem = ColorSystem.EightBit;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input);

        input.ComposerRedrawHandler!("Focus here", 5, [], 0);

        PlainOutput(writer).Split('\n').Count(line => line.Contains('\u2500') || line.Contains('\u276f')).Should().Be(rows);
        var inputRows = string.Join('\n', writer.ToString().Split('\n').Where(line => line.Contains('\u276f')));
        inputRows.Should().NotContain("48;5;234").And.NotContain("48;5;233");
        var history = new ChatTranscript();
        history.AddUser("Earlier request");
        var lines = history.GetLines(width);
        lines.Count(line => line.Contains('\u276f')).Should().Be(1);
        string.Join('\n', lines).Should().Contain("48;5;233").And.NotContain("48;5;234");
    }

    [Theory]
    [InlineData(44)]
    [InlineData(110)]
    public async Task Empty_input_placeholder_appears_only_on_home_screen(int width)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = width;
        console.Profile.Height = 30;
        using var ui = new SpectreUserInterface(console, new ChatInput());

        await ui.ReadInputAsync();
        PlainOutput(writer).Should().Contain("What shall we work on?");
        writer.GetStringBuilder().Clear();

        ui.WriteMarkdown("Hello.");
        console.Profile.Width = width == 44 ? 110 : 44;
        await ui.ReadInputAsync();

        PlainOutput(writer).Should().Contain("Hello.").And.Contain("\u276f")
            .And.NotContain("What shall we work on?");
    }

    [Fact]
    public void Sent_questions_use_compact_prompt_strip_with_indented_continuations()
    {
        var transcript = new ChatTranscript();
        transcript.AddUser("Hi\nSecond line");

        var lines = transcript.GetLines(44);
        var output = System.Text.RegularExpressions.Regex.Replace(string.Join('\n', lines), "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);

        output.Should().Contain("\u276f Hi").And.Contain("  Second line").And.NotContain("▎");
        lines.Count.Should().Be(2);
    }

    [Fact]
    public void Transcript_uses_one_blank_separator_without_accumulating_paragraph_padding()
    {
        var transcript = new ChatTranscript();
        transcript.AddUser("Hi");
        transcript.AppendMarkdown("Hello.");
        transcript.AddUser("How are you?");
        transcript.AppendMarkdown("Fine.");

        var plain = System.Text.RegularExpressions.Regex.Replace(string.Join('\n', transcript.GetLines(80)), "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);

        plain.Should().MatchRegex(@"Hello\.[^\n]*\n\n\u276f How are you\?")
            .And.EndWith("Fine.").And.NotMatchRegex(@"\n[ \t]*\n[ \t]*\n[ \t]*\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Composer_follows_short_answers_and_pins_only_when_transcript_fills_viewport(bool longAnswer)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 110;
        console.Profile.Height = 40;
        using var ui = new SpectreUserInterface(console, new ChatInput());
        await ui.ReadInputAsync();
        writer.GetStringBuilder().Clear();
        ui.WriteMarkdown(longAnswer ? string.Join('\n', Enumerable.Repeat("Answer line  ", 60)) : "Short answer.");

        await ui.ReadInputAsync();

        writer.ToString().Should().Contain(longAnswer ? "\u001b[37;4H" : "\u001b[8;4H");
        if (longAnswer)
        {
            writer.ToString().Should().Contain("\u001b[r\u001b[36;1H");
        }
        if (!longAnswer)
        {
            writer.ToString().Should().NotContain("\u001b[37;4H");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Loading_dot_grid_frames_keep_a_fixed_two_cell_slot(bool unicode)
    {
        var spinner = new SpectreUserInterface.DotMatrixSpinner(unicode);

        spinner.IsUnicode.Should().Be(unicode);
        spinner.Interval.Should().Be(TimeSpan.FromMilliseconds(160));
        spinner.Frames.Should().OnlyContain(frame => frame.GetCellWidth() == 2);
        spinner.Frames.Distinct().Count().Should().BeGreaterThan(4);
        if (!unicode)
        {
            spinner.Frames.Should().OnlyContain(frame => frame.All(character => character <= 127));
        }
    }

    [Fact]
    public void Loading_dot_grid_fills_nine_points_then_empties_without_changing_shape()
    {
        var frames = new SpectreUserInterface.DotMatrixSpinner(true).Frames;

        frames.Should().OnlyContain(frame => frame[0] >= '\u2800' && frame[0] <= '\u283f'
            && frame[1] >= '\u2800' && frame[1] <= '\u2807');
        frames.Select(frame => frame.Sum(character => System.Numerics.BitOperations.PopCount((uint)(character - '\u2800'))))
            .Should().Equal(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4, 3, 2, 1);
        frames[9].Should().Be("\u283f\u2807");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Loading_dot_grid_varies_patterns_between_cycles_without_changing_repeated_frames(bool unicode)
    {
        var spinner = new SpectreUserInterface.DotMatrixSpinner(unicode, new Random(42));
        var cycles = new List<string[]>();
        for (var cycle = 0; cycle < 12; cycle++)
        {
            var frames = new List<string>();
            for (var index = 0; index < spinner.Frames.Count; index++)
            {
                var position = cycle * spinner.Frames.Count + index;
                var frame = spinner.GetFrame(position);
                spinner.GetFrame(position).Should().Be(frame);
                frame.GetCellWidth().Should().Be(2);
                frames.Add(frame);
            }
            cycles.Add(frames.ToArray());
        }

        cycles.Select(frames => string.Join('|', frames)).Distinct().Count().Should().BeGreaterThan(1);
        var midpoint = spinner.Frames.Count / 2;
        cycles.Select(frames => string.Join('|', frames.Take(midpoint))).Distinct().Count().Should().BeGreaterThan(1);
        cycles.Select(frames => string.Join('|', frames.Skip(midpoint + 1))).Distinct().Count().Should().BeGreaterThan(1);
        cycles.Should().Contain(frames => !frames.Skip(midpoint + 1).SequenceEqual(frames.Skip(1).Take(midpoint - 1).Reverse()));
        if (unicode)
        {
            foreach (var frames in cycles)
            {
                frames.Select(frame => frame.Sum(character => System.Numerics.BitOperations.PopCount((uint)(character - '\u2800'))))
                    .Should().Equal(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4, 3, 2, 1);
            }
        }
    }

    [Theory]
    [InlineData(true, "Thinking")]
    [InlineData(false, "Reasoning")]
    [InlineData(true, "Considering")]
    [InlineData(false, "Loading")]
    public async Task Work_uses_filling_dot_grid_with_ascii_fallback(bool unicode, string phase)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        console.Profile.Capabilities.Unicode = unicode;
        var input = new QueuedTurnInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        writer.GetStringBuilder().Clear();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ui.RunTurnAsync(token =>
        {
            ui.WriteInfo(phase + "...");
            return input.Finished.Task.WaitAsync(token);
        }, deadline.Token);
        PlainOutput(writer).Should().MatchRegex(unicode
            ? @"[\u2800-\u283f][\u2800-\u2807][^\r\n]*" + phase + @"\.\.\.[^\r\n]*\(\d+s · esc interrupt"
            : @"[ .:]{2}[^\r\n]*" + phase + @"\.\.\.[^\r\n]*\(\d+s - esc interrupt")
            .And.Contain("esc interrupt")
            .And.NotContain("·✢·").And.NotContain("✢✳✢");
    }

    [Theory]
    [InlineData(false, 22)]
    [InlineData(true, 22)]
    [InlineData(true, 12)]
    [InlineData(false, 10)]
    public async Task Busy_loader_has_vertical_breathing_room_with_and_without_color(bool color, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 44;
        console.Profile.Height = height;
        console.Profile.Capabilities.ColorSystem = color ? ColorSystem.TrueColor : ColorSystem.NoColors;
        var input = new QueuedTurnInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        writer.GetStringBuilder().Clear();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ui.RunTurnAsync(async token =>
        {
            ui.WriteInfo("Reasoning...");
            return await input.Finished.Task.WaitAsync(token);
        }, deadline.Token);
        var output = writer.ToString();
        var plain = PlainOutput(writer);
        plain.Should().MatchRegex(@"Reasoning[^\r\n]*\(\d+s · esc(?: interrupt)?\)")
            .And.NotMatchRegex(@"(?m)^ [\u2800-\u28ff]{2} esc interrupt");
        if (color)
        {
            output.Should().Contain("\u001b[38;");
        }
        (await ui.ReadInputAsync()).Should().Be("next");
    }

    [Fact]
    public async Task Busy_resize_clears_new_composer_area_including_blank_rows()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 110;
        console.Profile.Height = 40;
        var input = new QueuedTurnInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        writer.GetStringBuilder().Clear();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ui.RunTurnAsync(async token =>
        {
            console.Profile.Width = 44;
            console.Profile.Height = 22;
            ui.WriteInfo("Reasoning...");
            return await input.Finished.Task.WaitAsync(token);
        }, deadline.Token);

        writer.ToString().Should().Contain("\u001b[13;1H\u001b[2K").And.Contain("\u001b[22;1H\u001b[2K");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Streaming_answers_keep_inline_elapsed_time_and_interrupt_hint(bool unicode)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 120;
        console.Profile.Height = 30;
        console.Profile.Capabilities.Unicode = unicode;
        var input = new QueuedTurnInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        writer.GetStringBuilder().Clear();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await ui.RunTurnAsync(async token =>
        {
            ui.WriteMarkdown(new string('a', 8_000));
            await Task.Delay(TimeSpan.FromSeconds(2.4), token);
            return true;
        }, deadline.Token);

        PlainOutput(writer).Should()
            .MatchRegex(unicode ? @"Responding\.\.\.[^\r\n]*\([1-9]\d*s · ↓ ~[\d.]+k tokens · esc interrupt\)"
                : @"Responding\.\.\.[^\r\n]*\([1-9]\d*s - out ~[\d.]+k tokens - esc interrupt\)");
    }

    [Fact]
    public async Task Narrow_terminals_shorten_loader_before_dropping_interrupt_hint()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 22;
        console.Profile.Height = 22;
        var input = new QueuedTurnInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        writer.GetStringBuilder().Clear();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ui.RunTurnAsync(token => input.Finished.Task.WaitAsync(token), deadline.Token);

        PlainOutput(writer).Should().MatchRegex(@"\(\d+s · esc\)").And.NotContain("esc interrupt");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Loading_work_keeps_its_phase_and_accepts_drafts(bool nested)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        var input = new QueuedTurnInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        writer.GetStringBuilder().Clear();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<bool> Load(CancellationToken token) => Task.FromResult(input.Finished.Task.Wait(TimeSpan.FromSeconds(2), token));
        Task<bool> Progress(CancellationToken token) => ui.RunWithProgressAsync("Loading files...", Load, token);
        var result = nested ? await ui.RunTurnAsync(Progress, deadline.Token) : await Progress(deadline.Token);
        result.Should().BeTrue();
        PlainOutput(writer).Should().Contain("esc interrupt");
        (await ui.ReadInputAsync()).Should().Be("next");
    }

    [Fact]
    public async Task Turn_accepts_draft_keys_while_work_is_synchronous()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        var input = new QueuedTurnInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var accepted = await ui.RunTurnAsync(token => Task.FromResult(
            input.Finished.Task.Wait(TimeSpan.FromSeconds(2), token)), deadline.Token);
        accepted.Should().BeTrue();
        (await ui.ReadInputAsync()).Should().Be("next");
    }

    [Fact]
    public void Account_footer_identifies_plan_usage_without_showing_api_cost()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 100;
        console.Profile.Height = 30;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input) { UsesAccountPlan = true };
        ui.ShowTokens("codai-plan-model", new TokenUsage(12000, 300), new TokenUsage(1200, 30), 1.23m);
        input.ComposerRedrawHandler!(string.Empty, 0, [], 0);
        writer.ToString().Should().Contain("ChatGPT plan").And.NotContain("$1.23");
    }

    [Fact]
    public async Task Startup_progress_animates_operation_label_without_empty_task_description()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        using var ui = new SpectreUserInterface(console, new ChatInput());
        var result = await ui.RunWithProgressAsync("Loading workspace...", async token =>
        {
            await Task.Delay(420, token);
            return 42;
        });
        result.Should().Be(42);
        PlainOutput(writer).Should().MatchRegex(@"Loading workspace\.\.\. \(\d+s\)").And.NotContain("Working");
    }

    [Theory]
    [InlineData("Thinking...", false)]
    [InlineData("Reasoning...", false)]
    [InlineData("Loading...", false)]
    [InlineData("Working...", false)]
    [InlineData("Loading...", true)]
    public async Task Work_animates_phases_above_composer_without_blocking_drafts(string activity, bool progress)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        var input = new QueuedTurnInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        writer.GetStringBuilder().Clear();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        async Task<bool> Work(CancellationToken token)
        {
            ui.WriteInfo(activity);
            ui.WriteMarkdown("Answer");
            return await input.Finished.Task.WaitAsync(token);
        }
        if (progress)
        {
            await ui.RunWithProgressAsync(activity, Work, deadline.Token);
        }
        else
        {
            await ui.RunTurnAsync(Work, deadline.Token);
        }
        PlainOutput(writer).Should().Contain("next")
            .And.Contain("esc interrupt")
            .And.NotContain("generating")
            .And.NotContain("Esc: stop");
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(64_000, 0, 50)]
    [InlineData(32_000, 32_000, 50)]
    [InlineData(128_000, 0, 100)]
    [InlineData(256_000, 0, 100)]
    public void Footer_percentage_uses_latest_context_and_preserves_session_totals(int inputTokens, int outputTokens, int percentage)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 100;
        console.Profile.Height = 30;
        console.Profile.Capabilities.ColorSystem = ColorSystem.EightBit;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input);
        ui.ShowTokens("codai-model", new TokenUsage(300_000, 12_345), new TokenUsage(inputTokens, outputTokens), 1.23m);
        input.ComposerRedrawHandler!(string.Empty, 0, [], 0);
        var output = writer.ToString();
        output.Should().Contain($"312.3K ({percentage}%) · $1.23")
            .And.NotContain("\u001b[48;5;237m");
        PlainOutput(writer).Should().MatchRegex("\u2500[^\\r\\n]*\\r?\\n\\r?\\n[^\\r\\n]*manual-approval[^\\r\\n]*312\\.3K");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(44)]
    [InlineData(110)]
    public void Footer_preserves_percentage_and_cost_when_space_is_limited(int width)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = 22;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input);
        ui.ShowTokens("codai-model", new TokenUsage(300_000, 12_345), new TokenUsage(64_000, 0), 12.34m);
        writer.GetStringBuilder().Clear();

        input.ComposerRedrawHandler!(string.Empty, 0, [], 0);

        PlainOutput(writer).Should().Contain(width < 40 ? "$12.34" : width < 60 ? "50% $12.34" : "312.3K (50%) · $12.34")
            .And.Contain(width < 60 ? "codai" : "codai-model");
    }

    [Theory]
    [InlineData(0, "0", true)]
    [InlineData(999, "999", true)]
    [InlineData(1_000, "1.0K", true)]
    [InlineData(19_000, "19.0K", true)]
    [InlineData(19_000, "19.0K", false)]
    [InlineData(1_500_000, "1.5M", true)]
    public void Footer_uses_compact_token_totals_and_parenthesized_context(int tokens, string expected, bool unicode)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 110;
        console.Profile.Height = 30;
        console.Profile.Capabilities.Unicode = unicode;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input);
        ui.ShowTokens("codai-model", new TokenUsage(tokens, 0), new TokenUsage(2_560, 0), 0.01m);
        writer.GetStringBuilder().Clear();

        input.ComposerRedrawHandler!(string.Empty, 0, [], 0);

        var separator = unicode ? " · " : " - ";
        PlainOutput(writer).Should().Contain($"{expected} (2%){separator}$0.01");
    }

    [Fact]
    public async Task Welcome_is_static_for_injected_input_and_is_not_replayed_between_turns()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 100;
        console.Profile.Height = 30;
        using var ui = new SpectreUserInterface(console, new ChatInput());
        await ui.ReadInputAsync();
        await ui.ReadInputAsync();
        writer.ToString().Split("Commands /  Files @  Menu Ctrl+P", StringSplitOptions.None).Should().HaveCount(2);
        writer.ToString().Should().NotContain("Codai v");
    }

    [Theory]
    [InlineData(24, 24)]
    [InlineData(44, 24)]
    [InlineData(64, 24)]
    [InlineData(80, 24)]
    [InlineData(120, 24)]
    [InlineData(80, 12)]
    [InlineData(64, 16)]
    public async Task Welcome_wordmark_keeps_metadata_inside_terminal_width(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.No,
            Interactive = InteractionSupport.No,
        });
        console.Profile.Width = width;
        console.Profile.Height = height;
        using var ui = new SpectreUserInterface(console, new ChatInput(), provider: "header-provider", model: new string('m', 160), projectRoot: "codai-workspace");
        await ui.ReadInputAsync();
        var output = writer.ToString();
        output.Should().NotContain("Codai v").And.Contain("Ctrl+P")
            .And.NotContain("codai-workspace").And.NotContain("header-provider").And.NotContain(new string('m', 20));
        output.Split('\n').Should().OnlyContain(line => line.TrimEnd('\r').GetCellWidth() <= width);
        if (width >= 64 && height >= 16)
        {
            output.Should().Contain("/ __|").And.Contain("|___|");
            output.IndexOf("|___|", StringComparison.Ordinal).Should().BeLessThan(output.IndexOf("Ctrl+P", StringComparison.Ordinal));
        }
        else
        {
            output.Should().NotContain("/ __|");
        }
    }

    [Theory]
    [InlineData(AgentMode.Plan, true)]
    [InlineData(AgentMode.Agent, true)]
    [InlineData(AgentMode.Plan, false)]
    [InlineData(AgentMode.Agent, false)]
    public void Input_controls_use_quiet_footer_strip_with_muted_hints_and_model(AgentMode mode, bool autoApprove)
    {
        Codai.Configuration.CommandLineParser.Parse(["code"]).Mode.Should().Be(AgentMode.Agent);
        Codai.Configuration.CommandLineParser.Parse(["code", "--mode", "plan"]).Mode.Should().Be(AgentMode.Plan);
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 100;
        console.Profile.Height = 30;
        console.Profile.Capabilities.ColorSystem = ColorSystem.EightBit;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input, model: "footer-model", mode: mode, autoApprove: autoApprove);
        input.ComposerRedrawHandler!(string.Empty, 0, [], 0);
        var output = writer.ToString();
        output.Should().Contain("\u001b[38;5;247;48;5;233mfooter-model")
            .And.Contain("\u001b[48;5;233m")
            .And.Contain("\u001b[38;5;247;48;5;233m (shift-tab)");
        var approval = autoApprove ? "auto-approval" : "manual-approval";
        var selectedMode = mode == AgentMode.Plan ? "plan" : "agent";
        output.Should().Contain("\u001b[38;5;15;48;5;233m" + approval);
        var separator = console.Profile.Capabilities.Unicode ? " · " : " - ";
        PlainOutput(writer).Should().Contain($"{approval} (shift-tab){separator}{selectedMode} (tab)")
            .And.Contain("footer-model").And.NotContain("Ctrl+P settings");
    }

    [Fact]
    public void Input_controls_preserve_explicit_host_defaults_and_model_footer()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No });
        console.Profile.Width = 100;
        console.Profile.Height = 30;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input, model: "deepseek-v4-flash");

        input.ComposerRedrawHandler!(string.Empty, 0, [], 0);

        PlainOutput(writer).Should().Contain("deepseek-v4-flash")
            .And.Contain("manual-approval (shift-tab)").And.Contain("agent (tab)").And.NotContain("Ctrl+P settings");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(44)]
    [InlineData(110)]
    public void Input_control_labels_follow_shortcuts_without_overflow(int width)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = 24;
        var input = new SystemConsoleInput();
        var mode = new AgentModeState(AgentMode.Agent);
        var approval = new ApprovalState(autoApprove: true);
        using var ui = new SpectreUserInterface(console, input, modeState: mode, approvalState: approval);
        input.ComposerRedrawHandler!(string.Empty, 0, [], 0);
        PlainOutput(writer).Should().Contain("auto").And.Contain("agent");
        writer.GetStringBuilder().Clear();

        input.KeyHandler!(new ConsoleKeyInfo('\t', ConsoleKey.Tab, true, false, false)).Should().BeTrue();
        input.KeyHandler!(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false)).Should().BeTrue();
        input.ComposerRedrawHandler!(string.Empty, 0, [], 0);

        approval.AutoApprove.Should().BeFalse();
        mode.Mode.Should().Be(AgentMode.Plan);
        var separator = console.Profile.Capabilities.Unicode ? " · " : " - ";
        PlainOutput(writer).Should().Contain(width >= 110 ? $"manual-approval (shift-tab){separator}plan (tab)"
            : width >= 44 ? "manual (S-T) plan (T)" : "man plan");
        PlainOutput(writer).Split('\n').Should().OnlyContain(line => line.TrimEnd('\r').GetCellWidth() <= width);
    }

    [Theory]
    [InlineData(24, false)]
    [InlineData(24, true)]
    [InlineData(44, false)]
    [InlineData(44, true)]
    [InlineData(80, false)]
    [InlineData(80, true)]
    [InlineData(110, false)]
    [InlineData(110, true)]
    [InlineData(180, false)]
    [InlineData(180, true)]
    public void Footer_fits_workspace_usage_and_current_model_in_one_line(int width, bool autoApprove)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = 24;
        using var ui = new SpectreUserInterface(console, new SystemConsoleInput(), projectRoot: @"C:\workspaces\codai", autoApprove: autoApprove);
        ui.ShowTokens(new string('m', 180), new TokenUsage(100, 20), new TokenUsage(100, 20), 0.12m);
        writer.GetStringBuilder().Clear();

        typeof(SpectreUserInterface).GetMethod("RenderStatus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(ui, [width - 2, 1]);

        var footer = PlainOutput(writer);
        footer.Should().NotContain("\n").And.NotContain("\r").And.Contain("$0.12")
            .And.Contain("mmm").And.Contain(autoApprove ? "auto" : width < 32 ? "man" : "manual").And.Contain("agent")
            .And.NotContain("workspaces").And.NotContain("Ctrl+P");
        if (width >= 44)
        {
            footer.Should().Contain("0%");
        }
        footer.GetCellWidth().Should().BeLessThanOrEqualTo(width - 1);
        if (width >= 80)
        {
            footer.Should().StartWith(autoApprove ? " auto-approval" : " manual-approval").And.EndWith("mmm");
        }
    }

    [Theory]
    [InlineData(AgentMode.Agent, AgentMode.Plan)]
    [InlineData(AgentMode.Plan, AgentMode.Agent)]
    public void Tab_cycles_only_between_plan_and_agent(AgentMode initial, AgentMode next)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });
        var input = new SystemConsoleInput();
        var state = new AgentModeState(initial);
        using var ui = new SpectreUserInterface(console, input, modeState: state);
        var tab = new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false);

        input.KeyHandler!(tab).Should().BeTrue();
        state.Mode.Should().Be(next);
        input.KeyHandler!(tab).Should().BeTrue();
        state.Mode.Should().Be(initial);
    }

    [Fact]
    public void Command_palette_dispatches_agent_selection()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No });
        console.Profile.Width = 100;
        console.Profile.Height = 30;
        var palette = new SettingsPalette();
        foreach (var character in "agents")
        {
            palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.M, false, false, false));
        }

        palette.Draw(console);
        writer.ToString().Should().Contain("Agents").And.NotContain("Ask mode");
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be("/agents");
    }

    [Fact]
    public void Settings_is_callable_but_not_suggested_in_slash_completion()
    {
        var editor = new ComposerBuffer([]);
        editor.Insert("/settings");
        var completion = new ComposerCompletion();

        completion.Update(editor, []);

        completion.Options.Should().BeEmpty();
        Codai.Application.SlashCommandParser.Parse("/settings").Should().Be(Codai.Application.SlashCommand.Settings);
    }

    [Fact]
    public void Command_palette_and_completion_offer_new_session()
    {
        var editor = new ComposerBuffer([]);
        editor.Insert("/new");
        var completion = new ComposerCompletion();
        completion.Update(editor, []);
        completion.Options.Should().Equal("/new");
        completion.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), editor);
        editor.Text.Should().Be("/new");
        var palette = new SettingsPalette();
        foreach (var character in "new")
        {
            palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.N, false, false, false));
        }
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be("/new");
    }

    [Theory]
    [InlineData(24, false, "forest")]
    [InlineData(44, true, "sunset")]
    [InlineData(110, true, "graphite")]
    public void Command_suggestions_have_aligned_descriptions_and_theme_colored_selection_and_rail(int width, bool unicode, string theme)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, ColorSystem = ColorSystemSupport.EightBit });
        console.Profile.Width = width;
        var accent = Codai.Core.TerminalThemes.Find(theme).Accent;
        console.Write(new Text(" > /new", Style.Parse(accent + " on grey11")));
        var selectedPrefix = writer.ToString().Split(" > /new", StringSplitOptions.None)[0];
        writer.GetStringBuilder().Clear();
        console.Write(PromptBlock.SuggestionRow("/new", width, accent, true, unicode));
        var selected = PlainOutput(writer);
        selected.Should().Contain("> /new");
        selected.GetCellWidth().Should().Be(width);
        writer.ToString().Should().Contain("48;5;234");
        writer.ToString().Should().Be(selectedPrefix + selected + "\u001b[0m");
        var rail = unicode ? "\u258d" : "|";
        writer.GetStringBuilder().Clear();
        console.Write(new Text(rail, Style.Parse(accent + " on default")));
        var railPrefix = writer.ToString().Split(rail, StringSplitOptions.None)[0];
        writer.GetStringBuilder().Clear();
        console.Write(PromptBlock.SuggestionRow("/exit", width, accent, false, unicode));
        var normal = PlainOutput(writer);
        normal.GetCellWidth().Should().Be(width);
        normal.IndexOf("Exit", StringComparison.Ordinal).Should().Be(selected.IndexOf("New", StringComparison.Ordinal));
        writer.ToString().Should().NotContain("48;5;234");
        writer.ToString().Should().Contain(railPrefix + rail);
        normal.Should().NotContain("> /exit");
    }

    [Fact]
    public void Enabled_skill_completion_reaches_input_through_auto_approval_and_clears_on_dispose()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        var input = new SystemConsoleInput();
        var ui = new SpectreUserInterface(console, input);
        var wrapper = new AutoApprovingUserInterface(ui, new ApprovalState());
        wrapper.SetSkills(["caveman"]);
        input.SkillNames.Should().Equal("caveman");
        wrapper.SetSkills([]);
        input.SkillNames.Should().BeEmpty();
        wrapper.SetSkills(["audit"]);
        ui.Dispose();
        input.SkillNames.Should().BeEmpty();
    }

    [Fact]
    public void Forward_slash_completion_includes_enabled_skill_names_and_refreshes_after_removal()
    {
        var editor = new ComposerBuffer([]);
        editor.Insert("/ca");
        var completion = new ComposerCompletion { SkillNames = ["caveman", "audit"] };
        completion.Update(editor, []);
        completion.Options.Should().Contain("/caveman");
        completion.HandleKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false), editor).Should().BeTrue();
        editor.Text.Should().Be("/caveman ");

        editor.Replace(0, editor.Text.Length, "/au");
        completion.Update(editor, []);
        completion.Options.Should().Contain("/audit");
        completion.SkillNames = [];
        completion.Update(editor, []);
        completion.Options.Should().NotContain("/audit");
    }

    [Fact]
    public void Forward_slash_completion_supports_enter_and_navigation_beyond_six_commands()
    {
        var editor = new ComposerBuffer([]);
        editor.Insert("/he");
        var completion = new ComposerCompletion();
        completion.Update(editor, []);
        completion.Options.Should().Contain("/help");
        completion.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), editor).Should().BeFalse();
        editor.Text.Should().Be("/help");
        editor.Replace(0, editor.Text.Length, "/");
        completion.Update(editor, []);
        var target = Array.FindIndex(Codai.Application.SlashCommandParser.HelpText.Split('\n'), line => line.StartsWith("/mcps ", StringComparison.Ordinal));
        for (var index = 0; index < target; index++)
        {
            completion.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false), editor);
        }
        completion.Options.Should().HaveCountLessThanOrEqualTo(6).And.Contain("/mcps");
        completion.HandleKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false), editor).Should().BeTrue();
        editor.Text.Should().Be("/mcps ");
    }

    [Fact]
    public void History_navigation_restores_unsent_draft()
    {
        var editor = new ComposerBuffer(["first", "second"]);
        editor.Insert("draft");
        Press(editor, ConsoleKey.UpArrow);
        editor.Text.Should().Be("second");
        Press(editor, ConsoleKey.UpArrow);
        editor.Text.Should().Be("first");
        Press(editor, ConsoleKey.DownArrow);
        Press(editor, ConsoleKey.DownArrow);
        editor.Text.Should().Be("draft");
        editor.Cursor.Should().Be(5);
    }

    [Fact]
    public void Multiline_navigation_and_home_end_stay_on_current_line()
    {
        var editor = new ComposerBuffer([]);
        editor.Insert("longer");
        Press(editor, ConsoleKey.Enter, ConsoleModifiers.Alt);
        editor.Insert("next");
        Press(editor, ConsoleKey.Home);
        editor.Cursor.Should().Be(7);
        Press(editor, ConsoleKey.UpArrow);
        editor.Cursor.Should().Be(0);
        Press(editor, ConsoleKey.End);
        Press(editor, ConsoleKey.DownArrow);
        editor.Cursor.Should().Be(editor.Text.Length);
        editor.Text.Should().Be("longer\nnext");
    }

    [Fact]
    public void Midline_editing_and_word_deletion_preserve_suffix()
    {
        var editor = new ComposerBuffer([]);
        editor.Insert("one two three");
        Press(editor, ConsoleKey.LeftArrow, ConsoleModifiers.Control);
        Press(editor, ConsoleKey.W, ConsoleModifiers.Control);
        editor.Text.Should().Be("one three");
        editor.Cursor.Should().Be(4);
        Press(editor, ConsoleKey.Delete);
        Press(editor, ConsoleKey.Backspace);
        editor.Text.Should().Be("onehree");
    }

    [Fact]
    public void Surrogate_pairs_are_not_split_by_horizontal_editing()
    {
        var editor = new ComposerBuffer([]);
        editor.Insert("x\U0001F600y");
        Press(editor, ConsoleKey.LeftArrow);
        Press(editor, ConsoleKey.LeftArrow);
        editor.Cursor.Should().Be(1);
        Press(editor, ConsoleKey.Delete);
        editor.Text.Should().Be("xy");
    }

    [Theory]
    [InlineData("abcdef", 6, 3, 2, 0)]
    [InlineData("abc\nx", 5, 3, 1, 1)]
    [InlineData("one\ntwo\nthree\nfour", 18, 20, 2, 4)]
    [InlineData("\u754c\u754cX", 3, 4, 1, 1)]
    public void Layout_keeps_cursor_inside_wrapped_view(string text, int cursor, int width, int row, int column)
    {
        var layout = ComposerLayout.Create(text, cursor, width);
        layout.CursorRow.Should().Be(row);
        layout.CursorColumn.Should().Be(column);
        layout.Lines.Should().HaveCountLessThanOrEqualTo(3);
        layout.Lines.Should().OnlyContain(line => line.GetCellWidth() <= width);
    }

    [Fact]
    public void Completion_replaces_only_active_mention_and_preserves_suffix()
    {
        var editor = new ComposerBuffer([]);
        editor.Insert("review @src rest");
        for (var count = 0; count < 5; count++)
        {
            Press(editor, ConsoleKey.LeftArrow);
        }
        var completion = new ComposerCompletion();
        completion.Update(editor, ["src/One.cs", "src/Two.cs", "other.cs"]);
        completion.Options.Should().HaveCount(2);
        completion.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false), editor).Should().BeTrue();
        completion.HandleKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false), editor).Should().BeTrue();
        editor.Text.Should().Be("review @src/Two.cs rest");
        completion.Options.Should().BeEmpty();
    }

    [Fact]
    public void Command_completion_is_bounded_and_escape_dismisses_it()
    {
        var editor = new ComposerBuffer([]);
        editor.Insert("/mo");
        var completion = new ComposerCompletion();
        completion.Update(editor, []);
        completion.Options.Should().Equal("/models");
        completion.Options.Should().HaveCountLessThanOrEqualTo(6);
        completion.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false), editor).Should().BeTrue();
        completion.Options.Should().BeEmpty();
        editor.Text.Should().Be("/mo");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Completion_selects_folders_and_preserves_following_text(bool suffix)
    {
        var root = Directory.CreateTempSubdirectory("codai-folder-mentions-");
        try
        {
            root.CreateSubdirectory("src/nested");
            File.WriteAllText(Path.Combine(root.FullName, "src", "entry.cs"), string.Empty);
            var editor = new ComposerBuffer([]);
            editor.Insert("review @src" + (suffix ? " rest" : string.Empty));
            if (suffix)
            {
                for (var count = 0; count < 5; count++)
                {
                    Press(editor, ConsoleKey.LeftArrow);
                }
            }
            var completion = new ComposerCompletion();
            completion.Update(editor, ComposerCompletion.FindFiles(root.FullName));
            completion.Options.Should().Contain("@src/").And.Contain("@src/nested/").And.Contain("@src/entry.cs");
            completion.HandleKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false), editor).Should().BeTrue();
            editor.Text.Should().Be("review @src/ " + (suffix ? "rest" : string.Empty));
            completion.Options.Should().BeEmpty();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Completion_scan_excludes_generated_and_private_directories()
    {
        var root = Directory.CreateTempSubdirectory("codai-composer-");
        try
        {
            foreach (var name in new[] { "src", "bin", "obj", "node_modules", ".git", ".codai" })
            {
                var directory = root.CreateSubdirectory(name);
                File.WriteAllText(Path.Combine(directory.FullName, "entry.cs"), string.Empty);
            }
            ComposerCompletion.FindFiles(root.FullName).Should().Equal("src/", "src/entry.cs");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var scan = () => ComposerCompletion.FindFiles(root.FullName, cancellation.Token);
            scan.Should().Throw<OperationCanceledException>();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(24, AgentMode.Plan)]
    [InlineData(80, AgentMode.Plan)]
    [InlineData(120, AgentMode.Agent)]
    public void Composer_rendering_preserves_literal_text_and_current_model(int width, AgentMode mode)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No });
        console.Profile.Width = width;
        console.Profile.Height = 24;
        var input = new SystemConsoleInput();
        _ = new SpectreUserInterface(console, input, mode: mode, model: new string('m', 160));
        input.ComposerRedrawHandler!("[red]literal[/]", 14, ["@src/Test.cs"], 0);
        var output = writer.ToString();
        output.Should().Contain("[red]literal[/]").And.Contain("mmm");
        output.Split('\n').Should().OnlyContain(line => line.TrimEnd('\r').GetCellWidth() <= width);
    }

    [Fact]
    public async Task Approval_uses_response_channel_not_composer()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No, Interactive = InteractionSupport.Yes });
        var input = new ResponseInput();
        var ui = new SpectreUserInterface(console, input);
        (await ui.ConfirmAsync("Allow change?")).Should().BeTrue();
        input.ResponseReads.Should().Be(1);
    }

    [Fact]
    public void Disposing_terminal_releases_owned_input_callbacks()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });
        var input = new SystemConsoleInput();
        var ui = new SpectreUserInterface(console, input);
        input.KeyHandler.Should().NotBeNull();
        input.MouseHandler.Should().NotBeNull();
        ui.Dispose();
        input.KeyHandler.Should().BeNull();
        input.MouseHandler.Should().BeNull();
        input.OverlayKeyHandler.Should().BeNull();
        input.PaletteCommandHandler.Should().BeNull();
        input.ComposerRedrawHandler.Should().BeNull();
        input.RedrawHandler.Should().BeNull();
    }

    [Fact]
    public async Task Interactive_frames_stay_in_alternate_screen_until_terminal_is_restored()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        using var ui = new SpectreUserInterface(console, new ChatInput());
        await ui.ReadInputAsync();
        ui.WriteMarkdown(string.Join('\n', Enumerable.Repeat("Answer line", 40)));
        console.Profile.Width = 60;
        console.Profile.Height = 18;
        ui.ShowTokens("model", new TokenUsage(100, 20), new TokenUsage(100, 20), 0.01m);
        await ui.ReadInputAsync();

        var frames = writer.ToString();
        frames.Should().StartWith("\u001b[?1049h");
        frames.Should().Contain("\u001b[2 q").And.Contain("\u001b[?2004h").And.NotContain("\u001b[6 q");
        frames.Split("\u001b[?1049h", StringSplitOptions.None).Should().HaveCount(2);
        frames.Should().NotContain("\u001b[?1049l");
        PlainOutput(writer).Should().Contain("model").And.Contain("$0.01");

        writer.GetStringBuilder().Clear();
        ui.RestoreTerminal();
        writer.ToString().Should().Be("\u001b[?2026l\u001b[r\u001b[0m\u001b[0 q\u001b[?25h\u001b[?2004l\u001b[?1003l\u001b[?1006l\u001b[?1049l");
        writer.GetStringBuilder().Clear();
        ui.Dispose();
        writer.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task Input_failure_restores_shell_screen()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        using var ui = new SpectreUserInterface(console, new ResponseInput());

        var read = () => ui.ReadInputAsync();
        await read.Should().ThrowAsync<InvalidOperationException>();

        writer.ToString().Should().StartWith("\u001b[?1049h").And.EndWith("\u001b[?1049l");
        writer.GetStringBuilder().Clear();
        ui.Dispose();
        writer.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task Non_ansi_input_does_not_switch_screen_buffers()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.No,
            Interactive = InteractionSupport.No,
        });
        using var ui = new SpectreUserInterface(console, new ChatInput());
        await ui.ReadInputAsync();
        ui.RestoreTerminal();
        writer.ToString().Should().NotContain("\u001b[?1049").And.NotContain("\u001b[2 q").And.NotContain("\u001b[0 q");
    }

    [Fact]
    public async Task Submission_keeps_composer_visible_in_chat_view()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        using var ui = new SpectreUserInterface(console, new ChatInput(), projectRoot: "codai-workspace");
        (await ui.ReadInputAsync()).Should().Be("hello");
        var chat = writer.ToString();
        chat = chat[(chat.LastIndexOf("\u001b[2J", StringComparison.Ordinal) + 4)..];
        chat.Should().Contain("configured model").And.Contain("hello").And.Contain("\u001b[1;1H");
    }

    [Fact]
    public void Settings_palette_filters_navigates_and_selects_commands()
    {
        var palette = new SettingsPalette();
        foreach (var character in "auth")
        {
            palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.S, false, false, false));
        }
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Closed.Should().BeTrue();
        palette.Command.Should().Be("/auth");
    }

    [Theory]
    [InlineData(44, 22)]
    [InlineData(110, 40)]
    public void Settings_view_has_minimal_search_without_header_tabs(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;

        new SettingsPalette().Draw(console);

        var output = PlainOutput(writer);
        output.Should().Contain("Settings").And.NotContain("Status").And.NotContain("Config").And.NotContain("Usage").And.NotContain("Stats").And.Contain("Search settings")
            .And.Contain("Session").And.NotContain("Current  Sessions")
            .And.NotContain("Issues  Pull requests");
        writer.ToString().Should().Contain("\u001b[2;3H");
    }

    [Fact]
    public void Settings_palette_labels_theme_action_and_selects_it()
    {
        var palette = new SettingsPalette();
        foreach (var character in "theme")
        {
            palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.T, false, false, false));
        }
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        palette.Draw(console);
        writer.ToString().Should().Contain("Themes").And.NotContain("Appearance");
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be("/themes");
    }

    [Theory]
    [InlineData(24, 12)]
    [InlineData(44, 22)]
    [InlineData(110, 40)]
    public void Command_search_filters_descriptions_and_keeps_selection_visible(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;
        var palette = new SettingsPalette();
        palette.Draw(console);
        PlainOutput(writer).Should().Contain("Session");

        foreach (var character in "provider")
        {
            palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.I, false, false, false));
        }
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));
        writer.GetStringBuilder().Clear();
        palette.Draw(console);
        PlainOutput(writer).Should().Contain("Auth").And.NotContain("Session");
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be("/auth");
    }

    [Fact]
    public void Provider_palette_shows_only_names_without_empty_description_rows()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        var palette = new SettingsPalette("Codai / Provider", Enumerable.Range(0, 7)
            .Select(index => new Codai.Core.Abstractions.SessionMenuItem($"provider{index}", string.Empty, $"provider{index}")).ToArray());
        palette.Draw(console);
        writer.ToString().Should().Contain("provider0").And.Contain("provider6");

        for (var index = 0; index < 6; index++)
        {
            palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        }
        writer.GetStringBuilder().Clear();
        palette.Draw(console);
        writer.ToString().Should().Contain("provider0").And.Contain("provider6");
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be("provider6");
    }

    [Theory]
    [InlineData(24, 12)]
    [InlineData(44, 22)]
    [InlineData(110, 40)]
    public void Settings_navigation_skips_headings_and_scrolls_across_every_category(int width, int height)
    {
        string[] labels = ["Session", "New", "Models", "Agents", "Compact", "Auto-compact", "Skills", "Diff", "Themes",
            "Plugins", "MCPs", "Auth", "Update", "Help", "Docs", "Exit"];
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;
        var palette = new SettingsPalette();
        for (var index = 0; index < labels.Length; index++)
        {
            writer.GetStringBuilder().Clear();
            palette.Draw(console);
            palette.Selected.Should().Be(index);
            PlainOutput(writer).Should().Contain(labels[index][..Math.Min(10, labels[index].Length)]);
            if (index < labels.Length - 1)
            {
                palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
            }
        }
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be("/exit");
    }

    [Fact]
    public void Settings_palette_shows_all_actions_without_scrolling_on_wide_terminals()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 110;
        console.Profile.Height = 40;
        new SettingsPalette().Draw(console);

        foreach (var label in new[] { "Session", "New", "Models", "Agents", "Compact", "Auto-compact", "Skills", "Diff", "Themes",
            "Plugins", "MCPs", "Auth", "Update", "Help", "Docs", "Exit" })
        {
            PlainOutput(writer).Should().Contain(label);
        }
        PlainOutput(writer).Should().NotContain("more");
    }

    [Theory]
    [InlineData("Session", "/session")]
    [InlineData("New", "/new")]
    [InlineData("Models", "/models")]
    [InlineData("Agents", "/agents")]
    [InlineData("Compact", "/compact")]
    [InlineData("Auto-compact", "/autocompact")]
    [InlineData("Skills", "/skills")]
    [InlineData("Diff", "/diff")]
    [InlineData("Themes", "/themes")]
    [InlineData("Plugins", "/plugins")]
    [InlineData("MCPs", "/mcps")]
    [InlineData("Auth", "/auth")]
    [InlineData("Update", "/update")]
    [InlineData("Help", "/help")]
    [InlineData("Docs", ":docs")]
    [InlineData("Exit", "/exit")]
    public void Settings_action_titles_match_command_names_and_preserve_dispatch(string title, string command)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 110;
        console.Profile.Height = 40;
        console.Profile.Capabilities.Unicode = false;
        var palette = new SettingsPalette();
        foreach (var character in title)
        {
            palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.A, false, false, false));
        }

        palette.Draw(console);

        PlainOutput(writer).Should().Contain("> " + title + " ");
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be(command);
    }

    [Fact]
    public void Settings_catalog_contains_only_requested_actions_and_dispatches_existing_workflows()
    {
        string[] commands = ["/session", "/new", "/models", "/agents", "/compact", "/autocompact", "/skills", "/diff", "/themes",
            "/plugins", "/mcps", "/auth", "/update", "/help", ":docs", "/exit"];
        for (var index = 0; index < commands.Length; index++)
        {
            var palette = new SettingsPalette();
            for (var move = 0; move < index; move++)
            {
                palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
            }
            palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            palette.Command.Should().Be(commands[index]);
        }
        var last = new SettingsPalette();
        last.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));
        last.Selected.Should().Be(commands.Length - 1);

        foreach (var removed in new[] { "/init", "approval", "debug", "editor", "review", "status", "variants" })
        {
            var palette = new SettingsPalette();
            foreach (var character in removed)
            {
                palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.A, false, false, false));
            }
            palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            palette.Command.Should().BeNull();
        }
    }

    [Fact]
    public void Settings_palette_model_selection_and_escape_are_local()
    {
        var palette = new SettingsPalette();
        foreach (var character in "/models")
        {
            palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.M, false, false, false));
        }
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be("/models");
        var cancelled = new SettingsPalette();
        cancelled.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        cancelled.Closed.Should().BeTrue();
        cancelled.Command.Should().BeNull();
    }

    [Theory]
    [InlineData(44, 22)]
    [InlineData(110, 40)]
    public void Menu_uses_compact_rows_escape_hint_and_keyboard_paging(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;
        var palette = new SettingsPalette("Select model", Enumerable.Range(0, 20)
            .Select(index => new Codai.Core.Abstractions.SessionMenuItem($"Model-{index}", "Local model", $"model-{index}")).ToArray());

        palette.Draw(console);

        PlainOutput(writer).Should().Contain("Esc").And.Contain("Select model").And.Contain("Model-6").And.NotContain("Search by name");
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));
        writer.GetStringBuilder().Clear();
        palette.Draw(console);
        PlainOutput(writer).Should().Contain("Model-19");
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be("model-19");
    }

    [Theory]
    [InlineData(24, 12)]
    [InlineData(44, 22)]
    [InlineData(110, 40)]
    public void Settings_modal_renders_responsively_and_preserves_draft(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input);
        var draft = "unsent request";
        input.ComposerRedrawHandler!(draft, draft.Length, [], 0);
        input.OverlayKeyHandler!(new ConsoleKeyInfo('\r', ConsoleKey.M, false, false, true)).Should().BeTrue();
        writer.GetStringBuilder().Clear();
        input.ComposerRedrawHandler!(draft, draft.Length, [], 0);
        writer.ToString().Should().Contain("Settings").And.Contain("Search");
        input.OverlayKeyHandler!(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false)).Should().BeTrue();
        writer.GetStringBuilder().Clear();
        input.ComposerRedrawHandler!(draft, draft.Length, [], 0);
        writer.ToString().Should().Contain(draft).And.NotContain("Switch session");
        input.PaletteCommandHandler!().Should().BeNull();
        input.OverlayKeyHandler!(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)).Should().BeFalse();
    }

    [Fact]
    public void Settings_modal_dispatches_existing_session_command_once()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input);
        input.OverlayKeyHandler!(new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false));
        input.OverlayKeyHandler!(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        input.PaletteCommandHandler!().Should().Be("/session");
        input.PaletteCommandHandler!().Should().BeNull();
    }

    [Fact]
    public void Documentation_action_opens_browser_without_sending_a_model_request()
    {
        VerifyDocumentationAction(browserFails: false);
    }

    [Fact]
    public void Documentation_action_preserves_url_and_draft_if_browser_is_unavailable()
    {
        VerifyDocumentationAction(browserFails: true);
    }

    private static void VerifyDocumentationAction(bool browserFails)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 110;
        console.Profile.Height = 40;
        var input = new SystemConsoleInput();
        var addresses = new List<string>();
        using var ui = new SpectreUserInterface(console, input, openBrowser: address =>
        {
            addresses.Add(address.AbsoluteUri);
            if (browserFails)
            {
                throw new System.ComponentModel.Win32Exception("No browser available");
            }
        });
        const string draft = "unsent request";
        input.ComposerRedrawHandler!(draft, draft.Length, [], 0);
        input.OverlayKeyHandler!(new ConsoleKeyInfo('\0', ConsoleKey.P, false, false, true));
        foreach (var character in "docs")
        {
            input.OverlayKeyHandler!(new ConsoleKeyInfo(character, ConsoleKey.D, false, false, false));
        }
        input.OverlayKeyHandler!(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        addresses.Should().Equal("https://meysamhadeli.github.io/codai");
        input.PaletteCommandHandler!().Should().BeNull();
        writer.ToString().Should().Contain("https://meysamhadeli.github.io/codai");
        if (browserFails)
        {
            writer.ToString().Should().Contain("Could not open documentation");
        }
        writer.GetStringBuilder().Clear();
        input.ComposerRedrawHandler!(draft, draft.Length, [], 0);
        writer.ToString().Should().Contain(draft).And.NotContain("Switch session");
    }

    [Fact]
    public async Task Escape_closes_settings_without_cancelling_active_turn()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        var input = new PaletteTurnInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var completed = await ui.RunTurnAsync(async token =>
        {
            await input.Closed.Task.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            return true;
        }, deadline.Token);
        completed.Should().BeTrue();
        writer.ToString().Should().NotContain("Request stopped");
    }

    [Theory]
    [InlineData("agents", "/agents")]
    [InlineData("models", "/models")]
    public void Command_actions_dispatch_without_bypassing_session_state(string search, string command)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        var mode = new AgentModeState(AgentMode.Agent);
        var approval = new ApprovalState(false);
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input, modeState: mode, approvalState: approval);
        input.OverlayKeyHandler!(new ConsoleKeyInfo('\x10', ConsoleKey.P, false, false, true)).Should().BeTrue();
        foreach (var character in search)
        {
            input.OverlayKeyHandler!(new ConsoleKeyInfo(character, ConsoleKey.A, false, false, false));
        }
        input.OverlayKeyHandler!(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        approval.AutoApprove.Should().BeFalse();
        mode.Mode.Should().Be(AgentMode.Agent);
        input.PaletteCommandHandler!().Should().Be(command);
    }

    private sealed class PaletteTurnInput : IConsoleInput
    {
        private int _index;
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? ReadLine() => "hello";
        public bool TryReadKey(out ConsoleKeyInfo key)
        {
            key = default;
            if (_index == 0)
            {
                _index++;
                key = new ConsoleKeyInfo('\r', ConsoleKey.M, false, false, true);
                return true;
            }
            if (_index++ == 1)
            {
                key = new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false);
                Closed.TrySetResult();
                return true;
            }
            return false;
        }
    }

    [Fact]
    public void Settings_submenus_return_values_and_secret_forms_do_not_render_secrets()
    {
        var submenu = new SettingsPalette("Codai / Connections", [new("Local tools", "Configured connection", "server-id")]);
        submenu.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        submenu.Command.Should().Be("server-id");
        var form = new SettingsPalette("Codai / Credential", initialValue: "private-value", secret: true);
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });
        form.Draw(console);
        writer.ToString().Should().NotContain("private-value").And.Contain("*************");
        form.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        form.Command.Should().Be("private-value");
    }

    private sealed class ChatInput : IConsoleInput
    {
        public string? ReadLine() => "hello";
    }

    private sealed class SubmittedInput(string text) : IConsoleInput
    {
        public string? ReadLine() => text;
    }

    [Fact]
    public void Transcript_markdown_renders_structures_without_markup_leaks()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No });
        console.Profile.Width = 80;
        console.Write(TranscriptMarkdown.Render("## Result\n\n**Ready** and `code` [red]literal[/]\n\n| File | State |\n| --- | --- |\n| main.cs | done |\n\n```cs\nreturn 1;\n```\n"));
        var output = writer.ToString();
        output.Should().Contain("Result").And.Contain("Ready").And.Contain("[red]literal[/]")
            .And.Contain("main.cs").And.Contain("return 1;").And.NotContain("**Ready**").And.NotContain("```cs");
    }

    [Fact]
    public void Transcript_streams_partial_text_and_expands_compact_tool_output()
    {
        var transcript = new ChatTranscript();
        transcript.AddUser("build feature");
        transcript.AppendMarkdown("Partial answer");
        string.Join('\n', transcript.GetLines(70)).Should().Contain("Partial answer");
        transcript.StartTool("read_file", "src/Main.cs");
        transcript.CompleteTool("read_file", true, "finished");
        string.Join('\n', transcript.GetLines(70)).Should().Contain("Read(src/Main.cs)").And.NotContain("(done)");
        transcript.ToolResult("read_file", string.Join('\n', Enumerable.Range(1, 12).Select(number => $"source row {number}")));
        string.Join('\n', transcript.GetLines(70)).Should().Contain("source row 1").And.Contain("9 more lines")
            .And.NotContain("source row 12");
        transcript.ToggleTool().Should().BeTrue();
        string.Join('\n', transcript.GetLines(70)).Should().Contain("source row 12").And.NotContain("more lines");
        transcript.Scroll(5);
        transcript.ScrollOffset.Should().Be(5);
        transcript.Scroll(-100);
        transcript.ScrollOffset.Should().Be(0);
    }

    [Fact]
    public async Task Escape_cancels_only_active_turn_and_leaves_parent_token_usable()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        using var parent = new CancellationTokenSource();
        using var ui = new SpectreUserInterface(console, new EscapeInput());
        await ui.ReadInputAsync(parent.Token);
        var result = await ui.RunTurnAsync(async token =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token);
                return false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return true;
            }
        }, parent.Token);
        result.Should().BeTrue();
        parent.IsCancellationRequested.Should().BeFalse();
        (await ui.RunTurnAsync(token => Task.FromResult(!token.IsCancellationRequested), parent.Token)).Should().BeTrue();
        writer.ToString().Should().Contain("Session remains open");
    }

    private sealed class EscapeInput : IConsoleInput
    {
        public string? ReadLine() => "hello";

        public bool TryReadKey(out ConsoleKeyInfo key)
        {
            key = new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false);
            return true;
        }
    }

    [Fact]
    public async Task Input_queued_during_response_is_used_by_next_turn_without_another_read()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        var input = new QueuedTurnInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ui.RunTurnAsync(token => input.Finished.Task.WaitAsync(token), deadline.Token);
        (await ui.ReadInputAsync()).Should().Be("next");
        input.Reads.Should().Be(1);
    }

    private sealed class QueuedTurnInput : IConsoleInput
    {
        private readonly Queue<char> _keys = new("next\r");
        public TaskCompletionSource<bool> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads { get; private set; }

        public string? ReadLine()
        {
            Reads++;
            return "first";
        }

        public bool TryReadKey(out ConsoleKeyInfo key)
        {
            key = default;
            if (!_keys.TryDequeue(out var character))
            {
                return false;
            }
            key = new ConsoleKeyInfo(character, character == '\r' ? ConsoleKey.Enter : ConsoleKey.A, false, false, false);
            if (character == '\r')
            {
                Finished.TrySetResult(true);
            }
            return true;
        }
    }

    [Fact]
    public async Task Tool_headers_use_targets_not_source_payload_and_accept_invalid_argument_shapes()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        using var ui = new SpectreUserInterface(console, new ChatInput());
        await ui.ReadInputAsync();
        writer.GetStringBuilder().Clear();
        using var arguments = System.Text.Json.JsonDocument.Parse("{\"path\":\"sample.cs\",\"old_string\":\"private source payload\"}");
        ui.WriteToolStarted("edit_file", "verbose source summary", arguments.RootElement);
        PlainOutput(writer).Should().Contain("sample.cs").And.NotContain("private source payload").And.NotContain("verbose source summary");
        using var invalid = System.Text.Json.JsonDocument.Parse("[]");
        var display = () => ui.WriteToolStarted("edit_file", "invalid arguments", invalid.RootElement);
        display.Should().NotThrow();
    }

    [Fact]
    public void Unchanged_transcript_draw_emits_no_terminal_writes()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 80;
        var transcript = new ChatTranscript();
        transcript.Add("Stable output");
        transcript.Draw(console, 10);
        writer.GetStringBuilder().Clear();
        transcript.Draw(console, 10);
        writer.ToString().Should().BeEmpty();
        transcript.Add("New output");
        transcript.Draw(console, 10);
        writer.ToString().Should().Contain("New output").And.NotContain("Stable output");
    }

    [Fact]
    public void Transcript_scrollbar_paints_thin_last_column_without_wrapping_and_repaints_after_resize()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 80;
        var transcript = new ChatTranscript();
        transcript.Add(string.Join('\n', Enumerable.Range(1, 30).Select(number => $"row {number}")));
        transcript.Draw(console, 5);
        writer.ToString().Should().Contain(";80H\u001b[38;5;").And.Contain("\u2595\u001b[0m\u001b[").And.NotContain(";79H");
        writer.GetStringBuilder().Clear();
        console.Profile.Width = 40;
        transcript.Draw(console, 5);
        writer.ToString().Should().Contain(";40H\u001b[38;5;").And.Contain("row 30").And.NotContain(";80H");
        writer.GetStringBuilder().Clear();
        transcript.Clear();
        transcript.Add("Short output");
        transcript.Draw(console, 5);
        writer.ToString().Should().Contain("Short output").And.Contain("\u001b[2K").And.NotContain("\u2595");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Select model")]
    [InlineData("Select agent")]
    public void Menu_navigation_hides_cursor_and_search_and_composer_restore_it(string? title)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 110;
        console.Profile.Height = 40;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input);
        var palette = title is null ? new SettingsPalette() : new SettingsPalette(title, [new("Quick choice", "Choice", "quick")]);
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        var overlay = typeof(SpectreUserInterface).GetField("_palette", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        overlay.SetValue(ui, palette);

        input.ComposerRedrawHandler!("draft", 5, [], 0);

        writer.ToString().Should().EndWith("\u001b[?25l\u001b[?2026l");
        writer.GetStringBuilder().Clear();
        palette.HandleKey(new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false));

        input.ComposerRedrawHandler!("draft", 5, [], 0);

        writer.ToString().Should().EndWith("\u001b[?25h\u001b[?2026l");
        writer.GetStringBuilder().Clear();
        overlay.SetValue(ui, null);

        input.ComposerRedrawHandler!("draft", 5, [], 0);

        writer.ToString().Should().EndWith("\u001b[?25h\u001b[?2026l");
    }

    [Fact]
    public void Composer_redraw_batches_frame_and_restores_cursor_after_render_failure()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input);
        input.ComposerRedrawHandler!("draft", 5, [], 0);
        writer.ToString().Should().StartWith("\u001b[?2026h\u001b[?25l").And.EndWith("\u001b[?25h\u001b[?2026l");
        writer.GetStringBuilder().Clear();
        var render = () => input.ComposerRedrawHandler!("draft", 5, null!, 0);
        render.Should().Throw<NullReferenceException>();
        writer.ToString().Should().EndWith("\u001b[?25h\u001b[?2026l");
    }

    private sealed class ResponseInput : IConsoleInput
    {
        public int ResponseReads { get; private set; }

        public string? ReadLine() => throw new InvalidOperationException("Composer must not read approval responses.");

        public Task<string?> ReadResponseAsync(CancellationToken cancellationToken = default)
        {
            ResponseReads++;
            return Task.FromResult<string?>("yes");
        }
    }

    [Fact]
    public async Task Confirmation_uses_inline_decision_frame_without_switching_terminal_buffers()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        var input = new QuestionInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        ui.WriteLine("Keep this conversation visible.");
        writer.GetStringBuilder().Clear();
        input.Keys.Enqueue(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        (await ui.ConfirmAsync("Continue with this change?", deadline.Token)).Should().BeTrue();

        PlainOutput(writer).Should().Contain("Confirm action").And.Contain("Continue with this change?")
            .And.Contain("Keep this conversation visible.").And.NotContain("Search");
        writer.ToString().Should().NotContain("\u001b[?1049h");
    }

    [Theory]
    [InlineData("yes", true, true)]
    [InlineData("no", false, true)]
    [InlineData("default", true, true)]
    [InlineData("escape", false, true)]
    [InlineData("yes", true, false)]
    public async Task Confirmations_default_to_yes_and_preserve_draft(string choice, bool expected, bool initialized)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        var input = new QuestionInput();
        using var ui = new SpectreUserInterface(console, input);
        if (initialized)
        {
            await ui.ReadInputAsync();
        }
        ui.InsertInput("saved draft");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var accepted = await ui.RunTurnAsync(token =>
        {
            if (choice == "no")
            {
                input.Keys.Enqueue(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
            }
            input.Keys.Enqueue(choice == "escape"
                ? new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false)
                : new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            return ui.ConfirmAsync("Continue with this change?", token);
        }, deadline.Token);

        accepted.Should().Be(expected);
        input.Reads.Should().Be(initialized ? 1 : 0);
        PlainOutput(writer).Should().Contain("Continue with this change?").And.Contain("No").And.Contain("Yes")
            .And.Contain("saved draft").And.NotContain("[y/N]");
        (await ui.RunTurnAsync(token => Task.FromResult(!token.IsCancellationRequested), deadline.Token)).Should().BeTrue();
    }

    [Fact]
    public async Task Cancelled_confirmation_restores_dialog_input_for_next_question()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        var input = new QuestionInput();
        using var ui = new SpectreUserInterface(console, input);
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var ask = () => ui.ConfirmAsync("Continue?", cancelled.Token);
        await ask.Should().ThrowAsync<OperationCanceledException>();

        input.Keys.Enqueue(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        (await ui.ConfirmAsync("Try again?", deadline.Token)).Should().BeTrue();
        input.Reads.Should().Be(0);
        PlainOutput(writer).Should().Contain("Try again?");
    }

    [Theory]
    [InlineData("choice", "Second choice")]
    [InlineData("custom", "my answer")]
    [InlineData("cancel", null)]
    [InlineData("free", "my answer")]
    public async Task Questions_own_input_during_turn_and_preserve_draft(string scenario, string? expected)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        var input = new QuestionInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        ui.InsertInput("saved draft");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var answer = await ui.RunTurnAsync(token =>
        {
            if (scenario == "cancel")
            {
                input.Keys.Enqueue(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
            }
            else
            {
                if (scenario != "free")
                {
                    input.Keys.Enqueue(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
                    if (scenario == "custom")
                    {
                        input.Keys.Enqueue(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
                    }
                    input.Keys.Enqueue(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
                }
                if (scenario is "custom" or "free")
                {
                    foreach (var character in "my answer\r")
                    {
                        input.Keys.Enqueue(new ConsoleKeyInfo(character, character == '\r' ? ConsoleKey.Enter : ConsoleKey.A, false, false, false));
                    }
                }
            }
            return new InteractiveClarificationHandler(ui).AskAsync("Where should this go?",
                scenario == "free" ? [] : ["First choice", "Second choice"], token);
        }, deadline.Token);
        answer.Should().Be(expected);
        input.Reads.Should().Be(1);
        PlainOutput(writer).Should().Contain("saved draft").And.Contain("Where should this go?");
        (await ui.RunTurnAsync(token => Task.FromResult(!token.IsCancellationRequested), deadline.Token)).Should().BeTrue();
    }

    [Theory]
    [InlineData(44)]
    [InlineData(80)]
    public void Question_template_wraps_long_question_and_choice(int width)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = 24;
        var question = "Question " + new string('q', 64) + " end?";
        var choice = "New project " + new string('p', 80) + " option-end";
        var palette = new DecisionPanel("Question", question, [new(choice, string.Empty, "chosen")]);
        palette.Draw(console);
        PlainOutput(writer).Should().Contain("end?").And.Contain("option-end").And.NotContain("Search");
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be("chosen");
    }

    [Fact]
    public void Inline_question_scrolls_choices_without_hiding_selected_answer()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 44;
        console.Profile.Height = 22;
        var panel = new DecisionPanel("Question", "Choose a destination",
            Enumerable.Range(1, 15).Select(index => new Codai.Core.Abstractions.SessionMenuItem("Destination " + index, string.Empty,
                index.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray());
        for (var index = 0; index < 14; index++)
        {
            panel.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        }

        panel.Draw(console, minimumTop: 4);
        panel.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        PlainOutput(writer).Should().Contain("> 15. Destination 15").And.Contain("Esc to cancel");
        panel.Command.Should().Be("15");
        writer.ToString().Should().NotContain("\u001b[2J").And.NotContain("\u001b[?1049h");
    }

    [Fact]
    public void Free_text_question_pages_context_without_losing_answer()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        var panel = new DecisionPanel("Question", string.Join('\n', Enumerable.Range(1, 25).Select(index => "Context line " + index)));
        foreach (var character in "kept answer")
        {
            panel.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.A, false, false, false));
        }
        for (var index = 0; index < 12; index++)
        {
            panel.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.PageDown, false, false, false));
        }

        panel.Draw(console, minimumTop: 4);
        panel.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        PlainOutput(writer).Should().Contain("Context line 25").And.Contain("kept answer").And.Contain("Enter to confirm");
        panel.Command.Should().Be("kept answer");
    }

    private sealed class QuestionInput : IConsoleInput
    {
        public Queue<ConsoleKeyInfo> Keys { get; } = new();
        public int Reads { get; private set; }
        public string? ReadLine()
        {
            Reads++;
            return Reads == 1 ? "request" : throw new InvalidOperationException("Question must not use chat input.");
        }

        public bool TryReadKey(out ConsoleKeyInfo key) => Keys.TryDequeue(out key);
    }

    private static void Press(ComposerBuffer editor, ConsoleKey key, ConsoleModifiers modifiers = ConsoleModifiers.None) =>
        editor.HandleKey(new ConsoleKeyInfo('\0', key, modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt), modifiers.HasFlag(ConsoleModifiers.Control)));
}
