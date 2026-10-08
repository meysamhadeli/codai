namespace Codai.Core.Assets;

/// <summary>
/// Access to the text assets embedded in <c>Codai.Core</c>.
/// </summary>
public static class EmbeddedResources
{
    private const string FullContextTemplateResourceName =
        "Codai.Core.Resources.SummarizeFullContextPrompt.tmpl";

    private static readonly Lazy<string> FullContextTemplateValue = new(
        () => ReadResource(FullContextTemplateResourceName),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The general "how to answer" template injected into every system prompt.</summary>
    public static string FullContextTemplate => FullContextTemplateValue.Value;

    private static string ReadResource(string name)
    {
        var assembly = typeof(EmbeddedResources).Assembly;
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{name}' was not found. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
