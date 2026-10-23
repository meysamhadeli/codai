namespace Codai.Analysis.Abstractions;

/// <summary>
/// Selects the signature extractor responsible for a given file.
/// </summary>
public interface ISignatureExtractorRegistry
{
    /// <summary>
    /// Extracts signatures for <paramref name="filePath"/>; returns an empty list when
    /// no extractor supports the file.
    /// </summary>
    IReadOnlyList<string> Extract(string filePath, string source);

    /// <summary>True when a dedicated extractor exists for the file.</summary>
    bool Supports(string filePath);
}
