using System.Text.Json;
using Codai.Cli;
using Codai.Cli.Ui;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.Tools;
using Codai.Core.IO;
using Codai.Providers.Accounts;
using Codai.Session.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Codai.Session.Tests;

public class CliApplicationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Auth_account_sign_out_clears_tokens_but_preserves_registration_and_project(bool confirmed)
    {
        var root = Directory.CreateTempSubdirectory("codai-plan-signout-");
        try
        {
            var path = Path.Combine(root.FullName, "codai-config.json");
            const string original = """{"theme":"forest"}""";
            await File.WriteAllTextAsync(path, original);
            var store = new AccountCredentialStore(Path.Combine(root.FullName, "private"));
            var session = new AccountSession
            {
                ClientId = "codai-client",
                Subject = "codai-user",
                AccessToken = "codai-access",
                RefreshToken = "codai-renewal",
                IdToken = "codai-identity",
                Scopes = [],
            };
            await store.SaveAsync(session);
            var ui = new FakeUserInterface();
            ui.EnqueueChoice(session.Id);
            ui.EnqueueChoice("out");
            var issuer = AccountAuthorizationClient.CreateAttempt("urn:uuid:codai-host", new Uri("http://127.0.0.1:45321/codai/account"))
                .BrowserAddress.GetLeftPart(UriPartial.Authority);
            var metadata = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration { RevocationEndpoint = issuer + "/codai/revoke" };
            using var http = new HttpClient(new AccountModelHandler(confirmed ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.ServiceUnavailable));
            var authorization = new AccountAuthorizationClient(http,
                metadata: new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(metadata));
            var workflow = new AccountConnectionWorkflow(ui, new CodaiConfigurationManager(new PhysicalFileSystem()), path, root.FullName, store, authorization);
            (await workflow.RunAsync()).Should().BeFalse();
            var saved = await store.ReadAsync(session.Id);
            saved!.ClientId.Should().Be(session.ClientId);
            saved.Subject.Should().Be(session.Subject);
            saved.AccessToken.Should().BeEmpty();
            saved.RefreshToken.Should().BeEmpty();
            saved.IdToken.Should().BeEmpty();
            (await File.ReadAllTextAsync(path)).Should().Be(original);
            ui.Infos.Should().Contain(message => confirmed ? message == "Account signed out." : message.Contains("remote revocation was not confirmed", StringComparison.Ordinal));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Auth_account_plan_requires_save_and_keeps_tokens_out_of_project_configuration(bool save)
    {
        var root = Directory.CreateTempSubdirectory("codai-plan-setup-");
        try
        {
            var path = Path.Combine(root.FullName, "codai-config.json");
            const string original = """{"theme":"forest","ai_provider_config":{"api_key":"old-key"}}""";
            await File.WriteAllTextAsync(path, original);
            var ui = new FakeUserInterface();
            ui.EnqueueChoice("account");
            ui.EnqueueChoice("codai-plan-model");
            ui.EnqueueSelection(items =>
            {
                items.Select(item => item.Label).Should().Equal("Save", "Cancel");
                return save ? "save" : "cancel";
            });
            var session = new AccountSession
            {
                ClientId = "codai-client",
                Subject = "codai-user",
                AccessToken = "codai-access-secret",
                RefreshToken = "codai-renewal-secret",
                IdToken = "codai-identity-secret",
                Scopes = [],
            };
            var store = new AccountCredentialStore(Path.Combine(root.FullName, "private"));
            using var http = new HttpClient(new AccountModelHandler());
            var manager = new CodaiConfigurationManager(new PhysicalFileSystem());
            var account = new AccountConnectionWorkflow(ui, manager, path, root.FullName, store,
                new AccountAuthorizationClient(http), (_, _) => Task.FromResult<AccountSession?>(session));
            var setup = new ConnectionSetupWorkflow(ui, manager, path, root.FullName, new AiProviderOptions(), signIn: account.RunAsync);
            (await setup.RunAsync()).Should().Be(save);
            var saved = await File.ReadAllTextAsync(path);
            saved.Should().NotContain(session.AccessToken).And.NotContain(session.RefreshToken).And.NotContain(session.IdToken);
            if (save)
            {
                using var document = JsonDocument.Parse(saved);
                document.RootElement.GetProperty("ai_provider_config").GetProperty("account").GetString().Should().Be(session.Id);
                document.RootElement.GetProperty("ai_provider_config").GetProperty("api_key").GetString().Should().BeEmpty();
                document.RootElement.GetProperty("theme").GetString().Should().Be("forest");
                var loaded = CodaiConfigurationLoader.Load(new(root.FullName), new TestEnvironment(new Dictionary<string, string?>()));
                loaded.AiProviderConfig.Account.Should().Be(session.Id);
                Codai.Providers.ResolvedProviderOptions.From(loaded.AiProviderConfig).EnsureUsable();
                (await store.ReadAsync(session.Id)).Should().NotBeNull();
            }
            else
            {
                saved.Should().Be(original);
                (await store.ListAsync()).Should().BeEmpty();
            }
            string.Join('\n', ui.Lines.Concat(ui.Infos).Concat(ui.Errors).Concat(ui.Successes))
                .Should().NotContain(session.AccessToken).And.NotContain(session.RefreshToken).And.NotContain(session.IdToken);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private sealed class AccountModelHandler(System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("""{"models":[{"slug":"codai-plan-model","display_name":"Codai model","visibility":"list"}]}"""),
            });
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--acp")]
    [InlineData("unexpected-prompt")]
    public async Task Auth_rejects_noninteractive_modes_without_opening_dialogs(string option)
    {
        var ui = new FakeUserInterface();
        var output = new RecordingConsole();
        var app = new CliApplication(output, uiOverride: ui);
        (await app.RunAsync(["auth", option])).Should().Be(1);
        ui.Dialogs.Should().BeEmpty();
        output.Errors.Should().Contain(message => message.Contains("interactive terminal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Auth_cancellation_returns_interrupt_code_without_saving()
    {
        var root = Directory.CreateTempSubdirectory("codai-auth-interrupt-");
        try
        {
            var ui = new FakeUserInterface();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var app = new CliApplication(new RecordingConsole(), () => root.FullName, ui,
                environment: new TestEnvironment(new Dictionary<string, string?>()));
            (await app.RunAsync(["auth"], cancellation.Token)).Should().Be(130);
            File.Exists(Path.Combine(root.FullName, "codai-config.json")).Should().BeFalse();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("local", false, false)]
    [InlineData("local", true, false)]
    [InlineData("hosted", false, false)]
    [InlineData("hosted", true, true)]
    public async Task Auth_supports_listed_or_custom_models_and_masks_credentials(string category, bool manual, bool unavailable)
    {
        var root = Directory.CreateTempSubdirectory("codai-auth-models-");
        try
        {
            var path = Path.Combine(root.FullName, "codai-config.json");
            await File.WriteAllTextAsync(path, """{"theme":"forest","budget":{"max_tokens":64000}}""");
            var ui = new FakeUserInterface();
            ui.EnqueueChoice("provider");
            ui.EnqueueSelection(items =>
            {
                items.Select(item => item.Value).Should().Equal("hosted", "local");
                items.Select(item => item.Label).Should().Equal("Hosted provider", "Local server");
                return category;
            });
            var providerName = category == "local" ? ProviderCatalog.Ollama : ProviderCatalog.OpenAi;
            ui.EnqueueSelection(items =>
            {
                items.Select(item => item.Label).Should().Equal(ProviderCatalog.All
                    .Where(item => category == "local" ? !item.RequiresApiKey : item.RequiresApiKey).Select(item => item.Name));
                items.Should().OnlyContain(item => item.Description == string.Empty && item.Label == item.Value);
                return providerName;
            });
            ui.EnqueueValue("https://models.example.test/v1");
            ui.EnqueueValue("codai-test-credential");
            ui.EnqueueSelection(items =>
            {
                if (unavailable)
                {
                    items.Should().Contain(item => item.Value == "model:gpt-6.1-sol" && item.Description == "Suggested model");
                    items.Should().Contain(item => item.Value == "model:gpt-5.4-mini");
                }
                else if (providerName == ProviderCatalog.OpenAi)
                {
                    items.Select(item => item.Value).Should().Equal("model:codai-listed-model", "model:gpt-5.4-mini", "manual");
                    items.Should().Contain(item => item.Value == "model:gpt-5.4-mini" && item.Description == "Saved model");
                    items.Should().NotContain(item => item.Value == "model:gpt-6.1-sol");
                }
                else
                {
                    items.Select(item => item.Value).Should().Equal("model:codai-listed-model", "manual");
                }
                return manual ? "manual" : "model:codai-listed-model";
            });
            if (manual)
            {
                ui.EnqueueValue("codai-custom-model");
            }
            ui.EnqueueChoice("save");
            var discoveries = 0;
            var app = new CliApplication(new RecordingConsole(), () => root.FullName, ui,
                environment: new TestEnvironment(new Dictionary<string, string?>()),
                modelDiscovery: (provider, endpoint, credential, _) =>
                {
                    discoveries++;
                    provider.Name.Should().Be(providerName);
                    endpoint.Should().Be("https://models.example.test/v1");
                    credential.Should().Be("codai-test-credential");
                    return unavailable ? throw new HttpRequestException("Unavailable")
                        : Task.FromResult<IReadOnlyList<string>>(["codai-listed-model"]);
                });
            (await app.RunAsync(["auth"])).Should().Be(0);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var settings = document.RootElement.GetProperty("ai_provider_config");
            settings.GetProperty("provider").GetString().Should().Be(providerName);
            settings.GetProperty("model").GetString().Should().Be(manual ? "codai-custom-model" : "codai-listed-model");
            settings.GetProperty("api_key").GetString().Should().Be("codai-test-credential");
            document.RootElement.GetProperty("theme").GetString().Should().Be("forest");
            document.RootElement.GetProperty("budget").GetProperty("max_tokens").GetInt32().Should().Be(64000);
            discoveries.Should().Be(1);
            ui.SecretInputs.Should().Contain(true);
            string.Join('\n', ui.Lines.Concat(ui.Infos).Concat(ui.Successes).Concat(ui.Errors)).Should().NotContain("codai-test-credential");
            if (unavailable)
            {
                ui.Infos.Should().Contain(message => message.StartsWith("Model listing unavailable", StringComparison.Ordinal));
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData(null)]
    public async Task Auth_discard_or_back_leaves_existing_configuration_unchanged(string? action)
    {
        var root = Directory.CreateTempSubdirectory("codai-auth-cancel-");
        try
        {
            var path = Path.Combine(root.FullName, "codai-config.json");
            const string original = """{"theme":"forest","ai_provider_config":{"api_key":"saved-credential"}}""";
            await File.WriteAllTextAsync(path, original);
            var ui = new FakeUserInterface();
            ui.EnqueueChoice("provider");
            ui.EnqueueChoice("local");
            ui.EnqueueChoice(ProviderCatalog.Ollama);
            ui.EnqueueValue("http://localhost:8080/v1");
            ui.EnqueueValue(string.Empty);
            ui.EnqueueChoice("manual");
            ui.EnqueueValue("codai-local-model");
            ui.EnqueueChoice(action);
            var app = new CliApplication(new RecordingConsole(), () => root.FullName, ui,
                environment: new TestEnvironment(new Dictionary<string, string?>()),
                modelDiscovery: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>([]));
            (await app.RunAsync(["auth"])).Should().Be(0);
            (await File.ReadAllTextAsync(path)).Should().Be(original);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(ProviderCatalog.OpenAi)]
    [InlineData(ProviderCatalog.AzureOpenAi)]
    [InlineData(ProviderCatalog.Anthropic)]
    [InlineData(ProviderCatalog.Gemini)]
    [InlineData(ProviderCatalog.Ollama)]
    [InlineData(ProviderCatalog.DeepSeek)]
    [InlineData(ProviderCatalog.OpenRouter)]
    [InlineData(ProviderCatalog.Qwen)]
    [InlineData(ProviderCatalog.Mistral)]
    [InlineData(ProviderCatalog.Grok)]
    public async Task Setup_uses_catalog_fallbacks_but_never_guesses_azure_deployment_names(string providerName)
    {
        var root = Directory.CreateTempSubdirectory("codai-provider-fallback-");
        try
        {
            var provider = ProviderCatalog.Get(providerName);
            var deployment = provider.WireFormat == ProviderWireFormat.AzureOpenAi;
            var ui = new FakeUserInterface();
            ui.EnqueueChoice("provider");
            ui.EnqueueChoice(provider.RequiresApiKey ? "hosted" : "local");
            ui.EnqueueChoice(providerName);
            ui.EnqueueValue("https://models.example.test/v1");
            ui.EnqueueValue("codai-test-credential");
            ui.EnqueueSelection(items =>
            {
                var suggestions = deployment ? [] : provider.SuggestedModels.Prepend(provider.DefaultModel).Order(StringComparer.Ordinal).ToArray();
                items.Select(item => item.Value).Should().Equal(suggestions.Select(model => "model:" + model).Append("manual"));
                if (!deployment)
                {
                    items.Where(item => item.Value != "manual").Should().OnlyContain(item => item.Description == "Suggested model");
                }
                return deployment ? "manual" : "model:" + provider.DefaultModel;
            });
            if (deployment)
            {
                ui.EnqueueValue("codai-custom-deployment");
            }
            ui.EnqueueSelection(items =>
            {
                items.Select(item => item.Label).Should().Equal("Save", "Change model", "Cancel");
                return "save";
            });
            var path = Path.Combine(root.FullName, "codai-config.json");
            var setup = new ConnectionSetupWorkflow(ui, new CodaiConfigurationManager(new PhysicalFileSystem()),
                path, root.FullName, new AiProviderOptions(), (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>([]));

            (await setup.RunAsync()).Should().BeTrue();

            var loaded = CodaiConfigurationLoader.Load(new(root.FullName), new TestEnvironment(new Dictionary<string, string?>()));
            loaded.AiProviderConfig.Provider.Should().Be(providerName);
            loaded.AiProviderConfig.Model.Should().Be(deployment ? "codai-custom-deployment" : provider.DefaultModel);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Auth_rejects_credentials_over_remote_plain_http_without_discovery_or_save()
    {
        var root = Directory.CreateTempSubdirectory("codai-auth-transport-");
        try
        {
            var ui = new FakeUserInterface();
            ui.EnqueueChoice("provider");
            ui.EnqueueChoice("hosted");
            ui.EnqueueChoice(ProviderCatalog.OpenAi);
            ui.EnqueueValue("http://models.example.test/v1");
            ui.EnqueueValue("codai-test-credential");
            var app = new CliApplication(new RecordingConsole(), () => root.FullName, ui,
                environment: new TestEnvironment(new Dictionary<string, string?>()),
                modelDiscovery: (_, _, _, _) => throw new InvalidOperationException("Unsafe endpoint must not receive a credential."));
            (await app.RunAsync(["auth"])).Should().Be(0);
            ui.Errors.Should().Contain(message => message.Contains("HTTPS", StringComparison.Ordinal));
            File.Exists(Path.Combine(root.FullName, "codai-config.json")).Should().BeFalse();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bare_command_starts_chat_after_setup_while_auth_only_saves_connection(bool explicitAuth)
    {
        var root = Directory.CreateTempSubdirectory("codai-connection-");
        try
        {
            var ui = new FakeUserInterface();
            if (!explicitAuth)
            {
                ui.EnqueueChoice("graphite");
            }
            ui.EnqueueSelection(items =>
            {
                items.Select(item => item.Value).Should().Equal("provider", "account");
                items.Select(item => item.Label).Should().Equal("Use your own provider", "Continue with ChatGPT");
                return items[0].Value;
            });
            ui.EnqueueChoice("local");
            ui.EnqueueChoice(ProviderCatalog.Ollama);
            ui.EnqueueValue("http://localhost:11434/api");
            ui.EnqueueValue(string.Empty);
            ui.EnqueueChoice("manual");
            ui.EnqueueValue("codai-local-model");
            ui.EnqueueChoice("save");
            var initialSession = new RecordingSession();
            var app = new CliApplication(new RecordingConsole(), () => root.FullName, ui,
                input: new TaskInput(),
                sessionFactory: _ => explicitAuth ? throw new InvalidOperationException("Auth must not start a session.") : initialSession,
                environment: new TestEnvironment(new Dictionary<string, string?> { ["CODAI_CONFIG_DIR"] = root.FullName }),
                modelDiscovery: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>([]));
            (await app.RunAsync(explicitAuth ? ["auth"] : [])).Should().Be(0);
            var options = CodaiConfigurationLoader.Load(new(root.FullName), new TestEnvironment(new Dictionary<string, string?>()));
            options.AiProviderConfig.Provider.Should().Be(ProviderCatalog.Ollama);
            options.AiProviderConfig.Model.Should().Be("codai-local-model");
            options.AiProviderConfig.ApiKey.Should().BeNullOrEmpty();
            ui.SecretInputs.Should().Contain(true);
            initialSession.InteractiveCalls.Should().Be(explicitAuth ? 0 : 1);

            var restartedUi = new FakeUserInterface();
            var session = new RecordingSession();
            var restarted = new CliApplication(new RecordingConsole(), () => root.FullName, restartedUi,
                input: new TaskInput(), sessionFactory: services =>
                {
                    services.GetRequiredService<SessionOptions>().Model.Should().Be("codai-local-model");
                    return session;
                }, environment: new TestEnvironment(new Dictionary<string, string?>()));

            (await restarted.RunAsync([])).Should().Be(0);
            restartedUi.Dialogs.Should().BeEmpty();
            session.InteractiveCalls.Should().Be(1);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Auth_back_from_server_address_returns_to_provider_then_category_then_connection()
    {
        var root = Directory.CreateTempSubdirectory("codai-auth-back-");
        try
        {
            var ui = new FakeUserInterface();
            ui.EnqueueChoice("provider");
            ui.EnqueueChoice("hosted");
            ui.EnqueueChoice(ProviderCatalog.OpenAi);
            ui.EnqueueValue(null);
            ui.EnqueueChoice(null);
            ui.EnqueueChoice(null);
            ui.EnqueueChoice(null);
            var app = new CliApplication(new RecordingConsole(), () => root.FullName, ui,
                environment: new TestEnvironment(new Dictionary<string, string?>()),
                modelDiscovery: (_, _, _, _) => throw new InvalidOperationException("Model discovery must wait for credentials."));

            (await app.RunAsync(["auth"])).Should().Be(0);

            ui.Dialogs.Should().Equal("Codai / Connect a model", "Codai / Your provider", "Codai / Provider",
                "Codai / Provider", "Codai / Your provider", "Codai / Connect a model");
            File.Exists(Path.Combine(root.FullName, "codai-config.json")).Should().BeFalse();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("skills", "skills")]
    [InlineData("workflows", "commands")]
    public async Task Config_global_customization_lists_effective_origins_not_bodies(string target, string section)
    {
        var root = Directory.CreateTempSubdirectory("codai-global-inventory-");
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "project"));
            var global = Directory.CreateDirectory(Path.Combine(root.FullName, "global"));
            foreach (var name in new[] { "review", "audit" })
            {
                var file = section == "skills"
                    ? Path.Combine(global.FullName, section, name, "SKILL.md")
                    : Path.Combine(global.FullName, section, name + ".md");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllTextAsync(file, "---\ndescription: Global metadata\n---\nHidden global body.");
            }

            var projectFile = section == "skills"
                ? Path.Combine(project.FullName, ".codai", section, "review", "SKILL.md")
                : Path.Combine(project.FullName, ".codai", section, "review.md");
            Directory.CreateDirectory(Path.GetDirectoryName(projectFile)!);
            await File.WriteAllTextAsync(projectFile, "---\ndescription: Project metadata\n---\nHidden project body.");
            var output = new RecordingConsole();
            var app = new CliApplication(output, () => project.FullName,
                environment: new TestEnvironment(new Dictionary<string, string?>()));

            (await app.RunAsync(["config", target, "--json", "--config-dir", global.FullName, "--no-hooks"])).Should().Be(0);

            using var report = JsonDocument.Parse(string.Join('\n', output.Lines));
            var entries = report.RootElement.GetProperty(section).EnumerateArray().ToArray();
            entries.Should().HaveCount(2);
            entries.Single(entry => entry.GetProperty("name").GetString() == "review")
                .GetProperty("isGlobal").GetBoolean().Should().BeFalse();
            entries.Single(entry => entry.GetProperty("name").GetString() == "audit")
                .GetProperty("isGlobal").GetBoolean().Should().BeTrue();
            string.Join('\n', output.Lines).Should().Contain("Project metadata").And.NotContain("Hidden");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Global_rules_follow_config_directory_precedence_and_project_overrides(bool flagOverride)
    {
        var root = Directory.CreateTempSubdirectory("codai-global-rules-cli-");
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "project", ".codai", "rules"));
            var environmentRules = Directory.CreateDirectory(Path.Combine(root.FullName, "environment", "rules"));
            var flagRules = Directory.CreateDirectory(Path.Combine(root.FullName, "flag", "rules"));
            await File.WriteAllTextAsync(Path.Combine(project.FullName, "style.md"), "Project style.");
            foreach (var directory in new[] { environmentRules, flagRules })
            {
                await File.WriteAllTextAsync(Path.Combine(directory.FullName, "style.md"), "Overridden global style.");
                await File.WriteAllTextAsync(Path.Combine(directory.FullName, "personal.md"), "Personal preferences.");
            }

            var output = new RecordingConsole();
            var app = new CliApplication(output, () => Path.Combine(root.FullName, "project"),
                environment: new TestEnvironment(new Dictionary<string, string?>
                {
                    ["CODAI_CONFIG_DIR"] = environmentRules.Parent!.FullName,
                }));
            List<string> args = ["config", "rules", "--json", "--no-hooks"];
            if (flagOverride)
            {
                args.AddRange(["--config-dir", flagRules.Parent!.FullName]);
            }

            (await app.RunAsync([.. args])).Should().Be(0);

            using var report = JsonDocument.Parse(string.Join('\n', output.Lines));
            var entries = report.RootElement.GetProperty("rules").EnumerateArray().ToArray();
            entries.Should().HaveCount(2);
            entries[0].GetProperty("name").GetString().Should().Be("style");
            entries[0].GetProperty("isGlobal").GetBoolean().Should().BeFalse();
            entries[1].GetProperty("isGlobal").GetBoolean().Should().BeTrue();
            var expected = flagOverride ? flagRules : environmentRules;
            entries[1].GetProperty("sourcePath").GetString().Should()
                .Be(Path.Combine(expected.FullName, "personal.md").Replace('\\', '/'));
            string.Join('\n', output.Lines).Should().NotContain("Personal preferences.");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Config_settings_uses_file_environment_and_cli_precedence_without_provider_work()
    {
        var root = Directory.CreateTempSubdirectory("codai-config-precedence-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "codai-config.json"),
                """{"ai_provider_config":{"provider":"openai","model":"file-model"}}""");
            var output = new RecordingConsole();
            var app = new CliApplication(output, () => root.FullName,
                environment: new TestEnvironment(new Dictionary<string, string?> { ["MODEL"] = "environment-model" }),
                sessionFactory: _ => throw new InvalidOperationException("Session must not start."));

            (await app.RunAsync(["config", "settings", "--json", "--model", "cli-model", "--no-hooks"])).Should().Be(0);

            using var report = JsonDocument.Parse(string.Join('\n', output.Lines));
            report.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal("paths", "model", "session");
            report.RootElement.GetProperty("model").GetProperty("model").GetString().Should().Be("cli-model");
            report.RootElement.GetProperty("model").GetProperty("apiKeyConfigured").GetBoolean().Should().BeFalse();
            output.Errors.Should().BeEmpty();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Config_management_updates_only_configuration_entries()
    {
        var root = Directory.CreateTempSubdirectory("codai-config-management-");
        try
        {
            var plugin = Path.Combine(root.FullName, "plugin.dll");
            await File.WriteAllTextAsync(plugin, "plugin-binary-placeholder");
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "codai-config.json"), """
                {
                  "plugins": ["plugin.dll"],
                  "mcp": {"servers": [{"name": "docs", "command": "must-not-start"}]}
                }
                """);
            var output = new RecordingConsole();
            var app = new CliApplication(output, () => root.FullName,
                environment: new TestEnvironment(new Dictionary<string, string?>()));

            (await app.RunAsync(["config", "mcp", "disable", "docs"])).Should().Be(0);
            (await app.RunAsync(["config", "plugin", "remove", "plugin.dll"])).Should().Be(0);

            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root.FullName, "codai-config.json")));
            config.RootElement.GetProperty("mcp").GetProperty("servers")[0].GetProperty("enabled").GetBoolean().Should().BeFalse();
            config.RootElement.GetProperty("plugins").GetArrayLength().Should().Be(0);
            File.Exists(plugin).Should().BeTrue();
            output.Errors.Should().BeEmpty();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Config_hooks_reports_disabled_and_default_timeout_metadata_without_running_commands()
    {
        var root = Directory.CreateTempSubdirectory("codai-hook-metadata-");
        try
        {
            var hooks = Directory.CreateDirectory(Path.Combine(root.FullName, "hooks"));
            await File.WriteAllTextAsync(Path.Combine(hooks.FullName, "hooks.json"),
                """{"hooks":[{"name":"guard","event":"TaskStarted","command":"must-not-run"},{"name":"off","enabled":false}]}""");
            var output = new RecordingConsole();
            var app = new CliApplication(output, () => root.FullName,
                environment: new TestEnvironment(new Dictionary<string, string?>()));

            (await app.RunAsync(["config", "hooks", "--json", "--config-dir", root.FullName])).Should().Be(0);

            using var report = JsonDocument.Parse(string.Join('\n', output.Lines));
            var manifest = report.RootElement.GetProperty("hooks")[0];
            manifest.TryGetProperty("error", out _).Should().BeFalse();
            manifest.GetProperty("hooks")[0].GetProperty("timeoutSeconds").GetInt32().Should().Be(10);
            manifest.GetProperty("hooks")[1].GetProperty("enabled").GetBoolean().Should().BeFalse();
            string.Join('\n', output.Lines).Should().NotContain("must-not-run");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("paths", "paths")]
    [InlineData("model", "model")]
    [InlineData("session", "session")]
    [InlineData("tools", "tools")]
    [InlineData("rules", "rules")]
    [InlineData("skills", "skills")]
    [InlineData("commands", "commands")]
    [InlineData("workflows", "commands")]
    [InlineData("hooks", "hooks")]
    [InlineData("mcp", "mcpServers")]
    [InlineData("plugins", "plugins")]
    public async Task Config_json_returns_only_requested_section(string target, string section)
    {
        var output = new RecordingConsole();
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            input: new TaskInput { IsRedirected = true },
            environment: new TestEnvironment(new Dictionary<string, string?>()));

        (await app.RunAsync(["config", target, "--json", "--no-hooks"])).Should().Be(0);

        using var report = JsonDocument.Parse(string.Join('\n', output.Lines));
        report.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal(section);
        output.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("agent")]
    public async Task Config_tools_respects_mode_shell_delegation_and_named_exclusions(string mode)
    {
        var output = new RecordingConsole();
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            environment: new TestEnvironment(new Dictionary<string, string?>()));

        (await app.RunAsync(["config", "tools", "--json", "--mode", mode,
            "--no-shell", "--no-sub-agents", "--no-hooks", "--disable-tool=fetch_web"])).Should().Be(0);

        using var report = JsonDocument.Parse(string.Join('\n', output.Lines));
        var tools = report.RootElement.GetProperty("tools");
        var available = tools.GetProperty("available").EnumerateArray().Select(entry => entry.GetString()).ToArray();
        var unavailable = tools.GetProperty("unavailable").EnumerateArray().Select(entry => entry.GetString()).ToArray();
        available.Should().Contain("read_file").And.NotContain(["run_command", "dotnet_build", "dotnet_test", "task", "fetch_web"]);
        unavailable.Should().Contain(["run_command", "dotnet_build", "dotnet_test", "task", "fetch_web"]);
        available.Contains("write_file").Should().Be(mode == "agent");
        tools.GetProperty("externalToolsInspected").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("code")]
    [InlineData("rules skills")]
    [InlineData("agents")]
    public async Task Config_rejects_unknown_or_unimplemented_target_without_starting_session(string target)
    {
        var output = new RecordingConsole();
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            sessionFactory: _ => throw new InvalidOperationException("Session must not start."));

        (await app.RunAsync(["config", target, "--json"])).Should().Be(1);

        output.Lines.Should().BeEmpty();
        output.Errors.Should().ContainSingle().Which.Should().Contain("Unknown configuration target");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Config_reports_credentials_only_as_presence_and_omits_secret_bearing_values(bool json)
    {
        var root = Directory.CreateTempSubdirectory("codai-redaction-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "codai-config.json"),
                """{"ai_provider_config":{"api_key":"secret-key","base_url":"https://user:secret-password@example.com/secret-path?token=secret-query#secret-fragment"},"mcp":{"servers":[{"name":"local","command":"secret-command","args":["secret-argument"],"env":{"TOKEN":"secret-environment"}}]}}""");
            var output = new RecordingConsole();
            var app = new CliApplication(output, () => root.FullName,
                environment: new TestEnvironment(new Dictionary<string, string?>()));
            List<string> args = ["config", "--no-hooks"];
            if (json)
            {
                args.Add("--json");
            }

            (await app.RunAsync([.. args])).Should().Be(0);

            var text = string.Join('\n', output.Lines);
            text.Should().NotContain("secret-").And.Contain("https://example.com").And.Contain("TOKEN");
            output.Errors.Should().BeEmpty();
            if (json)
            {
                using var report = JsonDocument.Parse(text);
                report.RootElement.GetProperty("model").GetProperty("apiKeyConfigured").GetBoolean().Should().BeTrue();
            }
            else
            {
                text.Should().Contain("api key: configured").And.Contain("plugin and MCP discovery skipped");
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("{\"hooks\":null}")]
    [InlineData("{\"hooks\":[null]}")]
    [InlineData("{\"hooks\":[{\"name\":\"bad\",\"event\":\"Unknown\",\"command\":\"secret-command\"}]}")]
    [InlineData("{\"hooks\":[{\"command\":\"secret-command\"},secret-invalid-json]}")]
    public async Task Config_hooks_reports_invalid_manifests_without_executing_or_echoing_contents(string manifest)
    {
        var root = Directory.CreateTempSubdirectory("codai-hook-inspection-");
        try
        {
            var hooks = Directory.CreateDirectory(Path.Combine(root.FullName, "hooks"));
            await File.WriteAllTextAsync(Path.Combine(hooks.FullName, "hooks.json"), manifest);
            var output = new RecordingConsole();
            var app = new CliApplication(output, () => root.FullName,
                environment: new TestEnvironment(new Dictionary<string, string?>()));

            (await app.RunAsync(["config", "hooks", "--json", "--config-dir", root.FullName])).Should().Be(0);

            var text = string.Join('\n', output.Lines);
            text.Should().NotContain("secret-");
            using var report = JsonDocument.Parse(text);
            report.RootElement.GetProperty("hooks")[0].GetProperty("error").GetString().Should().NotBeNullOrEmpty();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Config_hooks_bounds_large_manifest_before_parsing()
    {
        var root = Directory.CreateTempSubdirectory("codai-hook-bound-");
        try
        {
            var hooks = Directory.CreateDirectory(Path.Combine(root.FullName, "hooks"));
            await File.WriteAllTextAsync(Path.Combine(hooks.FullName, "hooks.json"), new string('x', HookRunner.MaxManifestBytes + 1));
            var output = new RecordingConsole();
            var app = new CliApplication(output, () => root.FullName,
                environment: new TestEnvironment(new Dictionary<string, string?>()));

            (await app.RunAsync(["config", "hooks", "--json", "--config-dir", root.FullName])).Should().Be(0);

            using var report = JsonDocument.Parse(string.Join('\n', output.Lines));
            report.RootElement.GetProperty("hooks")[0].GetProperty("error").GetString().Should().Contain("exceeds");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Config_paths_reuses_run_resolution_and_creates_no_state()
    {
        var root = Directory.CreateTempSubdirectory("codai-inspection-paths-");
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "project"));
            var configuration = Directory.CreateDirectory(Path.Combine(root.FullName, "configuration"));
            var output = new RecordingConsole();
            var app = new CliApplication(output, () => root.FullName,
                environment: new TestEnvironment(new Dictionary<string, string?>()));

            (await app.RunAsync(["config", "paths", "--json", "--cwd", "project",
                "--config-dir", "../configuration", "--data-dir", "../state", "--no-hooks"])).Should().Be(0);

            using var report = JsonDocument.Parse(string.Join('\n', output.Lines));
            var paths = report.RootElement.GetProperty("paths");
            paths.GetProperty("projectRoot").GetString().Should().Be(project.FullName);
            paths.GetProperty("configurationDirectory").GetString().Should().Be(configuration.FullName);
            paths.GetProperty("dataDirectory").GetString().Should().Be(Path.Combine(root.FullName, "state"));
            Directory.Exists(Path.Combine(root.FullName, "state")).Should().BeFalse();
            Directory.Exists(Path.Combine(project.FullName, ".codai")).Should().BeFalse();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Config_cancellation_returns_130_without_a_partial_report()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var output = new RecordingConsole();
        var app = new CliApplication(output, Directory.GetCurrentDirectory);

        (await app.RunAsync(["config", "--json"], cancellation.Token)).Should().Be(130);

        output.Lines.Should().BeEmpty();
        output.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Config_inspection_never_loads_plugins_launches_mcp_or_reads_input()
    {
        var root = Directory.CreateTempSubdirectory("codai-inspection-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "codai-config.json"),
                """{"plugins":["missing-plugin.dll"],"mcp":{"servers":[{"name":"offline","command":"codai-must-not-launch-this","args":["secret-argument"],"env":{"TOKEN":"secret-environment"}}]}}""");
            var output = new RecordingConsole();
            var input = new TaskInput { IsRedirected = true, WaitForCancellation = true };
            var app = new CliApplication(output, () => root.FullName, input: input,
                environment: new TestEnvironment(new Dictionary<string, string?>()),
                sessionFactory: _ => throw new InvalidOperationException("Session must not start."));

            (await app.RunAsync(["config", "mcp", "--json", "--no-hooks"])).Should().Be(0);

            input.Reads.Should().Be(0);
            output.Errors.Should().BeEmpty();
            using var report = JsonDocument.Parse(string.Join('\n', output.Lines));
            report.RootElement.GetProperty("mcpServers")[0].GetProperty("name").GetString().Should().Be("offline");
            report.RootElement.GetProperty("mcpServers")[0].GetProperty("enabled").GetBoolean().Should().BeTrue();
            report.RootElement.GetProperty("mcpServers")[0].GetProperty("provenance").GetString().Should().Be("configuration");
            report.RootElement.TryGetProperty("type", out _).Should().BeFalse();
            string.Join('\n', output.Lines).Should().NotContain("secret-argument").And.NotContain("secret-environment");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Config_plugins_reports_missing_and_discovered_paths_without_loading_assemblies()
    {
        var root = Directory.CreateTempSubdirectory("codai-plugin-inspection-");
        try
        {
            var pluginDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, ".codai", "plugins"));
            await File.WriteAllBytesAsync(Path.Combine(pluginDirectory.FullName, "discovered.dll"), [0x4D, 0x5A]);
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "codai-config.json"),
                "{\"plugins\":[\"missing.dll\"]}");
            var output = new RecordingConsole();
            var app = new CliApplication(output, () => root.FullName,
                environment: new TestEnvironment(new Dictionary<string, string?>()),
                sessionFactory: _ => throw new InvalidOperationException("Session must not start."));

            (await app.RunAsync(["config", "plugins", "--json", "--no-hooks"])).Should().Be(0);

            using var report = JsonDocument.Parse(string.Join('\n', output.Lines));
            var plugins = report.RootElement.GetProperty("plugins").EnumerateArray().ToArray();
            plugins.Should().HaveCount(2);
            plugins.Single(plugin => plugin.GetProperty("path").GetString()!.EndsWith("missing.dll", StringComparison.Ordinal))
                .GetProperty("exists").GetBoolean().Should().BeFalse();
            plugins.Single(plugin => plugin.GetProperty("path").GetString()!.EndsWith("discovered.dll", StringComparison.Ordinal))
                .GetProperty("provenance").GetString().Should().Be("project .codai/plugins");
            output.Errors.Should().BeEmpty();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_tools_merge_config_and_cli_without_reenabling_either(bool json)
    {
        var root = Directory.CreateTempSubdirectory("codai-tool-settings-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "codai-config.json"),
                """{"disabled_tools":["write_file","run_command","task"]}""");
            IToolRegistry? registry = null;
            var app = new CliApplication(new RecordingConsole(), () => root.FullName,
                input: new TaskInput(), sessionFactory: provider =>
                {
                    registry = provider.GetRequiredService<IToolRegistry>();
                    return new RecordingSession();
                });
            List<string> args = ["--provider", "ollama", "--no-hooks", "--no-shell", "--no-sub-agents", "--disable-tool=fetch_web", "task"];
            if (json)
            {
                args.Add("--json");
            }

            (await app.RunAsync([.. args])).Should().Be(0);

            registry!.Tools.Select(tool => tool.Name).Should().NotContain(["write_file", "fetch_web"]);
            registry.TryGet("write_file", out _).Should().BeFalse();
            registry.TryGet("fetch_web", out _).Should().BeFalse();
            registry.TryGet("read_file", out _).Should().BeTrue();

            (await app.RunAsync(["--provider", "ollama", "--no-hooks", "task"])).Should().Be(0);

            registry.TryGet("write_file", out _).Should().BeFalse();
            registry.TryGet("fetch_web", out _).Should().BeTrue();
            registry.TryGet("run_command", out _).Should().BeFalse();
            registry.TryGet("task", out _).Should().BeFalse();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_disabled_tool_is_clean_cli_failure_before_task_execution(bool json)
    {
        var output = new RecordingConsole();
        var session = new RecordingSession();
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            input: new TaskInput(), sessionFactory: provider =>
            {
                provider.GetRequiredService<IToolRegistry>();
                return session;
            });
        List<string> args = ["--provider", "ollama", "--no-hooks", "--disable-tool=not_a_tool", "task"];
        if (json)
        {
            args.Add("--json");
        }

        (await app.RunAsync([.. args])).Should().Be(1);

        session.Requests.Should().BeEmpty();
        session.InteractiveCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Execute_plan_routes_once_without_interactive_input_or_required_prompt(bool json, bool piped)
    {
        var output = new RecordingConsole();
        var session = new RecordingSession();
        IUserInterface? selected = null;
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            input: new TaskInput { IsRedirected = piped, Content = piped ? "Additional instructions" : string.Empty },
            sessionFactory: provider =>
            {
                selected = provider.GetRequiredService<IUserInterface>();
                return session;
            });
        List<string> args = ["--execute-plan", "--no-hooks"];
        if (json)
        {
            args.Add("--json");
        }

        (await app.RunAsync([.. args])).Should().Be(0);

        session.PlanInstructions.Should().Equal(piped ? "Additional instructions" : string.Empty);
        session.InteractiveCalls.Should().Be(0);
        session.Requests.Should().BeEmpty();
        (await selected!.ReadInputAsync()).Should().BeNull();
        (await selected.ConfirmAsync("Execute plan?")).Should().BeFalse();
    }

    [Theory]
    [InlineData("agentic", true, CompactionStrategy.Agentic)]
    [InlineData("basic", true, CompactionStrategy.Basic)]
    [InlineData("off", false, CompactionStrategy.Agentic)]
    public async Task Compaction_cli_strategy_reaches_runtime_options(string option, bool automatic, CompactionStrategy strategy)
    {
        SessionOptions? selected = null;
        var app = new CliApplication(new RecordingConsole(), Directory.GetCurrentDirectory,
            input: new TaskInput(), sessionFactory: provider =>
            {
                selected = provider.GetRequiredService<SessionOptions>();
                return new RecordingSession();
            });

        (await app.RunAsync(["task", "--compaction", option, "--no-hooks"])).Should().Be(0);

        selected!.AutoCompact.Should().Be(automatic);
        selected.CompactionStrategy.Should().Be(strategy);
    }

    [Fact]
    public async Task Version_flag_prints_the_version_and_exits()
    {
        var output = new RecordingConsole();

        var exitCode = await new CliApplication(output, () => @"C:\project").RunAsync(["--version"]);

        exitCode.Should().Be(0);
        output.Lines.Should().Contain($"version: {CodaiDefaults.Version}");
    }

    [Fact]
    public async Task Short_version_flag_is_supported()
    {
        var output = new RecordingConsole();

        var exitCode = await new CliApplication(output, () => @"C:\project").RunAsync(["-v"]);

        exitCode.Should().Be(0);
        output.Lines.Should().ContainSingle(line => line.StartsWith("version:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task First_run_saves_theme_then_connection_starts_chat_and_skips_setup_in_another_project(bool sharedDirectory, bool environmentCredential)
    {
        var root = Directory.CreateTempSubdirectory("codai-onboarding-");
        try
        {
            var firstProject = root.CreateSubdirectory("first-project");
            var secondProject = root.CreateSubdirectory("second-project");
            var profile = root.CreateSubdirectory("profile");
            var configurationPath = sharedDirectory ? root.CreateSubdirectory("configuration").FullName : Path.Combine(profile.FullName, ".codai");
            var variables = new Dictionary<string, string?> { ["USERPROFILE"] = profile.FullName, ["HOME"] = profile.FullName };
            if (sharedDirectory)
            {
                variables["CODAI_CONFIG_DIR"] = configurationPath;
            }
            if (environmentCredential)
            {
                variables["API_KEY"] = "codai-first-run-environment-credential";
            }
            var environment = new TestEnvironment(variables);
            var ui = new FakeUserInterface();
            ui.EnqueueSelection(items =>
            {
                items.Select(item => item.Value).Should().Equal(
                    "graphite", "ocean", "azure", "violet", "magenta", "forest", "mint", "ember", "sunset", "coral");
                return "forest";
            });
            ui.EnqueueChoice("provider");
            ui.EnqueueChoice("local");
            ui.EnqueueChoice(ProviderCatalog.Ollama);
            ui.EnqueueValue("http://localhost:11434/api");
            ui.EnqueueValue(string.Empty);
            ui.EnqueueChoice("manual");
            ui.EnqueueValue("codai-onboarding-model");
            ui.EnqueueChoice("save");
            var session = new RecordingSession();
            var app = new CliApplication(new RecordingConsole(), () => firstProject.FullName, ui, input: new TaskInput(),
                environment: environment, sessionFactory: services =>
                {
                    services.GetRequiredService<SessionOptions>().Model.Should().Be("codai-onboarding-model");
                    return session;
                }, modelDiscovery: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>([]));

            (await app.RunAsync([])).Should().Be(0);

            ui.Dialogs.First().Should().Be("Codai / Theme");
            ui.Themes.Should().Contain("forest");
            ui.Successes.Should().Contain("Connection settings saved.");
            session.InteractiveCalls.Should().Be(1);
            var path = Path.Combine(configurationPath, "codai-config.json");
            var saved = await File.ReadAllTextAsync(path);
            var options = CodaiConfigurationLoader.Load(new(configurationPath), environment);
            options.Theme.Should().Be("forest");
            options.AiProviderConfig.Model.Should().Be("codai-onboarding-model");

            var restartedUi = new FakeUserInterface();
            var restartedSession = new RecordingSession();
            var restarted = new CliApplication(new RecordingConsole(), () => secondProject.FullName, restartedUi,
                input: new TaskInput(), environment: environment, sessionFactory: _ => restartedSession);
            (await restarted.RunAsync([])).Should().Be(0);
            restartedUi.Dialogs.Should().BeEmpty();
            restartedSession.InteractiveCalls.Should().Be(1);
            (await File.ReadAllTextAsync(path)).Should().Be(saved);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Interrupted_onboarding_resumes_connection_without_repeating_saved_appearance()
    {
        var root = Directory.CreateTempSubdirectory("codai-onboarding-resume-");
        try
        {
            var environment = new TestEnvironment(new Dictionary<string, string?>
            {
                ["USERPROFILE"] = root.FullName,
                ["HOME"] = root.FullName,
            });
            var firstUi = new FakeUserInterface();
            firstUi.EnqueueChoice("ember");
            firstUi.EnqueueChoice(null);
            var firstSession = new RecordingSession();
            var first = new CliApplication(new RecordingConsole(), () => root.FullName, firstUi,
                input: new TaskInput(), environment: environment, sessionFactory: _ => firstSession);

            (await first.RunAsync([])).Should().Be(0);
            firstSession.InteractiveCalls.Should().Be(0);
            firstUi.Dialogs.Should().Equal("Codai / Theme", "Codai / Connect a model");
            var path = Path.Combine(root.FullName, ".codai", "codai-config.json");
            using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
            {
                document.RootElement.GetProperty("theme").GetString().Should().Be("ember");
                document.RootElement.TryGetProperty("ai_provider_config", out _).Should().BeFalse();
            }

            var resumedUi = new FakeUserInterface();
            resumedUi.EnqueueChoice("provider");
            resumedUi.EnqueueChoice("local");
            resumedUi.EnqueueChoice(ProviderCatalog.Ollama);
            resumedUi.EnqueueValue("http://localhost:11434/api");
            resumedUi.EnqueueValue(string.Empty);
            resumedUi.EnqueueChoice("model:qwen3-coder");
            resumedUi.EnqueueChoice("save");
            var resumedSession = new RecordingSession();
            var resumed = new CliApplication(new RecordingConsole(), () => root.FullName, resumedUi,
                input: new TaskInput(), environment: environment, sessionFactory: _ => resumedSession,
                modelDiscovery: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>([]));

            (await resumed.RunAsync([])).Should().Be(0);
            resumedUi.Dialogs.Should().NotContain("Codai / Theme");
            resumedUi.Themes.Should().Contain("ember");
            resumedSession.InteractiveCalls.Should().Be(1);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Cancelling_first_run_appearance_does_not_open_connection_setup_or_start_session()
    {
        var root = Directory.CreateTempSubdirectory("codai-first-connection-");
        try
        {
            var output = new RecordingConsole();
            var session = new RecordingSession();
            var ui = new FakeUserInterface();

            var exitCode = await new CliApplication(
                output, () => root.FullName, ui, input: new TaskInput(), sessionFactory: _ => session,
                environment: new TestEnvironment(new Dictionary<string, string?>())).RunAsync([]);

            exitCode.Should().Be(0);
            ui.Dialogs.Should().Equal("Codai / Theme");
            session.InteractiveCalls.Should().Be(0);
            session.Requests.Should().BeEmpty();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, false, true)]
    public async Task Saved_hosted_or_account_connections_start_session_unless_auth_explicitly_requested(bool account, bool shared, bool overrideModel, bool explicitAuth)
    {
        var root = Directory.CreateTempSubdirectory("codai-saved-connection-");
        try
        {
            var project = root.CreateSubdirectory("project");
            var configuration = shared ? root.CreateSubdirectory("configuration") : project;
            var path = Path.Combine(configuration.FullName, "codai-config.json");
            var original = JsonSerializer.Serialize(new
            {
                ai_provider_config = new
                {
                    provider = "openai",
                    model = "codai-saved-model",
                    api_key = account ? "" : "codai-saved-credential",
                    account = account ? new string('a', 64) : "",
                    base_url = account ? AccountAuthorizationClient.Resource : "https://api.openai.com/v1",
                },
            });
            await File.WriteAllTextAsync(path, original);
            var profile = root.CreateSubdirectory("profile");
            var global = profile.CreateSubdirectory(".codai");
            await File.WriteAllTextAsync(Path.Combine(global.FullName, "codai-config.json"),
                """{"theme":"ember","ai_provider_config":{"provider":"ollama","model":"global-model-must-not-replace-project"}}""");
            var variables = new Dictionary<string, string?>
            {
                ["USERPROFILE"] = profile.FullName,
                ["HOME"] = profile.FullName,
            };
            if (shared)
            {
                variables["CODAI_CONFIG_DIR"] = configuration.FullName;
            }
            if (overrideModel)
            {
                variables["MODEL"] = "codai-environment-model";
            }
            var ui = new FakeUserInterface();
            var session = new RecordingSession();
            var app = new CliApplication(new RecordingConsole(), () => project.FullName, ui, input: new TaskInput(),
                environment: new TestEnvironment(variables), sessionFactory: services =>
                {
                    services.GetRequiredService<SessionOptions>().Model.Should().Be(overrideModel ? "codai-environment-model" : "codai-saved-model");
                    return session;
                });

            (await app.RunAsync(explicitAuth ? ["auth"] : [])).Should().Be(0);

            session.InteractiveCalls.Should().Be(explicitAuth ? 0 : 1);
            if (explicitAuth)
            {
                ui.Dialogs.Should().Contain("Codai / Connect a model");
            }
            else
            {
                ui.Dialogs.Should().BeEmpty();
            }
            (await File.ReadAllTextAsync(path)).Should().Be(original);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Help_flag_prints_usage_including_session_commands()
    {
        var output = new RecordingConsole();

        var exitCode = await new CliApplication(output, () => @"C:\project").RunAsync(["--help"]);

        exitCode.Should().Be(0);
        output.Lines.Should().Contain(line => line.Contains("/clear-token", StringComparison.Ordinal));
        output.Lines.Should().Contain(line => line.Contains("/compact", StringComparison.Ordinal));
        output.Lines.Should().Contain(line => line.Contains("/mode", StringComparison.Ordinal));
        output.Lines.Should().Contain(line => line.Contains("--continue", StringComparison.Ordinal));
        output.Lines.Should().Contain(line => line.Contains("--mode", StringComparison.Ordinal));
        output.Lines.Should().Contain(line => line.Contains("--no-shell", StringComparison.Ordinal));
        output.Lines.Should().Contain(line => line.Contains("--no-auto-compact", StringComparison.Ordinal));
        output.Lines.Should().Contain(line => line.Contains("--no-sub-agents", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_option_in_the_help_text_is_on_its_own_line()
    {
        var output = new RecordingConsole();

        await new CliApplication(output, () => @"C:\project").RunAsync(["--help"]);

        // An option that shares a line with the previous one is invisible to a reader
        // scanning the column, and it is how a missing newline in the raw string shows up.
        var options = string.Join("\n", output.Lines)
            .Split('\n')
            .Where(line => line.TrimStart().StartsWith("--", StringComparison.Ordinal))
            .ToArray();

        options.Should().NotBeEmpty();
        options.Should().OnlyContain(line => line.Split("--").Length == 2);
    }

    [Fact]
    public async Task Unknown_option_is_reported_with_a_non_zero_exit_code()
    {
        var output = new RecordingConsole();

        var exitCode = await new CliApplication(output, () => @"C:\project")
            .RunAsync(["code", "--nope", "1"]);

        exitCode.Should().Be(1);
        output.Errors.Should().Contain("Unknown option '--nope'.");
    }

    [Fact]
    public async Task Missing_configuration_file_is_reported()
    {
        var output = new RecordingConsole();

        var exitCode = await new CliApplication(output, Directory.GetCurrentDirectory, input: new TaskInput())
            .RunAsync(["code", "--config", "does-not-exist.yml"]);

        exitCode.Should().Be(1);
        output.Errors.Should().Contain(message => message.Contains("was not found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Code_subcommand_is_detected_only_in_the_first_position()
    {
        CliApplication.HasCodeCommand(["code"]).Should().BeTrue();
        CliApplication.HasCodeCommand(["code", "--provider", "openai"]).Should().BeTrue();
        CliApplication.HasCodeCommand(["--provider", "openai", "code"]).Should().BeFalse();
        CliApplication.HasCodeCommand([]).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(130)]
    public async Task Positional_task_runs_once_and_propagates_exit_status(int expectedExit)
    {
        var session = new RecordingSession { ExitCode = expectedExit };
        var input = new TaskInput();
        var app = new CliApplication(
            new RecordingConsole(), Directory.GetCurrentDirectory, input: input, sessionFactory: _ => session);

        var exitCode = await app.RunAsync(["explain tests", "--model", "test-model"]);

        exitCode.Should().Be(expectedExit);
        session.Requests.Should().Equal("explain tests");
        session.InteractiveCalls.Should().Be(0);
        input.Reads.Should().Be(0);
    }

    [Fact]
    public async Task Piped_input_is_one_multiline_task_combined_with_prompt()
    {
        var session = new RecordingSession();
        var input = new TaskInput { IsRedirected = true, Content = "line one\nline two\n" };
        var app = new CliApplication(
            new RecordingConsole(), Directory.GetCurrentDirectory, input: input, sessionFactory: _ => session);

        var exitCode = await app.RunAsync(["summarize"]);

        exitCode.Should().Be(0);
        session.Requests.Should().Equal("summarize\n\nline one\nline two\n");
        input.Reads.Should().Be(1);
        session.InteractiveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Piped_input_without_arguments_is_not_help_or_interactive_input()
    {
        var session = new RecordingSession();
        var input = new TaskInput { IsRedirected = true, Content = ":exit\n/explain\n" };
        var app = new CliApplication(
            new RecordingConsole(), Directory.GetCurrentDirectory, input: input, sessionFactory: _ => session);

        (await app.RunAsync([])).Should().Be(0);

        session.Requests.Should().Equal(input.Content);
        session.InteractiveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Empty_redirected_input_fails_without_starting_session()
    {
        var output = new RecordingConsole();
        var session = new RecordingSession();
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            input: new TaskInput { IsRedirected = true, Content = " \n" }, sessionFactory: _ => session);

        (await app.RunAsync([])).Should().Be(1);

        output.Errors.Should().ContainSingle().Which.Should().Contain("did not contain a task prompt");
        session.Requests.Should().BeEmpty();
        session.InteractiveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Literal_help_flag_is_task_text_after_end_of_options()
    {
        var session = new RecordingSession();
        var app = new CliApplication(new RecordingConsole(), Directory.GetCurrentDirectory,
            input: new TaskInput(), sessionFactory: _ => session);

        (await app.RunAsync(["code", "--", "--help"])).Should().Be(0);

        session.Requests.Should().Equal("--help");
    }

    [Fact]
    public async Task Headless_ui_never_approves_or_reads_input_even_with_ui_override()
    {
        var session = new RecordingSession();
        var output = new RecordingConsole();
        IUserInterface? selectedUi = null;
        IToolApprover? selectedApprover = null;
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            uiOverride: new FakeUserInterface(), input: new TaskInput(), sessionFactory: provider =>
            {
                selectedUi = provider.GetRequiredService<IUserInterface>();
                selectedApprover = provider.GetRequiredService<IToolApprover>();
                return session;
            });

        (await app.RunAsync(["task"])).Should().Be(0);

        (await selectedUi!.ConfirmAsync("write file?")).Should().BeFalse();
        (await selectedApprover!.ApproveAsync(new ToolApprovalRequest("write_file", "Write file", "write file?")))
            .Should().BeFalse();
        (await selectedUi.ReadInputAsync()).Should().BeNull();
        output.Errors.Should().Contain(message => message.Contains("Approval unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancellation_while_reading_pipe_returns_130()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var app = new CliApplication(new RecordingConsole(), Directory.GetCurrentDirectory,
            input: new TaskInput { IsRedirected = true });

        (await app.RunAsync([], cancellation.Token)).Should().Be(130);
    }

    [Fact]
    public async Task Invalid_working_directory_fails_without_starting_session()
    {
        var output = new RecordingConsole();
        var session = new RecordingSession();
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            input: new TaskInput(), sessionFactory: _ => session);

        (await app.RunAsync(["task", "--cwd", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))]))
            .Should().Be(1);

        output.Errors.Should().ContainSingle().Which.Should().Contain("Working directory");
        session.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Working_directory_controls_project_root_and_relative_config_without_changing_process_directory()
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        var project = Directory.CreateTempSubdirectory("codai-cli-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(project.FullName, "settings.json"),
                "{\"ai_provider_config\":{\"model\":\"cwd-test-model\"}}");
            SessionOptions? selectedOptions = null;
            var session = new RecordingSession();
            var app = new CliApplication(new RecordingConsole(), () => project.Parent!.FullName,
                input: new TaskInput(), sessionFactory: provider =>
                {
                    selectedOptions = provider.GetRequiredService<SessionOptions>();
                    return session;
                });

            (await app.RunAsync(["task", "--cwd", project.Name, "--config", "settings.json"]))
                .Should().Be(0);

            selectedOptions!.ProjectRoot.Should().Be(project.FullName);
            selectedOptions.Model.Should().Be("cwd-test-model");
            Directory.GetCurrentDirectory().Should().Be(originalDirectory);
            session.Requests.Should().Equal("task");
        }
        finally
        {
            project.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Whitespace_positional_prompt_is_rejected()
    {
        var output = new RecordingConsole();
        var session = new RecordingSession();
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            input: new TaskInput(), sessionFactory: _ => session);

        (await app.RunAsync([" \n"])).Should().Be(1);

        session.Requests.Should().BeEmpty();
        output.Errors.Should().ContainSingle().Which.Should().Contain("must not be empty");
    }

    [Fact]
    public async Task Json_task_output_has_stream_events_usage_and_exact_final_text()
    {
        var output = new RecordingConsole();
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            input: new TaskInput(), sessionFactory: provider => new RecordingSession
            {
                TaskHandler = cancellationToken =>
                {
                    var ui = provider.GetRequiredService<IUserInterface>();
                    ui.WriteMarkdown("Hello\n");
                    ui.WriteMarkdown("\"world\"");
                    ui.ShowTokens("test-model", new(10, 5), new(10, 5), 0.01m);
                    return Task.FromResult(0);
                },
            });

        (await app.RunAsync(["task", "--json"])).Should().Be(0);

        var events = output.Lines.Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        events.Should().OnlyContain(line => line.GetProperty("ts").ValueKind == JsonValueKind.String);
        var result = events.Single(line => line.GetProperty("type").GetString() == "run_result");
        result.GetProperty("text").GetString().Should().Be("Hello\n\"world\"");
        result.GetProperty("finishReason").GetString().Should().Be("completed");
        result.GetProperty("model").GetString().Should().Be("test-model");
        result.GetProperty("usage").GetProperty("inputTokens").GetInt64().Should().Be(10);
        result.GetProperty("exitCode").GetInt32().Should().Be(0);
        events.Should().Contain(line => line.GetProperty("type").GetString() == "agent_event" &&
            line.GetProperty("event").GetProperty("type").GetString() == "content_update");
        output.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Json_timeout_emits_aborted_result_and_structured_error()
    {
        var output = new RecordingConsole();
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            input: new TaskInput { IsRedirected = true, WaitForCancellation = true });

        (await app.RunAsync(["--json", "--timeout", "0.05"])).Should().Be(124);

        var events = output.Lines.Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        events.Single(line => line.GetProperty("type").GetString() == "run_result")
            .GetProperty("finishReason").GetString().Should().Be("aborted");
        events.Single(line => line.GetProperty("type").GetString() == "run_aborted")
            .GetProperty("reason").GetString().Should().Be("timeout");
        JsonSerializer.Deserialize<JsonElement>(output.Errors.Single()).GetProperty("type").GetString().Should().Be("error");
    }

    [Fact]
    public async Task Json_mode_without_task_fails_without_reading_interactive_input()
    {
        var output = new RecordingConsole();
        var app = new CliApplication(output, Directory.GetCurrentDirectory, input: new TaskInput());

        (await app.RunAsync(["--json"])).Should().Be(1);

        output.Lines.Should().Contain(line => JsonSerializer.Deserialize<JsonElement>(line)
            .GetProperty("type").GetString() == "run_result");
        JsonSerializer.Deserialize<JsonElement>(output.Errors.Single()).GetProperty("message").GetString()
            .Should().Contain("requires a positional or piped task");
    }

    [Fact]
    public async Task Configuration_directory_and_data_directory_do_not_change_workspace_root()
    {
        var temporary = Directory.CreateTempSubdirectory("codai-cli-isolated-");
        try
        {
            var workspace = Directory.CreateDirectory(Path.Combine(temporary.FullName, "workspace"));
            var config = Directory.CreateDirectory(Path.Combine(temporary.FullName, "config"));
            var data = Path.Combine(temporary.FullName, "data");
            await File.WriteAllTextAsync(Path.Combine(config.FullName, CodaiOptions.FileName + ".json"),
                "{\"ai_provider_config\":{\"model\":\"isolated-model\"}}");
            var app = new CliApplication(new RecordingConsole(), () => workspace.FullName,
                input: new TaskInput(), sessionFactory: provider => new RecordingSession
                {
                    TaskHandler = async cancellationToken =>
                    {
                        var settings = provider.GetRequiredService<SessionOptions>();
                        settings.ProjectRoot.Should().Be(workspace.FullName);
                        settings.Model.Should().Be("isolated-model");
                        await provider.GetRequiredService<IAgentTaskStore>().SaveAsync(workspace.FullName,
                            AgentTask.Create("isolated task").Start().Complete(), cancellationToken);
                        return 0;
                    },
                });

            (await app.RunAsync(["task", "--config-dir", config.FullName, "--data-dir", data])).Should().Be(0);

            Directory.Exists(Path.Combine(workspace.FullName, ".codai")).Should().BeFalse();
            Directory.GetFiles(data, "latest.json", SearchOption.AllDirectories).Should().NotBeEmpty();
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(1, "failed")]
    [InlineData(130, "aborted")]
    public async Task Json_terminal_results_preserve_partial_text_and_failure_status(int exitCode, string reason)
    {
        var output = new RecordingConsole();
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            input: new TaskInput(), sessionFactory: provider => new RecordingSession
            {
                TaskHandler = async cancellationToken =>
                {
                    var ui = provider.GetRequiredService<IUserInterface>();
                    ui.WriteMarkdown("Partial response");
                    await provider.GetRequiredService<IAgentProgressSink>().PublishAsync(
                        new AgentProgressEvent("task", "read file", AgentProgressEventKind.ToolStarted, "read_file", DateTimeOffset.UtcNow),
                        cancellationToken);
                    if (exitCode == 1)
                    {
                        ui.WriteError("Provider failed.");
                    }

                    return exitCode;
                },
            });

        (await app.RunAsync(["task", "--json", "--model", "run-model"])).Should().Be(exitCode);

        var events = output.Lines.Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        var result = events.Single(line => line.GetProperty("type").GetString() == "run_result");
        result.GetProperty("finishReason").GetString().Should().Be(reason);
        result.GetProperty("text").GetString().Should().Be("Partial response");
        result.GetProperty("model").GetString().Should().Be("run-model");
        events.Should().Contain(line => line.GetProperty("type").GetString() == "agent_event" &&
            line.GetProperty("event").GetProperty("type").GetString() == "progress");
    }

    [Fact]
    public async Task Isolated_path_flags_override_environment_directories()
    {
        var temporary = Directory.CreateTempSubdirectory("codai-environment-");
        try
        {
            var workspace = Directory.CreateDirectory(Path.Combine(temporary.FullName, "workspace"));
            var environmentConfig = Directory.CreateDirectory(Path.Combine(temporary.FullName, "environment-config"));
            var explicitConfig = Directory.CreateDirectory(Path.Combine(temporary.FullName, "explicit-config"));
            var environmentData = Path.Combine(temporary.FullName, "environment-data");
            var explicitData = Path.Combine(temporary.FullName, "explicit-data");
            await File.WriteAllTextAsync(Path.Combine(environmentConfig.FullName, CodaiOptions.FileName + ".json"),
                "{\"ai_provider_config\":{\"model\":\"environment-model\"}}");
            await File.WriteAllTextAsync(Path.Combine(explicitConfig.FullName, CodaiOptions.FileName + ".json"),
                "{\"ai_provider_config\":{\"model\":\"explicit-model\"}}");
            var expectedModel = "environment-model";
            var environment = new TestEnvironment(new Dictionary<string, string?>
            {
                ["CODAI_CONFIG_DIR"] = environmentConfig.FullName,
                ["CODAI_DATA_DIR"] = environmentData,
            });
            var app = new CliApplication(new RecordingConsole(), () => workspace.FullName,
                input: new TaskInput(), environment: environment, sessionFactory: provider => new RecordingSession
                {
                    TaskHandler = async cancellationToken =>
                    {
                        provider.GetRequiredService<SessionOptions>().Model.Should().Be(expectedModel);
                        await provider.GetRequiredService<IAgentTaskStore>().SaveAsync(workspace.FullName,
                            AgentTask.Create(expectedModel).Start().Complete(), cancellationToken);
                        return 0;
                    },
                });

            (await app.RunAsync(["task"])).Should().Be(0);
            Directory.Exists(environmentData).Should().BeTrue();
            expectedModel = "explicit-model";
            (await app.RunAsync(["task", "--config-dir", explicitConfig.FullName, "--data-dir", explicitData])).Should().Be(0);
            Directory.Exists(explicitData).Should().BeTrue();
            Directory.Exists(Path.Combine(workspace.FullName, ".codai")).Should().BeFalse();
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    private sealed class TestEnvironment(IReadOnlyDictionary<string, string?> variables) : IEnvironmentReader
    {
        private readonly string _profile = Path.Combine(Path.GetTempPath(), "codai-test-profile-" + Guid.NewGuid().ToString("N"));

        public string? GetVariable(string name) => variables.GetValueOrDefault(name) ??
            (name is "USERPROFILE" or "HOME" ? _profile : null);

        public IReadOnlyDictionary<string, string?> All() => variables;
    }

    private sealed class TaskInput : IConsoleInput
    {
        public bool WaitForCancellation { get; init; }
        public bool IsRedirected { get; init; }
        public string Content { get; init; } = string.Empty;
        public int Reads { get; private set; }

        public string? ReadLine() => throw new InvalidOperationException("Interactive input must not be read.");

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }

        public async Task<string> ReadToEndAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return Content;
        }
    }

    [Fact]
    public async Task Run_timeout_cancels_stdin_wait_and_returns_distinct_exit_code()
    {
        var output = new RecordingConsole();
        var input = new TaskInput { IsRedirected = true, WaitForCancellation = true };
        var app = new CliApplication(output, Directory.GetCurrentDirectory, input: input);

        (await app.RunAsync(["--timeout", "0.05"])).Should().Be(124);

        output.Errors.Should().Contain("Run timed out.");
    }

    [Fact]
    public async Task Interactive_ui_line_read_observes_cancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var ui = new SpectreUserInterface(input: new TaskInput());
        Func<Task> read = () => ui.ReadInputAsync(cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Clarification_input_wait_observes_cancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var handler = new InteractiveClarificationHandler(new SpectreUserInterface(input: new TaskInput()));
        Func<Task> ask = () => handler.AskAsync("Which database?", [], cancellation.Token);

        await ask.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(false, "false")]
    [InlineData(false, "true")]
    [InlineData(true, "true")]
    public async Task Headless_clarification_never_auto_answers_and_json_reports_failure(bool json, string approval)
    {
        var output = new RecordingConsole();
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            input: new TaskInput(), sessionFactory: provider => new RecordingSession
            {
                TaskHandler = async cancellationToken =>
                {
                    using var args = JsonDocument.Parse("{\"question\":\"Which database?\",\"options\":[\"SQLite\",\"PostgreSQL\"]}");
                    var result = await provider.GetRequiredService<IToolExecutor>()
                        .ExecuteAsync("ask_question", args.RootElement, cancellationToken);
                    result.StopsExecution.Should().BeTrue();
                    provider.GetRequiredService<IUserInterface>().WriteError(result.Content);
                    return result.IsSuccess ? 0 : 1;
                },
            });
        List<string> args = ["task", "--provider", "ollama", "--mode", "plan", "--auto-approve", approval,
            "--no-hooks", "--no-sub-agents", "--timeout", "5"];
        if (json)
        {
            args.Add("--json");
        }

        (await app.RunAsync([.. args])).Should().Be(1);

        if (json)
        {
            var events = output.Lines.Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
            events.Single(entry => entry.GetProperty("type").GetString() == "run_result")
                .GetProperty("finishReason").GetString().Should().Be("failed");
            events.Where(entry => entry.GetProperty("type").GetString() == "agent_event")
                .Select(entry => entry.GetProperty("event"))
                .Where(entry => entry.GetProperty("type").GetString() == "notice")
                .Select(entry => entry.GetProperty("text").GetString()).Should().Contain("Which database?");
        }
        else
        {
            output.Errors.Should().Contain("Which database?");
        }
    }

    [Fact]
    public async Task Run_timeout_reaches_active_session_and_maps_returned_cancellation()
    {
        var output = new RecordingConsole();
        var session = new RecordingSession
        {
            TaskHandler = async cancellationToken =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return 130;
                }

                return 0;
            },
        };
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            input: new TaskInput(), sessionFactory: _ => session);

        (await app.RunAsync(["task", "--timeout", "0.2"])).Should().Be(124);

        session.Requests.Should().ContainSingle();
        output.Errors.Should().Contain("Run timed out.");
    }

    [Fact]
    public async Task Caller_cancellation_takes_precedence_over_configured_timeout()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var output = new RecordingConsole();
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            input: new TaskInput { IsRedirected = true });

        (await app.RunAsync(["--timeout", "1"], cancellation.Token)).Should().Be(130);

        output.Errors.Should().NotContain("Run timed out.");
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task Explicit_approval_policy_controls_tools_and_prose_changes(string flag, bool expected)
    {
        IUserInterface? selectedUi = null;
        IToolApprover? approver = null;
        var output = new RecordingConsole();
        var app = new CliApplication(output, Directory.GetCurrentDirectory,
            input: new TaskInput(), sessionFactory: provider =>
            {
                selectedUi = provider.GetRequiredService<IUserInterface>();
                approver = provider.GetRequiredService<IToolApprover>();
                return new RecordingSession();
            });

        (await app.RunAsync(["task", "--auto-approve", flag])).Should().Be(0);

        (await selectedUi!.ConfirmAsync("apply prose diff?")).Should().Be(expected);
        (await approver!.ApproveAsync(new ToolApprovalRequest("write_file", "Write", "write file?")))
            .Should().Be(expected);
        (await selectedUi.ReadInputAsync()).Should().BeNull();
        if (expected)
        {
            output.Errors.Should().Contain(message => message.Contains("Auto-approved", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("agent", "false", false)]
    [InlineData("agent", "true", true)]
    [InlineData("plan", "true", false)]
    public async Task Auto_approval_cannot_bypass_read_only_modes(string mode, string approval, bool expectedWrite)
    {
        var project = Directory.CreateTempSubdirectory("codai-approval-");
        try
        {
            var app = new CliApplication(new RecordingConsole(), () => project.FullName,
                input: new TaskInput(), sessionFactory: provider => new RecordingSession
                {
                    TaskHandler = async cancellationToken =>
                    {
                        var registry = provider.GetRequiredService<IToolRegistry>();
                        provider.GetRequiredService<ToolAccessPolicy>().BeginRequest(
                            AgentModePolicy.AllowedTools(provider.GetRequiredService<AgentModeState>().Mode, registry.Tools));
                        using var arguments = JsonDocument.Parse("{\"path\":\"test.txt\",\"content\":\"approved\"}");
                        var result = await provider.GetRequiredService<IToolExecutor>()
                            .ExecuteAsync("write_file", arguments.RootElement, cancellationToken);
                        return result.IsSuccess ? 0 : 1;
                    },
                });

            (await app.RunAsync(["task", "--mode", mode, "--auto-approve", approval, "--no-sub-agents"]))
                .Should().Be(expectedWrite ? 0 : 1);

            File.Exists(Path.Combine(project.FullName, "test.txt")).Should().Be(expectedWrite);
        }
        finally
        {
            project.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public async Task Clarification_uses_answer_editor_through_approval_wrapper(string approval)
    {
        var project = Directory.CreateTempSubdirectory("codai-question-routing-");
        try
        {
            using var writer = new StringWriter();
            var console = Spectre.Console.AnsiConsole.Create(new Spectre.Console.AnsiConsoleSettings
            {
                Out = new Spectre.Console.AnsiConsoleOutput(writer),
                Ansi = Spectre.Console.AnsiSupport.Yes,
                Interactive = Spectre.Console.InteractionSupport.Yes,
            });
            console.Profile.Width = 80;
            console.Profile.Height = 24;
            using var ui = new SpectreUserInterface(console, new TaskInput());
            var app = new CliApplication(new RecordingConsole(), () => project.FullName,
                uiOverride: ui, input: new TaskInput(), sessionFactory: provider => new RecordingSession
                {
                    InteractiveHandler = async token =>
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(TimeSpan.FromMilliseconds(100));
                        Func<Task> ask = () => provider.GetRequiredService<IClarificationHandler>()
                            .AskAsync("Which name?", [], deadline.Token);
                        await ask.Should().ThrowAsync<OperationCanceledException>();
                        writer.ToString().Should().Contain("Answer:");
                        return 0;
                    },
                });
            (await app.RunAsync(["code", "--provider", "ollama", "--model", "codai-question", "--auto-approve", approval, "--no-sub-agents"]))
                .Should().Be(0);
        }
        finally
        {
            project.Delete(recursive: true);
        }
    }

    private sealed class RecordingSession : ICodaiSession
    {
        public Func<CancellationToken, Task<int>>? TaskHandler { get; init; }
        public Func<CancellationToken, Task<int>>? InteractiveHandler { get; init; }
        public int ExitCode { get; init; }
        public int InteractiveCalls { get; private set; }
        public List<string> Requests { get; } = [];
        public List<string> PlanInstructions { get; } = [];

        public Task<int> RunAsync(CancellationToken cancellationToken = default)
        {
            InteractiveCalls++;
            return InteractiveHandler?.Invoke(cancellationToken) ?? Task.FromResult(ExitCode);
        }

        public Task<int> RunTaskAsync(string request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return TaskHandler?.Invoke(cancellationToken) ?? Task.FromResult(ExitCode);
        }

        public Task<int> ExecutePlanAsync(string instructions = "", CancellationToken cancellationToken = default)
        {
            PlanInstructions.Add(instructions);
            return TaskHandler?.Invoke(cancellationToken) ?? Task.FromResult(ExitCode);
        }
    }

    private sealed class RecordingConsole : IConsoleOutput
    {
        public List<string> Lines { get; } = [];

        public List<string> Errors { get; } = [];

        public void WriteLine(string message) => Lines.Add(message);

        public void WriteError(string message) => Errors.Add(message);
    }
}
