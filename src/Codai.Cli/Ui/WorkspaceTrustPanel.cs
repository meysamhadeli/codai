using Spectre.Console;

namespace Codai.Cli.Ui;

internal sealed class WorkspaceTrustPanel(string directory) : IInlineDecision
{
    private readonly DecisionPanel _panel = new("Workspace access",
        directory + "\n\nOnly continue if you trust this directory and its contents.\n" +
        "Codai can read project files and instructions, load integrations, and propose edits or commands.\n" +
        "Access lasts for this run only. Tool approvals remain separate.",
        [new("Yes", string.Empty, "trust"), new("No", string.Empty, "leave")]);

    public bool Closed => _panel.Closed;
    public string? Command => _panel.Command ?? (_panel.Closed ? "leave" : null);

    public void HandleKey(ConsoleKeyInfo key) => _panel.HandleKey(key);

    public int MeasureHeight(int width, int maximumHeight) => _panel.MeasureHeight(width, maximumHeight);

    public void Draw(IAnsiConsole console, string theme = "graphite", int minimumTop = 1) => _panel.Draw(console, theme, minimumTop);
}
