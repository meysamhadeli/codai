using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codai.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Codai.Providers.Accounts;

public sealed record AccountModel(string Id, string DisplayName);

public sealed class AccountAuthorizationAttempt
{
    public required Uri BrowserAddress { get; init; }
    public required Uri CallbackAddress { get; init; }
    internal string State { get; init; } = string.Empty;
    internal string Nonce { get; init; } = string.Empty;
    internal string Verifier { get; init; } = string.Empty;
    internal AccountSession? Previous { get; init; }
}

public sealed class AccountAuthorizationClient
{
    private const string Issuer = "https://auth.openai.com";
    private const string RegistrationClient = "dynamic_agent_client";
    private const string PlanScope = "chatgpt.tokens.use.direct";
    private readonly HttpClient _http;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _metadata;
    private readonly Func<Uri, CancellationToken, Task> _openBrowser;
    public static string Resource => ProviderCatalog.Get(ProviderCatalog.OpenAi).DefaultBaseUrl;

    public AccountAuthorizationClient(HttpClient http,
        Func<Uri, CancellationToken, Task>? openBrowser = null,
        IConfigurationManager<OpenIdConnectConfiguration>? metadata = null)
    {
        _http = http;
        _openBrowser = openBrowser ?? ((address, _) =>
        {
            Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
            return Task.CompletedTask;
        });
        _metadata = metadata ?? new ConfigurationManager<OpenIdConnectConfiguration>(
            Issuer + "/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(http) { RequireHttps = true });
    }

    public static AccountAuthorizationAttempt CreateAttempt(string hostId, Uri callback, AccountSession? previous = null)
    {
        if (callback.Scheme != "http" || callback.Host != "127.0.0.1" || callback.Query.Length != 0 || callback.Fragment.Length != 0)
        {
            throw new ArgumentException("Account callbacks require an IPv4 loopback HTTP address.", nameof(callback));
        }
        var state = RandomValue();
        var nonce = RandomValue();
        var verifier = RandomValue();
        var query = new Dictionary<string, string>
        {
            ["client_id"] = previous?.ClientId ?? RegistrationClient,
            ["ext_agent_host_id"] = hostId,
            ["response_type"] = "code",
            ["redirect_uri"] = callback.AbsoluteUri,
            ["scope"] = "openid profile email offline_access resource.invoke " + PlanScope,
            ["resource"] = Resource,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
        };
        if (previous is null)
        {
            query["agent_name_hint"] = "Codai";
        }
        else if (!string.IsNullOrEmpty(previous.IdToken))
        {
            query["id_token_hint"] = previous.IdToken;
        }
        return new AccountAuthorizationAttempt
        {
            BrowserAddress = new Uri(Issuer + "/api/accounts/authorize?" + string.Join('&',
                query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)))),
            CallbackAddress = callback, State = state, Nonce = nonce, Verifier = verifier, Previous = previous,
        };
    }

    public async Task<AccountSession?> AuthorizeAsync(string hostId, AccountSession? previous = null, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var attempt = CreateAttempt(hostId, new Uri($"http://127.0.0.1:{port}/codai/account"), previous);
        await _openBrowser(attempt.BrowserAddress, timeout.Token).ConfigureAwait(false);
        while (true)
        {
            var context = await listener.GetContextAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
            var valid = context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == attempt.CallbackAddress.AbsolutePath &&
                context.Request.QueryString.GetValues("state") is { Length: 1 } values && values[0] == attempt.State;
            context.Response.StatusCode = valid ? 200 : 400;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            var bytes = Encoding.UTF8.GetBytes(valid ? "Authorization received. Return to Codai." : "Invalid authorization callback.");
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
            context.Response.Close();
            if (!valid)
            {
                continue;
            }
            var query = context.Request.QueryString.AllKeys.Where(key => key is not null).ToDictionary(
                key => key!, key => context.Request.QueryString.GetValues(key) is { Length: 1 } items ? items[0] : null);
            return await CompleteAsync(attempt, query, timeout.Token).ConfigureAwait(false);
        }
    }

    public async Task<AccountSession?> CompleteAsync(AccountAuthorizationAttempt attempt,
        IReadOnlyDictionary<string, string?> callback, CancellationToken cancellationToken = default)
    {
        if (!callback.TryGetValue("state", out var state) || state != attempt.State)
        {
            throw new ProviderConfigurationException("Account sign-in returned an invalid state. Start sign-in again.");
        }
        if (callback.TryGetValue("error", out var error) && error is not null)
        {
            if (error == "access_denied")
            {
                return null;
            }
            throw new ProviderConfigurationException("Account sign-in was not authorized. Start sign-in again.");
        }
        callback.TryGetValue("client_id", out var issuedId);
        if (attempt.Previous is not null && issuedId is not null && issuedId != attempt.Previous.ClientId)
        {
            throw new ProviderConfigurationException("Account registration changed unexpectedly. Start sign-in again.");
        }
        var clientId = issuedId ?? attempt.Previous?.ClientId;
        if (string.IsNullOrWhiteSpace(clientId) || clientId == RegistrationClient ||
            !callback.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            throw new ProviderConfigurationException("Account registration did not complete. Start sign-in again.");
        }
        return await ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["code"] = code,
            ["code_verifier"] = attempt.Verifier, ["redirect_uri"] = attempt.CallbackAddress.AbsoluteUri, ["resource"] = Resource,
        }, clientId, attempt.Nonce, attempt.Previous, cancellationToken).ConfigureAwait(false);
    }

    public Task<AccountSession> RefreshAsync(AccountSession session, CancellationToken cancellationToken = default) =>
        ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["client_id"] = session.ClientId,
            ["refresh_token"] = session.RefreshToken, ["resource"] = Resource,
        }, session.ClientId, null, session, cancellationToken);

    public async Task<IReadOnlyList<AccountModel>> ListModelsAsync(AccountSession session, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Resource + "/models");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        List<AccountModel> models = [];
        if (document.RootElement.TryGetProperty("models", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in entries.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Take(2000))
            {
                var id = ReadString(item, "slug");
                var name = ReadString(item, "display_name");
                if (ReadString(item, "visibility") == "list" && id is { Length: > 0 and <= 512 } &&
                    !id.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
                {
                    models.Add(new AccountModel(id, name is { Length: > 0 and <= 256 } && !name.Any(char.IsControl) ? name : id));
                }
            }
        }
        return models.DistinctBy(model => model.Id, StringComparer.Ordinal).ToArray();
    }

    public async Task<bool> RevokeAsync(AccountSession session, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(session.RefreshToken))
        {
            return true;
        }
        var configuration = await _metadata.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!Uri.TryCreate(configuration.RevocationEndpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != "https" || endpoint.Host != new Uri(Issuer).Host || endpoint.UserInfo.Length != 0)
        {
            return false;
        }
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = session.RefreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = session.ClientId,
                });
                using var response = await _http.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return true;
                }
                if ((int)response.StatusCode < 500)
                {
                    return false;
                }
            }
            catch (HttpRequestException) when (attempt < 2)
            {
            }
            await Task.Delay(100 * (1 << attempt), cancellationToken).ConfigureAwait(false);
        }
        return false;
    }

    private async Task<AccountSession> ExchangeAsync(Dictionary<string, string> form, string clientId, string? nonce,
        AccountSession? previous, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(Issuer + "/api/accounts/oauth/token", content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderConfigurationException("Account authorization expired or was declined. Run 'codai auth' to sign in again.");
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;
        var scopes = ReadString(root, "scope")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? (nonce is null ? previous?.Scopes : null) ?? [];
        var access = ReadString(root, "access_token");
        var refresh = ReadString(root, "refresh_token") ?? (nonce is null ? previous?.RefreshToken : null);
        var idToken = ReadString(root, "id_token");
        if (!scopes.Contains(PlanScope, StringComparer.Ordinal) || !scopes.Contains("resource.invoke", StringComparer.Ordinal) ||
            string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh) ||
            !string.Equals(ReadString(root, "token_type"), "Bearer", StringComparison.OrdinalIgnoreCase) ||
            !root.TryGetProperty("expires_in", out var expiry) || !expiry.TryGetInt32(out var seconds) || seconds is <= 0 or > 86400)
        {
            throw new ProviderConfigurationException("Account did not grant usable plan access. Run 'codai auth' to authorize plan usage.");
        }
        var subject = previous?.Subject;
        var email = previous?.Email;
        if (idToken is not null)
        {
            var configuration = await _metadata.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            var validation = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = Issuer, ValidateAudience = true, ValidAudience = clientId,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidateIssuerSigningKey = true, IssuerSigningKeys = configuration.SigningKeys, ClockSkew = TimeSpan.FromSeconds(30),
            }).ConfigureAwait(false);
            if (!validation.IsValid || validation.SecurityToken is not JsonWebToken token ||
                string.IsNullOrWhiteSpace(token.Subject) || nonce is not null && (!token.TryGetClaim("nonce", out var claim) || claim.Value != nonce) ||
                previous is not null && token.Subject != previous.Subject)
            {
                throw new ProviderConfigurationException("Account identity could not be verified. Start sign-in again.");
            }
            subject = token.Subject;
            email = token.TryGetClaim("email", out var emailClaim) ? emailClaim.Value : null;
        }
        else if (nonce is not null)
        {
            throw new ProviderConfigurationException("Account sign-in returned no identity token. Start sign-in again.");
        }
        return new AccountSession
        {
            ClientId = clientId, Subject = subject!, Email = email, AccessToken = access,
            RefreshToken = refresh, IdToken = idToken ?? previous!.IdToken, Scopes = scopes,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds),
        };
    }

    private static string RandomValue() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    private static string? ReadString(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}