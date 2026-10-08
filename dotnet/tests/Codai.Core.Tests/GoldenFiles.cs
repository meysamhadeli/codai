using System.Reflection;

namespace Codai.Core.Tests;

/// <summary>
/// Frozen copies of the original Go reference artifacts, embedded into the test
/// assembly so parity can still be asserted after the Go sources are removed.
/// </summary>
/// <remarks>
/// These are immutable baselines. If a test against them fails, the production
/// artifact drifted — do not "fix" a golden by editing it.
/// </remarks>
internal static class GoldenFiles
{
    internal const string PromptTemplateResource =
        "Codai.Core.Tests.Golden.SummarizeFullContextPrompt.tmpl";

    internal const string PricingCatalogResource =
        "Codai.Core.Tests.Golden.model_pricing.json";

    /// <summary>The Go <c>summarize_full_context_prompt.tmpl</c>, byte-for-byte.</summary>
    public static string ReadPromptTemplate() => ReadText(PromptTemplateResource);

    /// <summary>The Go <c>model_details.tmpl</c> pricing snapshot, byte-for-byte.</summary>
    public static string ReadPricingCatalog() => ReadText(PricingCatalogResource);

    /// <summary>Opens the Go pricing snapshot as a stream.</summary>
    public static Stream OpenPricingCatalog() => OpenRead(PricingCatalogResource);

    private static Stream OpenRead(string resourceName)
    {
        var assembly = typeof(GoldenFiles).Assembly;

        return assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Golden resource '{resourceName}' is missing from {assembly.GetName().Name}. " +
                $"Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");
    }

    private static string ReadText(string resourceName)
    {
        using var reader = new StreamReader(OpenRead(resourceName));
        return reader.ReadToEnd();
    }
}
