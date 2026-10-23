using System.Net;
using Codai.Configuration;
using Codai.Providers.Tests.Fakes;
using FluentAssertions;

namespace Codai.Providers.Tests;

public class ProviderModelDiscoveryTests
{
    [Theory]
    [InlineData("http://localhost:8080/v1", false)]
    [InlineData("http://127.0.0.1:8080/v1", false)]
    [InlineData("http://[::1]:8080/v1", false)]
    [InlineData("https://models.example.test/v1", true)]
    public void Custom_loopback_chat_endpoints_do_not_require_a_provider_credential(string endpoint, bool requiresCredential)
    {
        var options = ResolvedProviderOptions.From(new AiProviderOptions
        {
            Provider = ProviderCatalog.OpenAi,
            BaseUrl = endpoint,
            Model = "codai-local",
        });
        options.RequiresApiKey.Should().Be(requiresCredential);
        if (requiresCredential)
        {
            var validate = () => options.EnsureUsable();
            validate.Should().Throw<ProviderConfigurationException>();
        }
        else
        {
            using var client = new ChatClientFactory().CreateClient(options);
            client.Should().NotBeNull();
        }
    }

    [Theory]
    [InlineData(ProviderCatalog.OpenAi, """{"data":[{"id":"codai-chat"},{"id":"codai-chat"},{"id":"bad\u001bmodel"}]}""", "/v1/models", "Authorization", "Bearer codai-test-credential")]
    [InlineData(ProviderCatalog.Ollama, """{"models":[{"name":"codai-chat"}]}""", "/api/tags", "Authorization", "Bearer codai-test-credential")]
    [InlineData(ProviderCatalog.Anthropic, """{"data":[{"id":"codai-chat"}]}""", "/v1/models", "x-api-key", "codai-test-credential")]
    [InlineData(ProviderCatalog.Gemini, """{"models":[{"name":"models/codai-chat","supportedGenerationMethods":["generateContent"]},{"name":"models/codai-embedding","supportedGenerationMethods":["embedContent"]}]}""", "/v1/models", "x-goog-api-key", "codai-test-credential")]
    public async Task Lists_valid_models_with_protocol_specific_path_and_credentials(string provider, string response, string path, string header, string value)
    {
        using var handler = new StubHttpMessageHandler(response, contentType: "application/json");
        using var http = new HttpClient(handler);
        var discovery = new ProviderModelDiscovery(http);
        var models = await discovery.ListAsync(ProviderCatalog.Get(provider), "https://models.example.test/v1", "codai-test-credential");
        models.Should().Equal("codai-chat");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be(path);
        handler.LastRequestHeaders[header].Should().ContainSingle().Which.Should().Be(value);
        handler.LastRequest.RequestUri.Query.Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://localhost:11434/api")]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://localhost:11434")]
    public async Task Local_model_listing_normalizes_runtime_endpoints_without_required_credential(string endpoint)
    {
        using var handler = new StubHttpMessageHandler("""{"models":[{"name":"codai-local"}]}""");
        using var http = new HttpClient(handler);
        var models = await new ProviderModelDiscovery(http).ListAsync(ProviderCatalog.Get(ProviderCatalog.Ollama), endpoint, null);
        models.Should().Equal("codai-local");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/tags");
        handler.LastRequestHeaders.Should().NotContainKey("Authorization");
    }

    [Fact]
    public async Task Deployment_provider_leaves_model_choice_to_user_without_guessing_management_endpoint()
    {
        using var handler = new StubHttpMessageHandler("{}");
        using var http = new HttpClient(handler);
        var models = await new ProviderModelDiscovery(http).ListAsync(ProviderCatalog.Get(ProviderCatalog.AzureOpenAi), "https://models.example.test", "codai-test-credential");
        models.Should().BeEmpty();
        handler.LastRequest.Should().BeNull();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":[null,{},42]}")]
    public async Task Missing_or_malformed_model_collections_return_no_suggestions(string response)
    {
        using var http = StubHttpMessageHandler.CreateClient(response);
        (await new ProviderModelDiscovery(http).ListAsync(ProviderCatalog.Get(ProviderCatalog.OpenAi), "https://models.example.test/v1", null)).Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_authentication_is_not_misreported_as_valid_model_listing()
    {
        using var http = StubHttpMessageHandler.CreateClient("{}", HttpStatusCode.Unauthorized);
        var action = () => new ProviderModelDiscovery(http).ListAsync(ProviderCatalog.Get(ProviderCatalog.OpenAi), "https://models.example.test/v1", "codai-test-credential");
        await action.Should().ThrowAsync<HttpRequestException>();
    }
}
