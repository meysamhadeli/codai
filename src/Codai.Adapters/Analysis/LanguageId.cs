namespace Codai.Analysis;

/// <summary>
/// Language identifiers used by the signature extractors.
/// </summary>
public static class LanguageId
{
    public const string CSharp = "csharp";
    public const string Go = "go";
    public const string TypeScript = "typescript";
    public const string JavaScript = "javascript";
    public const string Python = "python";
    public const string Java = "java";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        CSharp,
        Go,
        TypeScript,
        JavaScript,
        Python,
        Java,
    };

    /// <summary>
    /// Maps a file path to a language identifier, or <c>null</c> when unsupported.
    /// </summary>
    public static string? DetectFromPath(string path)
    {
        var extension = Path.GetExtension(path);

        return extension.ToLowerInvariant() switch
        {
            ".cs" => CSharp,
            ".go" => Go,
            ".ts" or ".tsx" or ".mts" or ".cts" => TypeScript,
            ".js" or ".jsx" or ".mjs" or ".cjs" => JavaScript,
            ".py" or ".pyi" => Python,
            ".java" => Java,
            _ => null,
        };
    }
}
