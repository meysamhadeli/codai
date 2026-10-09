namespace Codai.Core.IO;

/// <summary>
/// The locations inside a project that Codai owns.
/// </summary>
/// <remarks>
/// Everything under <see cref="Folder"/> holds configuration for the assistant rather
/// than project source, so the whole folder is excluded from the project scan and can
/// never be read back as context.
/// </remarks>
public static class CodaiPaths
{
    /// <summary>Folder, relative to the project root, that holds everything Codai owns.</summary>
    public const string Folder = ".codai";

    /// <summary>Where user-defined <c>:commands</c> live.</summary>
    public static string CommandsDirectory { get; } = $"{Folder}/commands";

    /// <summary>Where skills live, one folder per skill.</summary>
    public static string SkillsDirectory { get; } = $"{Folder}/skills";

    /// <summary>Where the stored conversation lives.</summary>
    public static string SessionsDirectory { get; } = $"{Folder}/sessions";

    /// <summary>File name of a skill's instructions.</summary>
    public const string SkillFileName = "SKILL.md";

    /// <summary>
    /// Directory for a relative path expressed with forward slashes, using the platform
    /// separator.
    /// </summary>
    public static string ToPlatformPath(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);
}
