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
    [InlineData(ProviderCatalog.DeepSeek, """{"data":[{"id":"codai-chat"}]}""", "/v1/models", "Authorization", "Bearer codai-test-credential")]
    [InlineData(ProviderCatalog.OpenRouter, """{"data":[{"id":"codai-chat"}]}""", "/v1/models", "Authorization", "Bearer codai-test-credential")]
    [InlineData(ProviderCatalog.Qwen, """{"data":[{"id":"codai-chat"}]}""", "/v1/models", "Authorization", "Bearer codai-test-credential")]
    [InlineData(ProviderCatalog.Mistral, """{"data":[{"id":"codai-chat"}]}""", "/v1/models", "Authorization", "Bearer codai-test-credential")]
    [InlineData(ProviderCatalog.Grok, """{"data":[{"id":"codai-chat"}]}""", "/v1/models", "Authorization", "Bearer codai-test-credential")]
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

    [Theory]
    [InlineData(ProviderCatalog.Anthropic, """{"data":[{"id":"second"}],"has_more":true,"last_id":"next/+&"}""", """{"data":[{"id":"first"},{"id":"second"}],"has_more":false}""", "after_id")]
    [InlineData(ProviderCatalog.Gemini, """{"models":[{"name":"models/second"}],"nextPageToken":"next/+&"}""", """{"models":[{"name":"models/first"},{"name":"models/second"}]}""", "pageToken")]
    public async Task Lists_all_pages_with_encoded_same_endpoint_cursors(string provider, string first, string second, string parameter)
    {
        using var handler = new PagedHandler(first, second);
        using var http = new HttpClient(handler);

        var models = await new ProviderModelDiscovery(http).ListAsync(ProviderCatalog.Get(provider), "https://models.example.test/v1", "codai-test-credential");

        models.Should().Equal("first", "second");
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].AbsolutePath.Should().Be("/v1/models");
        handler.Requests[1].Query.Should().Be("?" + parameter + "=next%2F%2B%26");
        handler.Credentials.Should().OnlyContain(value => value == "codai-test-credential");
    }

    [Fact]
    public async Task Repeated_pagination_cursor_fails_instead_of_looping_or_claiming_complete_catalog()
    {
        using var handler = new PagedHandler("""{"models":[{"name":"models/chat"}],"nextPageToken":"same"}""");
        using var http = new HttpClient(handler);
        var action = () => new ProviderModelDiscovery(http).ListAsync(ProviderCatalog.Get(ProviderCatalog.Gemini), "https://models.example.test/v1", null);

        await action.Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(ProviderCatalog.Mistral, """{"data":[{"id":"chat","capabilities":{"completion_chat":true}},{"id":"embed","capabilities":{"completion_chat":false}}]}""")]
    [InlineData(ProviderCatalog.Anthropic, """{"data":[{"id":"chat","lifecycle":"active"},{"id":"old","lifecycle":"retired"}]}""")]
    [InlineData(ProviderCatalog.OpenRouter, """{"data":[{"id":"chat","architecture":{"output_modalities":["text"]}},{"id":"image","architecture":{"output_modalities":["image"]}},{"id":"chat:batch"}]}""")]
    public async Task Excludes_explicitly_retired_or_non_chat_models(string provider, string response)
    {
        using var http = StubHttpMessageHandler.CreateClient(response);
        (await new ProviderModelDiscovery(http).ListAsync(ProviderCatalog.Get(provider), "https://models.example.test/v1", null)).Should().Equal("chat");
    }

    [Theory]
    [InlineData("""{"data":[{"id":"chat"}],"has_more":true}""")]
    [InlineData("""{"data":[{"id":"chat"}],"has_more":true,"last_id":""}""")]
    public async Task Missing_continuation_cursor_does_not_claim_complete_catalog(string response)
    {
        using var http = StubHttpMessageHandler.CreateClient(response);
        var action = () => new ProviderModelDiscovery(http).ListAsync(ProviderCatalog.Get(ProviderCatalog.Anthropic), "https://models.example.test/v1", null);
        await action.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Catalogs_are_not_silently_truncated_at_two_thousand_models()
    {
        var response = System.Text.Json.JsonSerializer.Serialize(new { data = Enumerable.Range(0, 2001).Select(index => new { id = $"model-{index}" }) });
        using var http = StubHttpMessageHandler.CreateClient(response);
        (await new ProviderModelDiscovery(http).ListAsync(ProviderCatalog.Get(ProviderCatalog.OpenAi), "https://models.example.test/v1", null)).Should().HaveCount(2001);
    }

    [Fact]
    public async Task Catalog_entry_limit_fails_instead_of_silently_truncating()
    {
        var response = System.Text.Json.JsonSerializer.Serialize(new { data = Enumerable.Range(0, 10001).Select(index => new { id = $"model-{index}" }) });
        using var http = StubHttpMessageHandler.CreateClient(response);
        var action = () => new ProviderModelDiscovery(http).ListAsync(ProviderCatalog.Get(ProviderCatalog.OpenAi), "https://models.example.test/v1", null);
        await action.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Unending_distinct_cursors_are_bounded()
    {
        var responses = Enumerable.Range(0, 100).Select(index => System.Text.Json.JsonSerializer.Serialize(new
        {
            models = Array.Empty<string>(),
            nextPageToken = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        })).ToArray();
        using var handler = new PagedHandler(responses);
        using var http = new HttpClient(handler);
        var action = () => new ProviderModelDiscovery(http).ListAsync(ProviderCatalog.Get(ProviderCatalog.Gemini), "https://models.example.test/v1", null);
        await action.Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().HaveCount(100);
    }

    private sealed class PagedHandler(params string[] responses) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<string?> Credentials { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            Credentials.Add(request.Headers.TryGetValues("x-api-key", out var anthropic) ? anthropic.Single() :
                request.Headers.TryGetValues("x-goog-api-key", out var gemini) ? gemini.Single() : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses[Math.Min(Requests.Count - 1, responses.Length - 1)]),
            });
        }
    }
}
