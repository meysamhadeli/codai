using System.Text;
using Spectre.Console;

namespace Codai.Cli.Ui;

internal enum TerminalIcon
{
    Read, Write, Edit, Search, Folder, Terminal, Web, Skill, Integration, Agent,
    Check, Warning, Error, Settings, History, Diff, Download, Plugin, Model, Lock,
    Info, Help, Theme, Exit, Compact,
}

internal static class TerminalIcons
{
    internal static string White(string markup)
    {
        var result = new StringBuilder(markup.Length);
        foreach (var character in markup)
        {
            if (character is >= '\ue000' and <= '\uf8ff')
                result.Append("[white]").Append(character).Append("[/]");
            else
                result.Append(character);
        }
        return result.ToString();
    }

    internal static Markup Render(string text, string style) =>
        new($"[{style}]{White(Markup.Escape(text))}[/]");

    internal static bool NerdFontEnabled =>
        !string.Equals(Environment.GetEnvironmentVariable("CODAI_ICONS"), "unicode", StringComparison.OrdinalIgnoreCase);

    internal static string For(TerminalIcon icon, bool unicode, bool? nerdFont = null)
    {
        if (!unicode)
        {
            return icon switch
            {
                TerminalIcon.Check => "+",
                TerminalIcon.Warning or TerminalIcon.Error => "!",
                _ => "*",
            };
        }
        if (!(nerdFont ?? NerdFontEnabled))
        {
            return icon switch
            {
                TerminalIcon.Check => "\u2714",
                TerminalIcon.Warning => "\u26a0",
                TerminalIcon.Error => "\u00d7",
                _ => "\u25cf",
            };
        }
        return icon switch
        {
            TerminalIcon.Read => "\uf0f6",
            TerminalIcon.Write => "\uf0c7",
            TerminalIcon.Edit => "\uf044",
            TerminalIcon.Search => "\uf002",
            TerminalIcon.Folder => "\uf07c",
            TerminalIcon.Terminal => "\uf120",
            TerminalIcon.Web => "\uf0ac",
            TerminalIcon.Skill => "\uf0e7",
            TerminalIcon.Integration or TerminalIcon.Plugin => "\uf1e6",
            TerminalIcon.Agent => "\uf007",
            TerminalIcon.Check => "\uf00c",
            TerminalIcon.Warning => "\uf071",
            TerminalIcon.Error => "\uf00d",
            TerminalIcon.Settings => "\uf013",
            TerminalIcon.History => "\uf1da",
            TerminalIcon.Diff => "\uf0ec",
            TerminalIcon.Download => "\uf019",
            TerminalIcon.Model => "\uf0e8",
            TerminalIcon.Lock => "\uf023",
            TerminalIcon.Info => "\uf05a",
            TerminalIcon.Help => "\uf059",
            TerminalIcon.Theme => "\uf1fc",
            TerminalIcon.Exit => "\uf08b",
            TerminalIcon.Compact => "\uf066",
            _ => "\uf016",
        };
    }

    internal static TerminalIcon Tool(string name) => name switch
    {
        "read_file" => TerminalIcon.Read,
        "write_file" => TerminalIcon.Write,
        "edit_file" or "apply_patch" => TerminalIcon.Edit,
        "grep" or "glob" or "tool_search" => TerminalIcon.Search,
        "list_files" => TerminalIcon.Folder,
        "run_command" => TerminalIcon.Terminal,
        "fetch_web" or "web_search" => TerminalIcon.Web,
        "read_skill" => TerminalIcon.Skill,
        _ when name.StartsWith("mcp", StringComparison.OrdinalIgnoreCase) => TerminalIcon.Integration,
        _ when name.Contains("agent", StringComparison.OrdinalIgnoreCase) => TerminalIcon.Agent,
        _ => TerminalIcon.Settings,
    };

    internal static string Prefix(TerminalIcon icon, bool unicode) =>
        unicode && NerdFontEnabled ? For(icon, unicode) + " " : string.Empty;

    internal static TerminalIcon Command(string command) => command.Split(' ', 2)[0] switch
    {
        "/session" or "/sessions" or "/new" => TerminalIcon.History,
        "/models" or "/model" => TerminalIcon.Model,
        "/agents" or "/agent" => TerminalIcon.Agent,
        "/compact" or "/autocompact" => TerminalIcon.Compact,
        "/skills" => TerminalIcon.Skill,
        "/diff" => TerminalIcon.Diff,
        "/themes" or "/theme" => TerminalIcon.Theme,
        "/plugins" => TerminalIcon.Plugin,
        "/mcps" => TerminalIcon.Integration,
        "/auth" => TerminalIcon.Lock,
        "/update" => TerminalIcon.Download,
        "/help" => TerminalIcon.Help,
        ":docs" => TerminalIcon.Web,
        "/exit" => TerminalIcon.Exit,
        _ => TerminalIcon.Settings,
    };
}