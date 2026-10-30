namespace Codai.Core;

/// <summary>
/// A named terminal appearance: the accent colour used for activity text and the
/// highlighted selection background used by dialogs and approval panels.
/// </summary>
/// <param name="Id">Stable configuration identifier, e.g. <c>graphite</c>.</param>
/// <param name="Title">Display name shown in the theme picker.</param>
/// <param name="Description">Short description shown under the picker title.</param>
/// <param name="Accent">Spectre colour name for foreground accents and activity.</param>
/// <param name="Highlight">Spectre style for the selected row, e.g. <c>black on skyblue1</c>.</param>
public sealed record TerminalTheme(
    string Id,
    string Title,
    string Description,
    string Accent,
    string Highlight);

/// <summary>
/// The palette of supported terminal themes.
/// </summary>
public static class TerminalThemes
{
    /// <summary>Cool sky blue.</summary>
    public static TerminalTheme Graphite { get; } = new(
        "graphite", "Graphite", "Cool sky blue selection", "deepskyblue1", "black on skyblue1");

    /// <summary>Deep ocean blue.</summary>
    public static TerminalTheme Ocean { get; } = new(
        "ocean", "Ocean", "Deep ocean blue selection", "dodgerblue1", "black on lightskyblue1");

    /// <summary>Bright cyan blue.</summary>
    public static TerminalTheme Azure { get; } = new(
        "azure", "Azure", "Bright cyan blue selection", "cyan1", "black on aquamarine1");

    /// <summary>Soft purple.</summary>
    public static TerminalTheme Violet { get; } = new(
        "violet", "Violet", "Soft purple selection", "purple", "black on plum1");

    /// <summary>Vivid pink.</summary>
    public static TerminalTheme Magenta { get; } = new(
        "magenta", "Magenta", "Vivid pink selection", "magenta1", "black on pink1");

    /// <summary>Soft green.</summary>
    public static TerminalTheme Forest { get; } = new(
        "forest", "Forest", "Soft green selection", "green", "black on green");

    /// <summary>Fresh mint teal.</summary>
    public static TerminalTheme Mint { get; } = new(
        "mint", "Mint", "Fresh mint selection", "teal", "black on palegreen1");

    /// <summary>Warm gold.</summary>
    public static TerminalTheme Ember { get; } = new(
        "ember", "Ember", "Warm gold selection", "yellow", "black on yellow");

    /// <summary>Warm orange.</summary>
    public static TerminalTheme Sunset { get; } = new(
        "sunset", "Sunset", "Warm orange selection", "#D97757", "black on #D97757");

    /// <summary>Warm coral.</summary>
    public static TerminalTheme Coral { get; } = new(
        "coral", "Coral", "Warm coral selection", "red1", "black on lightcoral");

    /// <summary>Default theme used when none is configured or the value is unknown.</summary>
    public static TerminalTheme Default { get; } = Graphite;

    /// <summary>All selectable themes in picker order.</summary>
    public static IReadOnlyList<TerminalTheme> All { get; } =
    [
        Graphite,
        Ocean,
        Azure,
        Violet,
        Magenta,
        Forest,
        Mint,
        Ember,
        Sunset,
        Coral,
    ];

    /// <summary>Resolves a configuration value to a theme, falling back to the default.</summary>
    public static TerminalTheme Find(string? name) =>
        All.FirstOrDefault(theme => string.Equals(theme.Id, name, StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>Whether a configuration value names a selectable theme.</summary>
    public static bool IsKnown(string? name) =>
        All.Any(theme => string.Equals(theme.Id, name, StringComparison.OrdinalIgnoreCase));
}
