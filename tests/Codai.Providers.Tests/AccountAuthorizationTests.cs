using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codai.Providers.Accounts;
using Codai.Providers.Tests.Fakes;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Codai.Providers.Tests;

public class AccountAuthorizationTests
{
    [Fact]
    public async Task Atomic_credential_replacement_tolerates_transient_windows_reader_lock()
    {
        var root = Directory.CreateTempSubdirectory("codai-account-write-");
        try
        {
            var store = new AccountCredentialStore(root.FullName);
            AccountSession Session(string token) => new()
            {
                ClientId = "codai-client", Subject = "codai-user", AccessToken = token,
                RefreshToken = "codai-renewal", IdToken = "codai-identity", Scopes = [],
            };
            await store.SaveAsync(Session("codai-old-access"));
            var path = Directory.GetFiles(root.FullName, "*.account").Single();
            Task pending;
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                pending = store.SaveAsync(Session("codai-new-access"));
                await Task.Delay(100);
            }
            await pending;
            (await store.ReadAsync(Session("unused").Id))!.AccessToken.Should().Be("codai-new-access");
            Directory.GetFiles(root.FullName, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Browser_callback_rejects_wrong_state_and_handles_declined_consent_without_token_exchange()
    {
        using var handler = new StubHttpMessageHandler("{}");
        using var http = new HttpClient(handler);
        using var browser = new HttpClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task? returned = null;
        var client = new AccountAuthorizationClient(http, (address, token) =>
        {
            var query = Query(address);
            returned = Task.Run(async () =>
            {
                using var invalid = await browser.GetAsync(query["redirect_uri"] + "?state=wrong&error=access_denied", token);
                invalid.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
                using var declined = await browser.GetAsync(query["redirect_uri"] + "?state=" + query["state"] + "&error=access_denied", token);
                declined.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
                declined.Headers.CacheControl!.NoStore.Should().BeTrue();
            }, token);
            return Task.CompletedTask;
        });
        (await client.AuthorizeAsync("urn:uuid:codai-host", cancellationToken: deadline.Token)).Should().BeNull();
        await returned!;
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public void Registration_uses_own_name_fresh_challenges_and_exact_loopback_callback()
    {
        using var http = new HttpClient();
        var callback = new Uri("http://127.0.0.1:45321/codai/account");
        var first = AccountAuthorizationClient.CreateAttempt("urn:uuid:codai-host", callback);
        var second = AccountAuthorizationClient.CreateAttempt("urn:uuid:codai-host", callback);
        var query = Query(first.BrowserAddress);
        query["agent_name_hint"].Should().Be("Codai");
        query["redirect_uri"].Should().Be(callback.AbsoluteUri);
        query["code_challenge_method"].Should().Be("S256");
        query["code_challenge"].Should().Be(Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(first.Verifier))));
        first.State.Should().NotBe(second.State);
        first.Nonce.Should().NotBe(second.Nonce);
        first.Verifier.Should().NotBe(second.Verifier);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("client")]
    public async Task Invalid_callback_never_exchanges_credentials(string invalid)
    {
        using var handler = new StubHttpMessageHandler("{}");
        using var http = new HttpClient(handler);
        var client = new AccountAuthorizationClient(http);
        var attempt = AccountAuthorizationClient.CreateAttempt("urn:uuid:codai-host", new Uri("http://127.0.0.1:45321/codai/account"));
        var action = () => client.CompleteAsync(attempt, new Dictionary<string, string?>
        {
            ["state"] = invalid == "state" ? "wrong-state" : attempt.State, ["code"] = "codai-code",
        });
        await action.Should().ThrowAsync<ProviderConfigurationException>();
        handler.LastRequest.Should().BeNull();
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("nonce")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("scope")]
    public async Task Sign_in_requires_valid_identity_and_plan_permission(string scenario)
    {
        using var signing = RSA.Create(2048);
        using var untrusted = RSA.Create(2048);
        var key = new RsaSecurityKey(signing) { KeyId = "codai-signing" };
        var configuration = new OpenIdConnectConfiguration();
        configuration.SigningKeys.Add(key);
        using var handler = new StubHttpMessageHandler("{}");
        using var initialHttp = new HttpClient(handler);
        var attempt = AccountAuthorizationClient.CreateAttempt("urn:uuid:codai-host", new Uri("http://127.0.0.1:45321/codai/account"));
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = attempt.BrowserAddress.GetLeftPart(UriPartial.Authority),
            Audience = scenario == "audience" ? "wrong-client" : "codai-client",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = "codai-user", ["nonce"] = scenario == "nonce" ? "wrong-nonce" : attempt.Nonce },
            SigningCredentials = new SigningCredentials(scenario == "signature" ? new RsaSecurityKey(untrusted) : key, SecurityAlgorithms.RsaSha256),
        });
        using var exchangeHandler = new StubHttpMessageHandler(JsonSerializer.Serialize(new
        {
            access_token = "codai-access", refresh_token = "codai-refresh", id_token = token, token_type = "Bearer", expires_in = 3600,
            scope = scenario == "scope" ? "openid" : "openid offline_access resource.invoke chatgpt.tokens.use.direct",
        }), contentType: "application/json");
        using var http = new HttpClient(exchangeHandler);
        var client = new AccountAuthorizationClient(http, metadata: new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));
        var action = () => client.CompleteAsync(attempt, new Dictionary<string, string?>
        {
            ["state"] = attempt.State, ["client_id"] = "codai-client", ["code"] = "codai-code",
        });
        if (scenario == "valid")
        {
            var session = await action();
            session!.Subject.Should().Be("codai-user");
            session.ClientId.Should().Be("codai-client");
            session.AccessToken.Should().Be("codai-access");
            exchangeHandler.LastRequestBody.Should().Contain("code_verifier=" + attempt.Verifier);
            exchangeHandler.LastRequest!.RequestUri!.Query.Should().BeEmpty();
        }
        else
        {
            await action.Should().ThrowAsync<ProviderConfigurationException>();
        }
    }

    [Fact]
    public async Task Account_storage_round_trips_without_project_files_and_keeps_host_identity()
    {
        var root = Directory.CreateTempSubdirectory("codai-accounts-");
        try
        {
            var store = new AccountCredentialStore(root.FullName);
            var session = new AccountSession
            {
                ClientId = "codai-client", Subject = "codai-user", AccessToken = "codai-access-secret",
                RefreshToken = "codai-refresh-secret", IdToken = "codai-identity", Scopes = [],
            };
            var host = await store.GetHostIdAsync();
            await store.SaveAsync(session);
            (await store.ReadAsync(session.Id))!.AccessToken.Should().Be(session.AccessToken);
            (await store.ListAsync()).Should().ContainSingle();
            (await new AccountCredentialStore(root.FullName).GetHostIdAsync()).Should().Be(host);
            var path = Directory.GetFiles(root.FullName, "*.account").Single();
            if (OperatingSystem.IsWindows())
            {
                Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)).Should().NotContain(session.AccessToken).And.NotContain(session.RefreshToken);
            }
            else
            {
                File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static Dictionary<string, string> Query(Uri address) => address.Query.TrimStart('?').Split('&')
        .Select(part => part.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
}