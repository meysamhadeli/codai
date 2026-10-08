namespace Codai.Configuration;

/// <summary>
/// Result of parsing the process command line.
/// </summary>
/// <param name="ConfigPath">Value of <c>--config</c>/<c>-c</c>, when supplied.</param>
/// <param name="ShowVersion">True when <c>--version</c>/<c>-v</c> was supplied.</param>
/// <param name="Overrides">Flag values keyed by configuration key.</param>
/// <param name="Errors">Problems detected while parsing.</param>
public sealed record CommandLineParseResult(
    string? ConfigPath,
    bool ShowVersion,
    IReadOnlyDictionary<string, string?> Overrides,
    IReadOnlyList<string> Errors)
{
    public static CommandLineParseResult Empty { get; } =
        new(null, false, new Dictionary<string, string?>(), []);
}
