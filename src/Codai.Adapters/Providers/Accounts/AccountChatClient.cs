using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using Codai.Core.Abstractions;
using Codai.Providers.Internal;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Responses;

namespace Codai.Providers.Accounts;

internal sealed class AccountChatClient : HttpChatClientBase
{
    private readonly ResolvedProviderOptions _options;
    private readonly AccountCredentialStore _store;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public AccountChatClient(ResolvedProviderOptions options, AccountCredentialStore? store = null, HttpClient? http = null)
        : base(options.Provider, options.Model, new Uri(AccountAuthorizationClient.Resource))
    {
        options.EnsureUsable();
        _options = options;
        _store = store ?? new AccountCredentialStore();
        _ownsHttp = http is null;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromMinutes(5), MaxResponseContentBufferSize = 2 * 1024 * 1024,
        };
    }

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        AccountSession session;
        using (await _store.LockAsync(_options.Account!, cancellationToken).ConfigureAwait(false))
        {
            session = await _store.ReadAsync(_options.Account!, cancellationToken).ConfigureAwait(false)
                ?? throw new ProviderConfigurationException("Account credentials are missing. Run 'codai auth' to sign in.");
            if (string.IsNullOrWhiteSpace(session.RefreshToken))
            {
                throw new ProviderConfigurationException("Account is signed out. Run 'codai auth' to sign in.");
            }
            if (session.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
            {
                session = await new AccountAuthorizationClient(_http).RefreshAsync(session, cancellationToken).ConfigureAwait(false);
                await _store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
            }
        }
    #pragma warning disable OPENAI001
        using var client = new OpenAIClient(new ApiKeyCredential(session.AccessToken), new OpenAIClientOptions
        {
            Endpoint = new Uri(AccountAuthorizationClient.Resource), Transport = new HttpClientPipelineTransport(_http),
            RetryPolicy = new ClientRetryPolicy(0),
        }).GetResponsesClient().AsIChatClient(_options.Model);
        var request = options?.Clone() ?? new ChatOptions();
        request.RawRepresentationFactory = _ => new CreateResponseOptions { StoredOutputEnabled = false };
    #pragma warning restore OPENAI001
        var completed = false;
        await using var updates = client.GetStreamingResponseAsync(messages, request, cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (await NextAsync(updates).ConfigureAwait(false))
        {
            var update = updates.Current;
            if (update.FinishReason is { } finish)
            {
                if (finish != ChatFinishReason.Stop && finish != ChatFinishReason.ToolCalls)
                {
                    throw new ChatProviderException("Account response was incomplete. Retry with a smaller request.");
                }
                completed = true;
            }
            yield return update;
        }
        if (!completed)
        {
            throw new ChatProviderException("Account response ended before completion. Retry the request.");
        }
    }

    private static async Task<bool> NextAsync(IAsyncEnumerator<ChatResponseUpdate> updates)
    {
        try
        {
            return await updates.MoveNextAsync().ConfigureAwait(false);
        }
        catch (ClientResultException)
        {
            throw new ChatProviderException("Account request failed. Review plan usage in account settings or run 'codai auth' to sign in again.");
        }
    }

    public override void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
        base.Dispose();
    }
}