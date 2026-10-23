using System.Net.Http.Headers;
using System.Text.Json;
using Codai.Configuration;

namespace Codai.Providers;

public sealed class ProviderModelDiscovery(HttpClient http, string? apiVersion = null)
{
    public async Task<IReadOnlyList<string>> ListAsync(ProviderDescriptor provider, string endpoint, string? credential, CancellationToken cancellationToken = default)
    {
        if (provider.WireFormat == ProviderWireFormat.AzureOpenAi)
        {
            return [];
        }
        var address = new UriBuilder(endpoint);
        var path = address.Path.TrimEnd('/');
        var local = provider.Name == ProviderCatalog.Ollama;
        if (local)
        {
            if (path.EndsWith("/api", StringComparison.Ordinal) || path.EndsWith("/v1", StringComparison.Ordinal))
            {
                path = path[..path.LastIndexOf('/')];
            }
            address.Path = path + "/api/tags";
        }
        else
        {
            address.Path = path + "/models";
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, address.Uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (provider.WireFormat == ProviderWireFormat.Anthropic)
        {
            request.Headers.TryAddWithoutValidation("x-api-key", credential);
            request.Headers.TryAddWithoutValidation("anthropic-version", apiVersion ?? "2023-06-01");
        }
        else if (provider.WireFormat == ProviderWireFormat.Gemini)
        {
            request.Headers.TryAddWithoutValidation("x-goog-api-key", credential);
        }
        else if (!string.IsNullOrWhiteSpace(credential))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken).ConfigureAwait(false);
        var collection = local || provider.WireFormat == ProviderWireFormat.Gemini ? "models" : "data";
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(collection, out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        List<string> models = [];
        foreach (var entry in entries.EnumerateArray().Take(2000))
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            var key = local || provider.WireFormat == ProviderWireFormat.Gemini ? "name" : "id";
            if (!entry.TryGetProperty(key, out var identifier) || identifier.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            var model = identifier.GetString()!;
            if (provider.WireFormat == ProviderWireFormat.Gemini)
            {
                if (entry.TryGetProperty("supportedGenerationMethods", out var methods) && methods.ValueKind == JsonValueKind.Array &&
                    !methods.EnumerateArray().Any(method => method.ValueKind == JsonValueKind.String && method.GetString() == "generateContent"))
                {
                    continue;
                }
                if (model.StartsWith("models/", StringComparison.Ordinal))
                {
                    model = model[7..];
                }
            }
            if (!string.IsNullOrWhiteSpace(model) && model.Length <= 512 && !model.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
            {
                models.Add(model);
            }
        }
        return models.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}
