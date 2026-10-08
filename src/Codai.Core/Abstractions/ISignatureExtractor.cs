namespace Codai.Core.Abstractions;

/// <summary>
/// Extracts a structural signature (namespaces, types, members) from a source file.
/// </summary>
public interface ISignatureExtractor
{
    /// <summary>True when this extractor understands the given file.</summary>
    bool Supports(string filePath);

    /// <summary>
    /// Returns one tagged entry per discovered symbol, e.g. <c>class: Order</c>.
    /// </summary>
    IReadOnlyList<string> Extract(string filePath, string source);
}
