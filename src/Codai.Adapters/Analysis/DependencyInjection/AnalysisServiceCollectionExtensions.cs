using Codai.Analysis;
using Codai.Analysis.Abstractions;
using Codai.Analysis.Extractors;
using Codai.Core.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition helpers for the project analysis services.
/// </summary>
public static class AnalysisServiceCollectionExtensions
{
    /// <summary>Registers the project scanner and signature extractors.</summary>
    public static IServiceCollection AddCodaiAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISignatureExtractorRegistry, SignatureExtractorRegistry>();
        services.AddSingleton<IProjectScanner, FileSystemProjectScanner>();

        return services;
    }
}
