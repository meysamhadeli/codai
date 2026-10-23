using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codai.Configuration;

namespace Codai.Mcp.Client;

/// <summary>Performs OAuth authorization-code login for an MCP server.</summary>
internal static class McpOAuthAuthenticator
{
    public static async Task<string?> GetAccessTokenAsync(
        McpServerOptions server,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(server.OAuthAuthorizationEndpoint) &&
            string.IsNullOrWhiteSpace(server.OAuthTokenEndpoint))
        {
            return null;
        }

        if (!Uri.TryCreate(server.OAuthAuthorizationEndpoint, UriKind.Absolute, out var authorizationEndpoint) ||
            !Uri.TryCreate(server.OAuthTokenEndpoint, UriKind.Absolute, out var tokenEndpoint) ||
            string.IsNullOrWhiteSpace(server.OAuthClientId))
        {
            throw new InvalidOperationException(
                $"MCP server '{server.Name}' OAuth configuration needs authorization and token endpoints plus oauth_client_id.");
        }

        var port = server.OAuthCallbackPort is > 0 and <= 65535 ? server.OAuthCallbackPort : 53123;
        var redirectUri = $"http://127.0.0.1:{port}/callback/";
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = server.OAuthClientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        if (server.OAuthScopes.Count > 0)
        {
            query["scope"] = string.Join(' ', server.OAuthScopes);
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();
        var browserUri = authorizationEndpoint + (authorizationEndpoint.Query.Length == 0 ? "?" : "&") +
            string.Join('&', query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        Process.Start(new ProcessStartInfo(browserUri) { UseShellExecute = true });

        using var registration = cancellationToken.Register(listener.Stop);
        var context = await listener.GetContextAsync().ConfigureAwait(false);
        var code = context.Request.QueryString["code"];
        var returnedState = context.Request.QueryString["state"];
        var error = context.Request.QueryString["error"];
        var responseText = error is null && code is not null && returnedState == state
            ? "Authorization received. You can close this window."
            : "Authorization failed. You can close this window.";
        var responseBytes = Encoding.UTF8.GetBytes(responseText);
        context.Response.ContentType = "text/plain";
        context.Response.ContentLength64 = responseBytes.Length;
        await context.Response.OutputStream.WriteAsync(responseBytes, cancellationToken).ConfigureAwait(false);
        context.Response.Close();

        if (error is not null || code is null || returnedState != state)
        {
            throw new InvalidOperationException($"MCP OAuth authorization failed for server '{server.Name}'.");
        }

        using var client = new HttpClient();
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = server.OAuthClientId,
                ["code_verifier"] = verifier,
            }),
        };
        var secret = string.IsNullOrWhiteSpace(server.OAuthClientSecretEnvironment)
            ? null
            : Environment.GetEnvironmentVariable(server.OAuthClientSecretEnvironment);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            var fields = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = server.OAuthClientId,
                ["client_secret"] = secret,
                ["code_verifier"] = verifier,
            };
            tokenRequest.Content = new FormUrlEncodedContent(fields);
        }

        using var tokenResponse = await client.SendAsync(tokenRequest, cancellationToken).ConfigureAwait(false);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return tokenDocument.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException($"MCP OAuth token response for '{server.Name}' had no access_token.");
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
