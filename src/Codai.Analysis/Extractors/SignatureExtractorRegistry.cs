using Codai.Analysis.Abstractions;
using Codai.Core.Abstractions;

namespace Codai.Analysis.Extractors;

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
