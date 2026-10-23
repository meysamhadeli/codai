using Spectre.Console;

namespace Codai.Cli.Ui;

/// <summary>
/// A modal drawn over the composer: it takes every key while it is open and reports how it ended.
/// </summary>
/// <remarks>
/// The settings palette and the approval prompt differ only in what they ask and how they look, so
/// the render loop treats both the same way: draw it over the composer, hand it the keys, and read
/// the outcome when it closes.
/// </remarks>
internal interface IOverlay
{
    /// <summary>Gets whether the overlay has finished and wants its result read.</summary>
    bool Closed { get; }

    /// <summary>Gets the outcome, meaningful once <see cref="Closed"/> is true.</summary>
    string? Command { get; }

    /// <summary>Handles one keystroke.</summary>
    void HandleKey(ConsoleKeyInfo key);

    /// <summary>Draws the overlay.</summary>
    /// <param name="console">Console to draw to.</param>
    /// <param name="theme">Palette name used for the selected row.</param>
    /// <param name="minimumTop">First row the overlay may use.</param>
    void Draw(IAnsiConsole console, string theme = "graphite", int minimumTop = 1);
}
