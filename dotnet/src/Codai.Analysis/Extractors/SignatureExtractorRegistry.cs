using Codai.Core.Abstractions;

namespace Codai.Analysis.Extractors;

/// <summary>
/// Selects the extractor responsible for a given file and returns its signatures.
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

/// <summary>Default registry: Roslyn for C#, regex rules for the other languages.</summary>
public sealed class SignatureExtractorRegistry : ISignatureExtractorRegistry
{
    private readonly IReadOnlyList<ISignatureExtractor> _extractors;

    public SignatureExtractorRegistry()
        : this([new CSharpSignatureExtractor(), .. SignatureRules.All])
    {
    }

    public SignatureExtractorRegistry(IReadOnlyList<ISignatureExtractor> extractors)
    {
        ArgumentNullException.ThrowIfNull(extractors);
        _extractors = extractors;
    }

    public bool Supports(string filePath) =>
        _extractors.Any(extractor => extractor.Supports(filePath));

    public IReadOnlyList<string> Extract(string filePath, string source)
    {
        foreach (var extractor in _extractors)
        {
            if (extractor.Supports(filePath))
            {
                return extractor.Extract(filePath, source);
            }
        }

        return [];
    }
}
