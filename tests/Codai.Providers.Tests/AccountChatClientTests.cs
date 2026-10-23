using System.Text.Json;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Providers.Accounts;
using Codai.Providers.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Tests;

public class AccountChatClientTests
{
    [Fact]
    public async Task Concurrent_requests_refresh_once_and_atomically_retain_rotated_credentials()
    {
        var root = Directory.CreateTempSubdirectory("codai-plan-refresh-");
        try
        {
            var store = new AccountCredentialStore(root.FullName);
            var session = new AccountSession
            {
                ClientId = "codai-client", Subject = "codai-user", AccessToken = "codai-expired",
                RefreshToken = "codai-old-renewal", IdToken = "codai-identity", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Scopes = ["resource.invoke", "chatgpt.tokens.use.direct"],
            };
            await store.SaveAsync(session);
            using var handler = new RenewalHandler();
            using var http = new HttpClient(handler);
            var options = ResolvedProviderOptions.From(new AiProviderOptions
            {
                Provider = ProviderCatalog.OpenAi, Account = session.Id, Model = "codai-plan-model",
            });
            using var first = new AccountChatClient(options, store, http);
            using var second = new AccountChatClient(options, new AccountCredentialStore(root.FullName), http);
            await Task.WhenAll(first.GetResponseAsync([new ChatMessage(ChatRole.User, "First")]),
                second.GetResponseAsync([new ChatMessage(ChatRole.User, "Second")]));
            handler.Renewals.Should().Be(1);
            var saved = await store.ReadAsync(session.Id);
            saved!.AccessToken.Should().Be("codai-new-access");
            saved.RefreshToken.Should().Be("codai-new-renewal");
            saved.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
            saved.IdToken.Should().Be(session.IdToken);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private sealed class RenewalHandler : HttpMessageHandler
    {
        public int Renewals;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/oauth/token", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref Renewals);
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                body.Should().Contain("grant_type=refresh_token").And.Contain("client_id=codai-client").And.Contain("refresh_token=codai-old-renewal");
                request.RequestUri.Query.Should().BeEmpty();
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"codai-new-access","refresh_token":"codai-new-renewal","token_type":"Bearer","expires_in":3600}"""),
                };
            }
            request.Headers.Authorization!.Parameter.Should().Be("codai-new-access");
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("event: response.completed\ndata: {\"type\":\"response.completed\",\"response\":{\"id\":\"resp_codai\",\"object\":\"response\",\"status\":\"completed\",\"created_at\":1760000000,\"model\":\"codai-plan-model\",\"output\":[]}}\n\n", System.Text.Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("interrupted")]
    [InlineData("incomplete")]
    [InlineData("failed")]
    public async Task Account_requests_stream_without_storage_and_require_completed_response(string status)
    {
        var root = Directory.CreateTempSubdirectory("codai-plan-runtime-");
        try
        {
            var store = new AccountCredentialStore(root.FullName);
            var session = new AccountSession
            {
                ClientId = "codai-client", Subject = "codai-user", AccessToken = "codai-plan-access",
                RefreshToken = "codai-renewal", IdToken = "codai-identity", Scopes = [], ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            };
            await store.SaveAsync(session);
            var stream = "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"item_id\":\"msg_codai\",\"output_index\":0,\"content_index\":0,\"delta\":\"Hello\"}\n\n";
            if (status != "interrupted")
            {
                stream += "event: response." + status + "\ndata: " + JsonSerializer.Serialize(new
                {
                    type = "response." + status,
                    response = new
                    {
                        id = "resp_codai", @object = "response", status, created_at = 1760000000,
                        model = "codai-plan-model", output = Array.Empty<object>(),
                        usage = new { input_tokens = 12, output_tokens = 5, total_tokens = 17 },
                        incomplete_details = status == "incomplete" ? new { reason = "max_output_tokens" } : null,
                        error = status == "failed" ? new { code = "subscription_sharing_usage_limit_exceeded", message = "Plan limit reached" } : null,
                    },
                }) + "\n\n";
            }
            using var handler = new StubHttpMessageHandler(stream);
            using var http = new HttpClient(handler);
            var options = ResolvedProviderOptions.From(new AiProviderOptions
            {
                Provider = ProviderCatalog.OpenAi, Account = session.Id, Model = "codai-plan-model", Stream = false,
            });
            using var client = new AccountChatClient(options, store, http);
            var request = () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]);
            if (status == "completed")
            {
                (await request()).Text.Should().Be("Hello");
            }
            else
            {
                await request.Should().ThrowAsync<ChatProviderException>();
            }
            handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/responses");
            handler.LastRequestHeaders["Authorization"].Should().ContainSingle().Which.Should().Be("Bearer codai-plan-access");
            using var body = JsonDocument.Parse(handler.LastRequestBody!);
            body.RootElement.GetProperty("store").GetBoolean().Should().BeFalse();
            body.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
            body.RootElement.GetProperty("model").GetString().Should().Be("codai-plan-model");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("https://models.example.test/v1")]
    [InlineData("http://localhost:8080/v1")]
    public void Account_credentials_cannot_be_routed_to_custom_endpoints(string endpoint)
    {
        var options = ResolvedProviderOptions.From(new AiProviderOptions
        {
            Provider = ProviderCatalog.OpenAi, Account = new string('a', 64), BaseUrl = endpoint,
        });
        var action = () => new ChatClientFactory().CreateClient(options);
        action.Should().Throw<ProviderConfigurationException>();
    }
}