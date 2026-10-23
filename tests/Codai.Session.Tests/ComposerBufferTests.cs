using Codai.Cli;
using Codai.Cli.Ui;
using Codai.Core.Agents;
using Codai.Core.Tokens;
using FluentAssertions;
using Spectre.Console;

namespace Codai.Session.Tests;

public class ComposerBufferTests
{
    private static string PlainOutput(StringWriter writer) =>
        System.Text.RegularExpressions.Regex.Replace(writer.ToString(), "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Work_uses_pulsing_star_with_ascii_fallback(bool unicode)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        console.Profile.Capabilities.Unicode = unicode;
        var input = new QueuedTurnInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        writer.GetStringBuilder().Clear();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ui.RunTurnAsync(token => input.Finished.Task.WaitAsync(token), deadline.Token);
        PlainOutput(writer).Should().Contain(unicode ? "· Thinking" : ". Thinking")
            .And.Contain(unicode ? "✧ Thinking" : "+ Thinking")
            .And.NotContain("/ Thinking").And.NotContain("\\ Thinking").And.NotContain("| Thinking");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Activity_text_animates_with_and_without_color(bool color)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 44;
        console.Profile.Height = 22;
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
        plain.Should().Contain("Reasoning (esc to interrupt)").And.NotContain("Reasoning.");
        if (color)
        {
            output.Should().Contain("Rea\u001b[").And.Contain("eas\u001b[");
        }
        (await ui.ReadInputAsync()).Should().Be("next");
    }

    [Fact]
    public async Task Long_waits_report_elapsed_time_tokens_and_the_interrupt_hint()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 120;
        console.Profile.Height = 30;
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
            .MatchRegex(@"esc to interrupt · \d+s · ↓ \d+\.\d+k tokens");
    }

    [Fact]
    public async Task Narrow_terminals_drop_the_hint_before_the_activity_itself()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 22;
        console.Profile.Height = 22;
        var input = new QueuedTurnInput();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        writer.GetStringBuilder().Clear();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ui.RunTurnAsync(token => input.Finished.Task.WaitAsync(token), deadline.Token);

        PlainOutput(writer).Should().Contain("Thinking").And.NotContain("esc to interrupt");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Loading_work_keeps_its_phase_and_accepts_drafts(bool nested)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
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
        PlainOutput(writer).Should().Contain("Loading");
        (await ui.ReadInputAsync()).Should().Be("next");
    }

    [Fact]
    public async Task Turn_accepts_draft_keys_while_work_is_synchronous()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
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
        PlainOutput(writer).Should().Contain("Loading workspace   ").And.Contain("Loading workspace.  ").And.NotContain("Working");
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
            .And.Contain(activity.TrimEnd('.'))
            .And.Contain("Responding")
            .And.NotContain("generating")
            .And.NotContain("Esc: stop");
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(64_000, 0, 3)]
    [InlineData(32_000, 32_000, 3)]
    [InlineData(128_000, 0, 6)]
    [InlineData(256_000, 0, 6)]
    public void Footer_progress_uses_latest_context_and_preserves_session_totals(int inputTokens, int outputTokens, int filled)
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
        output.Should().Contain("codai-model").And.Contain("(312,345) $1.23");
        if (filled > 0)
        {
            output.Should().Contain("\u001b[48;5;8m" + new string(' ', filled));
        }
        if (filled < 6)
        {
            output.Should().Contain("\u001b[48;5;237m" + new string(' ', 6 - filled));
        }
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
        writer.ToString().Split("Codai v", StringSplitOptions.None).Should().HaveCount(2);
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
        output.Should().Contain("Codai v").And.Contain("Ctrl+P")
            .And.NotContain("codai-workspace").And.NotContain("header-provider").And.NotContain(new string('m', 20));
        output.Split('\n').Should().OnlyContain(line => line.TrimEnd('\r').GetCellWidth() <= width);
        if (width >= 64 && height >= 16)
        {
            output.Should().Contain("/ __|").And.Contain("|___|");
            output.IndexOf("|___|", StringComparison.Ordinal).Should().BeLessThan(output.IndexOf("Codai v", StringComparison.Ordinal));
        }
        else
        {
            output.Should().NotContain("/ __|");
        }
        output.IndexOf("Codai v", StringComparison.Ordinal).Should().BeLessThan(output.IndexOf("Ctrl+P", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AgentMode.Plan, true)]
    [InlineData(AgentMode.Agent, true)]
    [InlineData(AgentMode.Plan, false)]
    [InlineData(AgentMode.Agent, false)]
    public void Footer_uses_state_colors_and_gray_shortcuts(AgentMode mode, bool autoApprove)
    {
        Codai.Configuration.CommandLineParser.Parse(["code"]).Mode.Should().Be(AgentMode.Agent);
        Codai.Configuration.CommandLineParser.Parse(["code", "--mode", "plan"]).Mode.Should().Be(AgentMode.Plan);
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 100;
        console.Profile.Height = 30;
        console.Profile.Capabilities.ColorSystem = ColorSystem.EightBit;
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input, mode: mode, autoApprove: autoApprove);
        input.ComposerRedrawHandler!(string.Empty, 0, [], 0);
        var output = writer.ToString();
        output.Should().Contain(autoApprove ? "\u001b[38;5;2mAuto-approval ON" : "\u001b[38;5;8mApproval required")
            .And.Contain($"\u001b[38;5;39m[{mode}]")
            .And.Contain("\u001b[38;5;8m  (Shift+Tab)")
            .And.NotContain(": change");
        PlainOutput(writer).Should().NotContain("Ask");
        var labels = string.Join("  ", Enum.GetValues<AgentMode>().Select(value => value == mode ? $"[{value}]" : value.ToString())) + " (Tab)";
        var parts = labels.Split($"[{mode}]", StringSplitOptions.None);
        if (parts[0].Length > 0)
        {
            output.Should().Contain("\u001b[38;5;8m" + parts[0]);
        }
        output.Should().Contain("\u001b[38;5;8m" + parts[1]);
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

    [Theory]
    [InlineData(1, "mode:Plan")]
    [InlineData(2, "mode:Agent")]
    public void Mode_palette_contains_only_plan_and_agent(int moves, string command)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No });
        console.Profile.Width = 100;
        console.Profile.Height = 30;
        var palette = new SettingsPalette();
        foreach (var character in "mode")
        {
            palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.M, false, false, false));
        }

        palette.Draw(console);
        writer.ToString().Should().Contain("Plan mode").And.Contain("Agent mode").And.NotContain("Ask mode");
        for (var index = 0; index < moves; index++)
        {
            palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        }
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be(command);
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
        for (var index = 0; index < 8; index++)
        {
            completion.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false), editor);
        }
        completion.Options.Should().HaveCountLessThanOrEqualTo(6).And.Contain("/new-task");
        completion.HandleKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false), editor).Should().BeTrue();
        editor.Text.Should().Be("/new-task ");
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
        editor.Insert(":cl");
        var completion = new ComposerCompletion();
        completion.Update(editor, []);
        completion.Options.Should().Contain(":clear").And.Contain(":clear-history");
        completion.Options.Should().HaveCountLessThanOrEqualTo(6);
        completion.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false), editor).Should().BeTrue();
        completion.Options.Should().BeEmpty();
        editor.Text.Should().Be(":cl");
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
    public void Composer_rendering_preserves_literal_text_and_active_mode(int width, AgentMode mode)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No });
        console.Profile.Width = width;
        console.Profile.Height = 24;
        var input = new SystemConsoleInput();
        _ = new SpectreUserInterface(console, input, mode: mode, model: new string('m', 160));
        input.ComposerRedrawHandler!("[red]literal[/]", 14, ["@src/Test.cs"], 0);
        var output = writer.ToString();
        output.Should().Contain("[red]literal[/]").And.Contain($"[{mode}]");
        output.Split('\n').Should().OnlyContain(line => line.TrimEnd('\r').GetCellWidth() <= width);
    }

    [Fact]
    public async Task Approval_uses_response_channel_not_composer()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Interactive = InteractionSupport.Yes });
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
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
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
        frames.Split("\u001b[?1049h", StringSplitOptions.None).Should().HaveCount(2);
        frames.Should().NotContain("\u001b[?1049l");
        PlainOutput(writer).Should().Contain("[Agent]").And.Contain("$0.01");

        writer.GetStringBuilder().Clear();
        ui.RestoreTerminal();
        writer.ToString().Should().Be("\u001b[?2026l\u001b[r\u001b[0m\u001b[?25h\u001b[?1002l\u001b[?1006l\u001b[?1049l");
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
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
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
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No, Interactive = InteractionSupport.No,
        });
        using var ui = new SpectreUserInterface(console, new ChatInput());
        await ui.ReadInputAsync();
        ui.RestoreTerminal();
        writer.ToString().Should().NotContain("\u001b[?1049");
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
        chat.Should().Contain("Approval required").And.Contain("hello").And.Contain("\u001b[1;1H");
    }

    [Fact]
    public void Settings_palette_filters_navigates_and_selects_configuration()
    {
        var palette = new SettingsPalette();
        foreach (var character in "settings")
        {
            palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.S, false, false, false));
        }
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Closed.Should().BeTrue();
        palette.Command.Should().Be(":preferences");
    }

    [Fact]
    public void Provider_palette_shows_only_names_without_empty_description_rows()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes,
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

    [Fact]
    public void Settings_palette_model_entry_and_escape_are_local()
    {
        var palette = new SettingsPalette();
        palette.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.EditingModel.Should().BeTrue();
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Closed.Should().BeFalse();
        foreach (var character in "codai-provider/codai-model")
        {
            palette.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.M, false, false, false));
        }
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be(":model agent codai-provider/codai-model");
        var cancelled = new SettingsPalette();
        cancelled.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        cancelled.Closed.Should().BeTrue();
        cancelled.Command.Should().BeNull();
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
        writer.ToString().Should().Contain("Codai / Settings").And.Contain("Search:");
        input.OverlayKeyHandler!(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false)).Should().BeTrue();
        writer.GetStringBuilder().Clear();
        input.ComposerRedrawHandler!(draft, draft.Length, [], 0);
        writer.ToString().Should().Contain(draft).And.NotContain("Codai / Settings");
        input.PaletteCommandHandler!().Should().BeNull();
        input.OverlayKeyHandler!(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)).Should().BeFalse();
    }

    [Fact]
    public void Settings_modal_dispatches_existing_configuration_command_once()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        var input = new SystemConsoleInput();
        using var ui = new SpectreUserInterface(console, input);
        input.OverlayKeyHandler!(new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false));
        input.OverlayKeyHandler!(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        input.PaletteCommandHandler!().Should().Be(":preferences");
        input.PaletteCommandHandler!().Should().BeNull();
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
    [InlineData("Approval policy", true, AgentMode.Agent)]
    [InlineData("Plan mode", false, AgentMode.Plan)]
    public void Settings_actions_update_shared_session_state(string search, bool autoApprove, AgentMode expectedMode)
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
        approval.AutoApprove.Should().Be(autoApprove);
        mode.Mode.Should().Be(expectedMode);
        input.PaletteCommandHandler!().Should().BeNull();
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
        string.Join('\n', transcript.GetLines(70)).Should().Contain("(done)");
        transcript.ToolResult("read_file", string.Join('\n', Enumerable.Range(1, 12).Select(number => $"source row {number}")));
        string.Join('\n', transcript.GetLines(70)).Should().NotContain("source row").And.NotContain("more lines");
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
        writer.ToString().Should().Contain("sample.cs").And.NotContain("private source payload").And.NotContain("verbose source summary");
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
    public void Transcript_scrollbar_avoids_terminal_last_column_and_repaints_after_resize()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 80;
        var transcript = new ChatTranscript();
        transcript.Add(string.Join('\n', Enumerable.Range(1, 30).Select(number => $"row {number}")));
        transcript.Draw(console, 5);
        writer.ToString().Should().Contain(";79H").And.NotContain(";80H");
        writer.GetStringBuilder().Clear();
        console.Profile.Width = 40;
        transcript.Draw(console, 5);
        writer.ToString().Should().Contain(";39H").And.Contain("row 30");
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
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
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
        var palette = new SettingsPalette(question, [new("1. " + choice, string.Empty, "chosen")], question: true);
        palette.Draw(console);
        PlainOutput(writer).Should().Contain("end?").And.Contain("option-end");
        palette.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        palette.Command.Should().Be("chosen");
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
