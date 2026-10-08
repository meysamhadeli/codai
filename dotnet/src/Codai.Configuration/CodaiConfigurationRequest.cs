namespace Codai.Configuration;

/// <summary>
/// Inputs required to resolve configuration.
/// </summary>
/// <param name="WorkingDirectory">Directory scanned for <c>codai-config.*</c>.</param>
/// <param name="ConfigFilePath">Explicit configuration file, if any.</param>
/// <param name="CommandLineArgs">Raw process arguments (flags only are used).</param>
public sealed record CodaiConfigurationRequest(
    string WorkingDirectory,
    string? ConfigFilePath = null,
    IReadOnlyList<string>? CommandLineArgs = null)
{
    public static CodaiConfigurationRequest For(string workingDirectory) => new(workingDirectory);
}
