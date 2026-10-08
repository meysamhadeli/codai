namespace Codai.Configuration;

/// <summary>
/// Defaults matching the original Go <c>DefaultConfig</c>.
/// </summary>
public static class CodaiDefaults
{
    public const string Version = "2.0.0";
    public const string Theme = "dracula";
    public const string Provider = "openai";
    public const string Model = "gpt-4o";

    /// <summary>Default base URL used by OpenAI.</summary>
    public const string OpenAiBaseUrl = "https://api.openai.com/v1";

    public const string FileSystemIgnoreName = ".codai-gitignore";
    public const string DefaultIgnoreFileName = ".codai-gitignore";

    /// <summary>Files larger than this are excluded from the project context.</summary>
    public const long MaxFileSizeBytes = 100 * 1024;
}
