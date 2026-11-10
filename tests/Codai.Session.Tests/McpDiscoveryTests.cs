using Codai.Application.Inspection;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Mcp.Client;
using Codai.Mcp.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Codai.Session.Tests;

public sealed class McpDiscoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deferred_initialization_uses_latest_selection_instead_of_startup_snapshot(bool initiallyEmpty)
    {
        var services = new ServiceCollection();
        services.AddCodaiCore();
        services.AddCodaiTools();
        var discoverer = new GatedDiscoverer();
        var reads = 0;
        IReadOnlyList<McpServerOptions> current = [];
        services.AddCodaiMcpClientTools(new McpOptions
        {
            Servers = initiallyEmpty ? [] : [new McpServerOptions { Name = "previous", Command = "must-not-start" }],
        }, Path.GetTempPath(), discoverer, deferDiscovery: true, serverSource: cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            reads++;
            return Task.FromResult(current);
        });
        using var provider = services.BuildServiceProvider();
        var registry = (Codai.Core.Tools.IAsyncToolRegistry)provider.GetRequiredService<Codai.Core.Tools.IToolRegistry>();
        reads.Should().Be(0);
        discoverer.Calls.Should().Be(0);
        current = [new McpServerOptions { Name = "current", Command = "fixture" },
            new McpServerOptions { Name = "unchecked", Enabled = false, Command = "must-not-start" }];
        discoverer.Release.SetResult([new McpToolDefinition("mcp_current_docs", "Documentation", "{\"type\":\"object\"}",
            true, false, (_, _, _) => Task.FromResult("fixture docs"))]);

        await registry.InitializeAsync();
        await registry.InitializeAsync();

        reads.Should().Be(1);
        discoverer.Calls.Should().Be(1);
        discoverer.SeenServers.Should().ContainSingle().Which.Name.Should().Be("current");
        registry.Tools.Should().Contain(tool => tool.Name == "mcp_current_docs").And.Contain(tool => tool.Name == "read_file");
    }

    [Fact]
    public async Task Deferred_registration_keeps_workspace_startup_offline_and_initializes_once()
    {
        var services = new ServiceCollection();
        services.AddCodaiCore();
        services.AddCodaiTools();
        var discoverer = new GatedDiscoverer();
        services.AddCodaiMcpClientTools(new McpOptions
        {
            Servers = [new McpServerOptions { Name = "slow", Command = "must-not-start" }],
        }, Path.GetTempPath(), discoverer, deferDiscovery: true);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<Codai.Core.Tools.IToolRegistry>();
        var prompt = new Codai.Core.Prompting.PromptBuilder("Answer the request.", registry.Tools);

        discoverer.Calls.Should().Be(0);
        registry.Tools.Should().Contain(tool => tool.Name == "read_file");
        registry.Tools.Should().NotContain(tool => tool.Name == "run_command" || tool.Name == "task");
        var deferred = registry.Should().BeAssignableTo<Codai.Core.Tools.IAsyncToolRegistry>().Subject;
        var first = deferred.InitializeAsync();
        var second = deferred.InitializeAsync();
        discoverer.Calls.Should().Be(1);
        first.IsCompleted.Should().BeFalse();
        second.IsCompleted.Should().BeFalse();
        discoverer.Release.SetResult([new McpToolDefinition("mcp_slow_lookup", "Lookup", "{\"type\":\"object\"}",
            true, false, (_, _, _) => Task.FromResult("ok"))]);

        await Task.WhenAll(first, second);
        await deferred.InitializeAsync();

        discoverer.Calls.Should().Be(1);
        registry.Tools.Should().Contain(tool => tool.Name == "read_file");
        registry.TryGet("mcp_slow_lookup", out _).Should().BeTrue();
        prompt.Build(Codai.Core.Projects.ProjectContext.Empty, [], "lookup", new Codai.Core.Prompting.PromptExtras
        {
            Mode = Codai.Core.Agents.AgentMode.Agent,
        }).AllowedTools.Should().Contain("mcp_slow_lookup");
    }

    [Fact]
    public async Task Deferred_registration_retries_cancelled_discovery_without_publishing_partial_tools()
    {
        var services = new ServiceCollection();
        var discoverer = new GatedDiscoverer();
        services.AddCodaiMcpClientTools(new McpOptions
        {
            Servers = [new McpServerOptions { Name = "slow", Command = "must-not-start" }],
        }, Path.GetTempPath(), discoverer, deferDiscovery: true);
        using var provider = services.BuildServiceProvider();
        var registry = (Codai.Core.Tools.IAsyncToolRegistry)provider.GetRequiredService<Codai.Core.Tools.IToolRegistry>();
        using var cancellation = new CancellationTokenSource();
        var initialize = registry.InitializeAsync(cancellation.Token);
        cancellation.Cancel();
        Func<Task> cancelled = () => initialize;
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        registry.Tools.Should().BeEmpty();
        discoverer.Release.SetResult([]);

        await registry.InitializeAsync();

        discoverer.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Deferred_registration_never_connects_disabled_servers()
    {
        var services = new ServiceCollection();
        var discoverer = new GatedDiscoverer();
        services.AddCodaiMcpClientTools(new McpOptions
        {
            Servers = [new McpServerOptions { Name = "disabled", Enabled = false, Command = "must-not-start" }],
        }, Path.GetTempPath(), discoverer, deferDiscovery: true);
        using var provider = services.BuildServiceProvider();
        var registry = (Codai.Core.Tools.IAsyncToolRegistry)provider.GetRequiredService<Codai.Core.Tools.IToolRegistry>();

        await registry.InitializeAsync();

        discoverer.Calls.Should().Be(0);
        registry.Tools.Should().BeEmpty();
    }

    private sealed class GatedDiscoverer : IMcpToolDiscoverer
    {
        public int Calls { get; private set; }
        public IReadOnlyList<McpServerOptions> SeenServers { get; private set; } = [];
        public TaskCompletionSource<IReadOnlyList<McpToolDefinition>> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<McpToolDefinition>> DiscoverAsync(IEnumerable<McpServerOptions> servers, string projectRoot,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            SeenServers = servers.ToArray();
            return Release.Task.WaitAsync(cancellationToken);
        }
    }

    [Theory]
    [InlineData("mcpServers")]
    [InlineData("servers")]
    public async Task Codai_project_manifest_is_discovered_without_runtime_configuration_or_automatic_startup(string wrapper)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-dedicated-mcp-manifest", "repo"));
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-dedicated-mcp-manifest-home", ".codai"));
        var harness = new SessionHarness(root);
        var manifest = Path.Combine(root, ".codai", "mcp.json");
        var contents = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [wrapper] = new { docs = new { command = "must-not-start" } },
        });
        harness.FileSystem.AddFile(manifest, contents);
        var catalog = new McpManifestCatalog(harness.FileSystem, global);

        var server = (await catalog.LoadAsync(root, [])).Should().ContainSingle().Subject;

        server.Name.Should().Be("docs");
        server.Command.Should().Be("must-not-start");
        server.DiscoverySourcePath.Should().Be(manifest);
        server.DiscoveryScope.Should().Be("project");
        server.Enabled.Should().BeFalse();
        var discoverer = new RecordingDiscoverer();
        new ServiceCollection().AddCodaiMcpClientTools(new McpOptions { Servers = [server] }, root, discoverer);
        discoverer.Servers.Should().BeEmpty();

        await catalog.SetEnabledAsync(root, server, true);

        (await catalog.LoadAsync(root, [])).Should().ContainSingle().Which.Enabled.Should().BeTrue();
        (await harness.FileSystem.ReadAllTextAsync(manifest)).Should().Be(contents);
        harness.FileSystem.FileExists(Path.Combine(root, ".codai.json")).Should().BeFalse();
        harness.FileSystem.FileExists(Codai.Core.Customization.UserCustomizationSettings.GetFilePath(global)).Should().BeTrue();
    }

    [Fact]
    public async Task Vs_code_http_entry_with_qualified_name_is_discovered_without_starting()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-vscode-http-entry", "repo"));
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-vscode-http-entry-home", ".codai"));
        var harness = new SessionHarness(root);
        harness.FileSystem.AddFile(Path.Combine(global, ".codai.json"), """
            {"servers":{"microsoftdocs/mcp":{
              "type":"http","url":"https://learn.microsoft.com/api/mcp",
              "gallery":"https://api.mcp.github.com","version":"1.0.0"
            }}}
            """);

        var catalog = new McpManifestCatalog(harness.FileSystem, global);
        var server = (await catalog.LoadAsync(root, [])).Should().ContainSingle().Subject;

        server.Name.Should().Be("microsoftdocs/mcp");
        server.Transport.Should().Be("streamableHttp");
        server.Url.Should().Be("https://learn.microsoft.com/api/mcp");
        server.Gallery.Should().Be("https://api.mcp.github.com");
        server.Version.Should().Be("1.0.0");
        server.Enabled.Should().BeFalse();
        using var client = new HttpClient(new RemoteSseHandler("event: message\n"));
        var tools = await new McpRemoteToolDiscoverer(client).DiscoverAsync([server], root);
        tools.Should().ContainSingle().Which.Name.Should().Be("mcp_microsoftdocs_mcp_lookup");

        await catalog.SetEnabledAsync(root, server, true);
        var file = Path.Combine(global, ".codai.json");
        var document = System.Text.Json.Nodes.JsonNode.Parse(await harness.FileSystem.ReadAllTextAsync(file))!;
        document["servers"]!["microsoftdocs/mcp"]!["version"] = "2.0.0";
        await harness.FileSystem.WriteAllTextAsync(file, document.ToJsonString());
        var refreshed = (await catalog.LoadAsync(root, [])).Single();
        refreshed.Version.Should().Be("2.0.0");
        refreshed.DiscoveryFingerprint.Should().Be(server.DiscoveryFingerprint);
        refreshed.Enabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("../docs")]
    [InlineData("docs//mcp")]
    [InlineData("docs/")]
    [InlineData("docs\\mcp")]
    public async Task Qualified_import_names_reject_empty_or_traversal_segments(string name)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-qualified-mcp-invalid", "repo"));
        var harness = new SessionHarness(root);
        harness.FileSystem.AddFile(Path.Combine(root, ".mcp.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            servers = new Dictionary<string, object> { [name] = new { type = "http", url = "https://example.com/mcp" } },
        }));
        var catalog = new McpManifestCatalog(harness.FileSystem);

        Func<Task> load = async () => await catalog.LoadAsync(root, []);
        await load.Should().ThrowAsync<IOException>().WithMessage("*name*invalid*");
    }

    [Theory]
    [InlineData("event: message\n", "text/event-stream", false)]
    [InlineData(": heartbeat\n\nevent: message\n", "text/event-stream", false)]
    [InlineData("id: 1\nevent: message\n", "text/event-stream", false)]
    [InlineData("event: message\r\n", "text/event-stream", false)]
    [InlineData("", "text/event-stream", false)]
    [InlineData("event: message\n", "text/event-stream", true)]
    [InlineData("", "application/json", false)]
    public async Task Remote_discovery_accepts_sse_metadata_before_json_data(string prefix, string contentType, bool multiline)
    {
        using var client = new HttpClient(new RemoteSseHandler(prefix, contentType, multiline));
        var discoverer = new McpRemoteToolDiscoverer(client);
        var tools = await discoverer.DiscoverAsync([new McpServerOptions
        {
            Name = "docs",
            Transport = "streamableHttp",
            Url = "https://example.com/mcp",
        }], Path.GetTempPath());

        tools.Should().ContainSingle().Which.Name.Should().Be("mcp_docs_lookup");
    }

    [Fact]
    public async Task Remote_discovery_reports_malformed_event_data_as_transport_failure()
    {
        using var client = new HttpClient(new RemoteSseHandler("event: message\ndata: invalid\n\n"));
        var discoverer = new McpRemoteToolDiscoverer(client);
        Func<Task> discover = async () => await discoverer.DiscoverAsync([new McpServerOptions
        {
            Name = "docs",
            Transport = "streamableHttp",
            Url = "https://example.com/mcp",
        }], Path.GetTempPath());

        await discover.Should().ThrowAsync<IOException>().WithMessage("Remote MCP response contained invalid JSON.");
    }

    private sealed class RemoteSseHandler(string prefix, string contentType = "text/event-stream", bool multiline = false) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Accept.Select(value => value.MediaType).Should().Contain("application/json").And.Contain("text/event-stream");
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var method = body.RootElement.GetProperty("method").GetString();
            if (method == "notifications/initialized")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted);
            }
            var result = method == "tools/list"
                ? "{\"tools\":[{\"name\":\"lookup\",\"inputSchema\":{\"type\":\"object\"}}]}" : "{}";
            var id = body.RootElement.GetProperty("id").GetRawText();
            var payload = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + result + "}";
            if (multiline)
            {
                payload = payload.Replace(",\"result\":", ",\ndata: \"result\":", StringComparison.Ordinal);
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(contentType == "application/json" ? payload : prefix + "data: " + payload + "\n\n",
                    System.Text.Encoding.UTF8, contentType),
            };
        }
    }

    [Fact]
    public async Task Stdio_discovery_uses_current_sdk_before_reporting_missing_executable()
    {
        var root = Path.GetTempPath();
        var server = new McpServerOptions
        {
            Name = "sdk-regression",
            Command = Path.Combine(root, Guid.NewGuid().ToString("N"), "missing-server"),
            TimeoutSeconds = 5,
        };
        Func<Task> discover = async () => await new McpStdioToolDiscoverer().DiscoverAsync([server], root);
        await discover.Should().ThrowAsync<ModelContextProtocol.Client.ClientTransportClosedException>();
    }

    [Fact]
    public async Task User_native_servers_remain_visible_in_project_settings_menu_without_automatic_startup()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-user-mcp-menu", "repo"));
        var home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-user-mcp-menu-home"));
        var global = Path.Combine(home, ".codai");
        var harness = new SessionHarness(root);
        var projectFile = Path.Combine(root, ".codai.json");
        harness.FileSystem.AddFile(projectFile, """{"ai_provider_config":{"provider":"ollama","model":"project-model"}}""");
        harness.FileSystem.AddFile(Path.Combine(global, ".codai.json"), """
            {"mcp":{"servers":[
              {"name":"context7","enabled":true,"command":"must-not-start"},
              {"name":"microsoftdocs","transport":"streamableHttp","url":"https://learn.microsoft.com/api/mcp"},
              {"name":"tavily-mcp","command":"must-not-start"}
            ]}}
            """);
        var catalog = new McpManifestCatalog(harness.FileSystem, global, home);
        var discovered = await catalog.LoadAsync(root, []);
        discovered.Select(server => server.Name).Should().Equal("context7", "microsoftdocs", "tavily-mcp");
        discovered.Should().OnlyContain(server => !server.Enabled && server.DiscoveryScope == "user");
        var discoverer = new RecordingDiscoverer();
        new ServiceCollection().AddCodaiMcpClientTools(new McpOptions { Servers = discovered.ToList() }, root, discoverer);
        discoverer.Servers.Should().BeEmpty();
        harness.Ui.EnqueueSelection(items =>
        {
            items.Select(item => item.Value).Should().Equal("context7", "microsoftdocs", "tavily-mcp");
            items.Should().OnlyContain(item => item.RightLabel == "Global" && !item.IsCurrent);
            items[0].Description.Should().Contain("must-not-start");
            items[1].Description.Should().Contain("https://learn.microsoft.com");
            return "[\"context7\",\"microsoftdocs\"]";
        });
        var context = new ConfigurationInspectionContext
        {
            ProjectRoot = root,
            ConfigurationDirectory = root,
            ConfigurationFile = projectFile,
            GlobalConfigurationDirectory = global,
            Provider = "ollama",
            Model = "project-model",
        };
        await new SessionSettingsWorkflow(harness.Ui, harness.Inspector, context, harness.FileSystem, mcpCatalog: catalog).RunAsync("mcp");
        var enabled = await catalog.LoadAsync(root, []);
        enabled.Where(server => server.Enabled).Select(server => server.Name).Should().Equal("context7", "microsoftdocs");
        harness.Ui.Dialogs.Should().ContainSingle().Which.Should().Be("MCPs");
        new ServiceCollection().AddCodaiMcpClientTools(new McpOptions { Servers = enabled.ToList() }, root, discoverer);
        discoverer.Servers.Select(server => server.Name).Should().Equal("context7", "microsoftdocs");

        harness.Ui.EnqueueChoice("[]");
        await new SessionSettingsWorkflow(harness.Ui, harness.Inspector, context, harness.FileSystem, mcpCatalog: catalog).RunAsync("mcp");

        (await catalog.LoadAsync(root, [])).Should().OnlyContain(server => !server.Enabled);
        harness.Ui.Dialogs.Should().Equal("MCPs", "MCPs");
    }

    [Fact]
    public async Task Native_selection_saves_multiple_states_without_changing_runtime_or_unrelated_configuration()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-multiple", "repo"));
        var harness = new SessionHarness(root);
        var path = Path.Combine(root, ".codai.json");
        harness.FileSystem.AddFile(path,
            "{\"mcp\":{\"servers\":[{\"name\":\"first\",\"enabled\":true},{\"name\":\"second\",\"enabled\":false},{\"name\":\"third\",\"enabled\":true}]},\"custom\":{\"keep\":\"value\"}}");
        var runtimeServer = new McpServerOptions { Name = "first", Enabled = true };
        var context = new ConfigurationInspectionContext
        {
            ProjectRoot = root,
            ConfigurationDirectory = root,
            ConfigurationFile = path,
            Provider = "ollama",
            Model = "test",
            McpServers = [runtimeServer],
        };
        harness.Ui.EnqueueSelection(items =>
        {
            items.Select(item => item.IsCurrent).Should().Equal(true, false, true);
            return "[\"second\",\"third\"]";
        });

        await new SessionSettingsWorkflow(harness.Ui, harness.Inspector, context, harness.FileSystem).RunAsync("mcp");

        using var saved = System.Text.Json.JsonDocument.Parse(await harness.FileSystem.ReadAllTextAsync(path));
        saved.RootElement.GetProperty("mcp").GetProperty("servers").EnumerateArray()
            .Select(server => server.GetProperty("enabled").GetBoolean()).Should().Equal(false, true, true);
        saved.RootElement.GetProperty("custom").GetProperty("keep").GetString().Should().Be("value");
        runtimeServer.Enabled.Should().BeTrue();
        harness.Ui.Menus.Should().ContainSingle().Which.MultiSelect.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-json")]
    [InlineData("[\"unknown\"]")]
    public async Task Closed_input_or_invalid_selection_does_not_change_native_configuration(string? choice)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-cancel", "repo"));
        var harness = new SessionHarness(root);
        var path = Path.Combine(root, ".codai.json");
        const string original = "{\"mcp\":{\"servers\":[{\"name\":\"first\",\"enabled\":true}]}}";
        harness.FileSystem.AddFile(path, original);
        var context = new ConfigurationInspectionContext
        {
            ProjectRoot = root,
            ConfigurationDirectory = root,
            ConfigurationFile = path,
            Provider = "ollama",
            Model = "test",
        };
        harness.Ui.EnqueueChoice(choice);

        await new SessionSettingsWorkflow(harness.Ui, harness.Inspector, context, harness.FileSystem).RunAsync("mcp");

        (await harness.FileSystem.ReadAllTextAsync(path)).Should().Be(original);
        harness.Ui.Infos.Should().BeEmpty();
        harness.Ui.Errors.Should().HaveCount(choice is null ? 0 : 1);
    }

    [Fact]
    public void Offline_inspection_discovers_imported_metadata_without_connecting()
    {
        var harness = new SessionHarness(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-offline", "repo")));
        harness.FileSystem.AddFile(Path.Combine(harness.Root, ".mcp.json"),
            "{\"mcpServers\":{\"docs\":{\"command\":\"must-not-start\"}}}");
        var services = new ServiceCollection();
        services.AddCodaiConfigurationInspection(new CodaiOptions
        {
            AiProviderConfig = new AiProviderOptions { Provider = "ollama", Model = "test" },
        }, harness.Root);
        services.Replace(ServiceDescriptor.Singleton<IFileSystem>(harness.FileSystem));
        using var provider = services.BuildServiceProvider();

        var context = provider.GetRequiredService<ConfigurationInspectionContext>();

        context.ExternalToolsInspected.Should().BeFalse();
        context.McpServers.Should().ContainSingle().Which.Enabled.Should().BeFalse();
        context.McpServers.Single().DiscoveryScope.Should().Be("project");
    }

    [Theory]
    [InlineData("[\"docs\"]", true)]
    [InlineData("[]", false)]
    [InlineData(null, false)]
    [InlineData("[\"unknown\"]", false)]
    [InlineData("not-json", false)]
    public async Task Imported_server_checklist_is_only_startup_consent(string? selection, bool expectedEnabled)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-review", "repo"));
        var home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-review-home"));
        var harness = new SessionHarness(root);
        harness.FileSystem.AddFile(Path.Combine(root, ".mcp.json"),
            "{\"mcpServers\":{\"docs\":{\"command\":\"docs\",\"env\":{\"TOKEN\":\"private-secret\"}}}}");
        var catalog = new McpManifestCatalog(harness.FileSystem, Path.Combine(home, ".codai"), home);
        harness.Ui.EnqueueSelection(items =>
        {
            items.Single().RightLabel.Should().Be("Repository");
            items.Single().IsCurrent.Should().BeFalse();
            items.Should().NotContain(item => item.Description.Contains("private-secret", StringComparison.Ordinal));
            return selection;
        });
        var context = new ConfigurationInspectionContext
        {
            ProjectRoot = root,
            ConfigurationDirectory = root,
            GlobalConfigurationDirectory = Path.Combine(home, ".codai"),
            Provider = "ollama",
            Model = "test",
        };

        await new SessionSettingsWorkflow(harness.Ui, harness.Inspector, context, harness.FileSystem, mcpCatalog: catalog).RunAsync("mcp");

        (await catalog.LoadAsync(root, [])).Single().Enabled.Should().Be(expectedEnabled);
        harness.FileSystem.FileExists(Path.Combine(root, ".codai.json")).Should().BeFalse();
        harness.FileSystem.FileExists(Path.Combine(home, ".codai", ".codai.json")).Should().BeFalse();
        harness.FileSystem.FileExists(Codai.Core.Customization.UserCustomizationSettings.GetFilePath(Path.Combine(home, ".codai"))).Should().Be(expectedEnabled);
        harness.FileSystem.FileExists(Path.Combine(home, ".codai", "mcp-approvals.json")).Should().BeFalse();
        harness.Ui.Menus.Should().ContainSingle().Which.MultiSelect.Should().BeTrue();
        harness.Ui.Dialogs.Should().ContainSingle().Which.Should().Be("MCPs");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"mcpServers\":[]}")]
    [InlineData("{\"mcpServers\":{\"docs\":{\"type\":\"websocket\",\"url\":\"https://example.com/mcp\"}}}")]
    [InlineData("{\"mcpServers\":{\"docs\":{\"url\":\"https://user:secret@example.com/mcp\"}}}")]
    public async Task Unsupported_or_malformed_manifests_fail_closed(string contents)
    {
        var harness = new SessionHarness(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-invalid", "repo")));
        harness.FileSystem.AddFile(Path.Combine(harness.Root, ".mcp.json"), contents);
        var load = () => new McpManifestCatalog(harness.FileSystem).LoadAsync(harness.Root, []);
        await load.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task Environment_defaults_expand_and_unapproved_servers_never_reach_tool_discovery()
    {
        var harness = new SessionHarness(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-environment", "repo")));
        harness.FileSystem.AddFile(Path.Combine(harness.Root, ".vscode", "mcp.json"),
            "{\"servers\":{\"docs\":{\"command\":\"${env:CODAI_MCP_TEST_UNSET_7638:-docs}\",\"args\":[\"${workspaceFolder}\"]}}}");
        var servers = await new McpManifestCatalog(harness.FileSystem).LoadAsync(harness.Root, []);
        servers.Single().Command.Should().Be("docs");
        servers.Single().Args.Should().Equal(harness.Root);
        var discoverer = new RecordingDiscoverer();
        new ServiceCollection().AddCodaiMcpClientTools(new McpOptions { Servers = servers.ToList() }, harness.Root, discoverer);
        discoverer.Servers.Should().BeEmpty();
    }

    [Fact]
    public async Task Explicit_manifest_precedence_is_preserved_when_its_path_was_already_discovered()
    {
        var harness = new SessionHarness(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-explicit", "repo")));
        harness.FileSystem.AddFile(Path.Combine(harness.Root, ".mcp.json"), "{\"mcpServers\":{\"docs\":{\"command\":\"explicit\"}}}");
        harness.FileSystem.AddFile(Path.Combine(harness.Root, ".codai.json"),
            "{\"mcpConfigFiles\":[\".mcp.json\"],\"mcpServers\":{\"docs\":{\"command\":\"default\"}}}");

        var servers = await new McpManifestCatalog(harness.FileSystem).LoadAsync(harness.Root, []);

        servers.Single().Command.Should().Be("explicit");
        servers.Single().DiscoveryScope.Should().Be("explicit");
    }

    [Fact]
    public async Task Project_approval_files_cannot_authorize_imported_execution()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-untrusted", "repo"));
        var harness = new SessionHarness(root);
        harness.FileSystem.AddFile(Path.Combine(root, ".mcp.json"), "{\"mcpServers\":{\"docs\":{\"command\":\"docs\"}}}");
        harness.FileSystem.AddFile(Path.Combine(root, ".codai", "mcp-approvals.json"), "not trusted JSON");
        var catalog = new McpManifestCatalog(harness.FileSystem, Path.Combine(root, ".codai"));
        var server = (await catalog.LoadAsync(root, [])).Single();
        server.Enabled.Should().BeFalse();
        var enable = () => catalog.SetEnabledAsync(root, server, true);
        await enable.Should().ThrowAsync<IOException>().WithMessage("*outside*");
    }

    [Fact]
    public async Task Discovery_merges_scopes_without_connecting_and_native_config_wins()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-discovery", "repo"));
        var home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-home"));
        var harness = new SessionHarness(root);
        harness.FileSystem.AddFile(Path.Combine(root, ".git"), "gitdir: ../main/.git");
        harness.FileSystem.AddFile(Path.Combine(home, ".codai.json"),
            "{\"mcpServers\":{\"docs\":{\"command\":\"global-docs\"}}}");
        harness.FileSystem.AddFile(Path.Combine(root, ".mcp.json"),
            "{\"mcpServers\":{\"docs\":{\"command\":\"project-docs\"},\"search\":{\"type\":\"http\",\"url\":\"https://example.com/mcp\",\"auto_approve\":[\"all\"],\"read_only\":true}}}");
        harness.FileSystem.AddFile(Path.Combine(Path.GetDirectoryName(root)!, ".mcp.json"),
            "{\"mcpServers\":{\"outside\":{\"command\":\"outside\"}}}");
        var catalog = new McpManifestCatalog(harness.FileSystem, Path.Combine(home, ".codai"), home);

        var servers = await catalog.LoadAsync(root, [new McpServerOptions { Name = "docs", Command = "native-docs" }]);

        servers.Select(server => server.Name).Should().Equal("docs", "search");
        servers[0].Command.Should().Be("native-docs");
        servers[1].Transport.Should().Be("streamableHttp");
        servers[1].Enabled.Should().BeFalse();
        servers[1].ReadOnly.Should().BeFalse();
        servers[1].AutoApprove.Should().BeEmpty();
        servers[1].DiscoverySourcePath.Should().Be(Path.GetFullPath(Path.Combine(root, ".mcp.json")));
    }

    [Fact]
    public async Task Approval_is_private_project_specific_and_invalidated_by_definition_changes()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-approval", "repo"));
        var home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-approval-home"));
        var harness = new SessionHarness(root);
        var settings = Path.Combine(home, ".codai", ".codai.json");
        harness.FileSystem.AddFile(settings, "{\"ai_provider_config\":{\"provider\":\"ollama\"},\"custom\":{\"keep\":true}}");
        var manifest = Path.Combine(root, ".mcp.json");
        harness.FileSystem.AddFile(manifest, "{\"mcpServers\":{\"docs\":{\"command\":\"docs\",\"args\":[\"original\"]}}}");
        var catalog = new McpManifestCatalog(harness.FileSystem, Path.Combine(home, ".codai"), home);
        var server = (await catalog.LoadAsync(root, [])).Single();
        await catalog.SetEnabledAsync(root, server, true);

        (await catalog.LoadAsync(root, [])).Single().Enabled.Should().BeTrue();
        (await catalog.LoadAsync(Path.Combine(root, "other"), [])).Single().Enabled.Should().BeFalse();
        harness.FileSystem.AddFile(manifest, "{\"mcpServers\":{\"docs\":{\"command\":\"docs\",\"args\":[\"changed\"]}}}");
        (await catalog.LoadAsync(root, [])).Single().Enabled.Should().BeFalse();
        harness.FileSystem.FileExists(Path.Combine(root, ".codai", "mcp-approvals.json")).Should().BeFalse();
        harness.FileSystem.FileExists(Path.Combine(home, ".codai", "mcp-approvals.json")).Should().BeFalse();
        using var saved = System.Text.Json.JsonDocument.Parse(await harness.FileSystem.ReadAllTextAsync(settings));
        saved.RootElement.GetProperty("ai_provider_config").GetProperty("provider").GetString().Should().Be("ollama");
        saved.RootElement.GetProperty("custom").GetProperty("keep").GetBoolean().Should().BeTrue();
        saved.RootElement.TryGetProperty("customization", out _).Should().BeFalse();
        using var state = System.Text.Json.JsonDocument.Parse(await harness.FileSystem.ReadAllTextAsync(
            Codai.Core.Customization.UserCustomizationSettings.GetFilePath(Path.Combine(home, ".codai"))));
        state.RootElement.GetProperty("mcp").EnumerateObject().Should().ContainSingle();
    }

    [Fact]
    public async Task User_runtime_selections_migrate_without_changing_startup_consent()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-runtime-migration", "repo"));
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-runtime-migration-home", ".codai"));
        var harness = new SessionHarness(root);
        harness.FileSystem.AddFile(Path.Combine(root, ".mcp.json"),
            "{\"mcpServers\":{\"docs\":{\"command\":\"docs\"},\"search\":{\"command\":\"search\"}}}");
        var catalog = new McpManifestCatalog(harness.FileSystem, global);
        var servers = await catalog.LoadAsync(root, []);
        await catalog.SetEnabledAsync(root, servers.Single(server => server.Name == "docs"), true);
        await catalog.SetEnabledAsync(root, servers.Single(server => server.Name == "search"), false);
        var stateFile = Codai.Core.Customization.UserCustomizationSettings.GetFilePath(global);
        var previous = System.Text.Json.Nodes.JsonNode.Parse(await harness.FileSystem.ReadAllTextAsync(stateFile))!;
        var settings = Path.Combine(global, ".codai.json");
        var document = new System.Text.Json.Nodes.JsonObject
        {
            ["ai_provider_config"] = new System.Text.Json.Nodes.JsonObject { ["provider"] = "ollama" },
            ["customization"] = previous.DeepClone(),
        };
        harness.FileSystem.AddFile(settings, document.ToJsonString());
        harness.FileSystem.DeleteFile(stateFile);

        var reopened = await new McpManifestCatalog(harness.FileSystem, global).LoadAsync(root, []);

        reopened.Single(server => server.Name == "docs").Enabled.Should().BeTrue();
        reopened.Single(server => server.Name == "search").Enabled.Should().BeFalse();
        var savedState = System.Text.Json.Nodes.JsonNode.Parse(await harness.FileSystem.ReadAllTextAsync(stateFile))!;
        System.Text.Json.Nodes.JsonNode.DeepEquals(savedState, previous).Should().BeTrue();
        var savedSettings = System.Text.Json.Nodes.JsonNode.Parse(await harness.FileSystem.ReadAllTextAsync(settings))!;
        savedSettings["customization"].Should().BeNull();
        savedSettings["ai_provider_config"]!["provider"]!.GetValue<string>().Should().Be("ollama");
        (await catalog.LoadAsync(Path.Combine(root, "other"), [])).Should().OnlyContain(server => !server.Enabled);
    }

    [Fact]
    public async Task Legacy_mcp_selections_migrate_all_projects_without_losing_skill_or_provider_settings()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-migration", "repo"));
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-mcp-migration-home", ".codai"));
        var harness = new SessionHarness(root);
        harness.FileSystem.AddFile(Path.Combine(root, ".mcp.json"), "{\"mcpServers\":{\"docs\":{\"command\":\"docs\"}}}");
        var catalog = new McpManifestCatalog(harness.FileSystem, global);
        var other = Path.Combine(root, "other");
        await catalog.SetEnabledAsync(root, (await catalog.LoadAsync(root, [])).Single(), true);
        await catalog.SetEnabledAsync(other, (await catalog.LoadAsync(other, [])).Single(), true);
        var file = Path.Combine(global, ".codai.json");
        var stateFile = Codai.Core.Customization.UserCustomizationSettings.GetFilePath(global);
        var state = System.Text.Json.Nodes.JsonNode.Parse(await harness.FileSystem.ReadAllTextAsync(stateFile))!.AsObject();
        var legacy = Path.Combine(global, "mcp-approvals.json");
        harness.FileSystem.AddFile(legacy, state["mcp"]!.ToJsonString());
        state.Remove("mcp");
        state["skills"] = new System.Text.Json.Nodes.JsonObject();
        harness.FileSystem.AddFile(file, "{\"custom\":\"preserve\"}");
        await harness.FileSystem.WriteAllTextAsync(stateFile, state.ToJsonString());

        (await catalog.LoadAsync(root, [])).Single().Enabled.Should().BeTrue();
        (await catalog.LoadAsync(other, [])).Single().Enabled.Should().BeTrue();

        harness.FileSystem.FileExists(legacy).Should().BeFalse();
        var saved = System.Text.Json.Nodes.JsonNode.Parse(await harness.FileSystem.ReadAllTextAsync(file))!;
        saved["custom"]!.GetValue<string>().Should().Be("preserve");
        saved["customization"].Should().BeNull();
        var savedState = System.Text.Json.Nodes.JsonNode.Parse(await harness.FileSystem.ReadAllTextAsync(stateFile))!;
        savedState["skills"].Should().NotBeNull();
        savedState["mcp"]!.AsObject().Should().HaveCount(2);
    }

    [Fact]
    public async Task Concurrent_mcp_and_skill_selections_preserve_both_sections()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-selection-merge", "repo"));
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-selection-merge-home", ".codai"));
        var harness = new SessionHarness(root);
        harness.FileSystem.AddFile(Path.Combine(root, ".mcp.json"), "{\"mcpServers\":{\"docs\":{\"command\":\"docs\"}}}");
        harness.FileSystem.AddFile(Path.Combine(root, ".codai", "skills", "audit", "SKILL.md"), "Audit changes.");
        var catalog = new McpManifestCatalog(harness.FileSystem, global);
        var skills = new Codai.Core.Customization.SkillCatalog(harness.FileSystem, global);
        var server = (await catalog.LoadAsync(root, [])).Single();

        await Task.WhenAll(catalog.SetEnabledAsync(root, server, true), skills.SaveEnabledAsync(root, []));

        (await catalog.LoadAsync(root, [])).Single().Enabled.Should().BeTrue();
        (await skills.LoadAllAsync(root)).Single().IsEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"customization\":{\"mcp\":[]}}", "{}")]
    [InlineData("{}", "{\"bad\":{\"docs\":{\"enabled\":true}}}")]
    public async Task Malformed_selection_migration_preserves_legacy_and_user_files(string settings, string approvals)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-selection-malformed", "repo"));
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-selection-malformed-home", ".codai"));
        var harness = new SessionHarness(root);
        var file = Path.Combine(global, ".codai.json");
        var legacy = Path.Combine(global, "mcp-approvals.json");
        harness.FileSystem.AddFile(file, settings);
        harness.FileSystem.AddFile(legacy, approvals);
        var catalog = new McpManifestCatalog(harness.FileSystem, global);

        Func<Task> load = async () => await catalog.LoadAsync(root, []);
        await load.Should().ThrowAsync<IOException>();

        (await harness.FileSystem.ReadAllTextAsync(file)).Should().Be(settings);
        (await harness.FileSystem.ReadAllTextAsync(legacy)).Should().Be(approvals);
    }

    private sealed class RecordingDiscoverer : IMcpToolDiscoverer
    {
        public IReadOnlyList<McpServerOptions> Servers { get; private set; } = [];

        public Task<IReadOnlyList<McpToolDefinition>> DiscoverAsync(IEnumerable<McpServerOptions> servers, string projectRoot,
            CancellationToken cancellationToken = default)
        {
            Servers = servers.ToArray();
            return Task.FromResult<IReadOnlyList<McpToolDefinition>>([]);
        }
    }
}
