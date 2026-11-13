using System.Net;
using System.Text;
using System.Text.Json;
using Codai.Cli.Ui;
using Codai.Core.Abstractions;
using Codai.Tools.Plugins;
using FluentAssertions;
using Spectre.Console;

namespace Codai.Session.Tests;

public sealed class PluginBrowserTests
{
    private static SessionMenuItem[] Items =>
    [
        new("context7", "Documentation lookup", "context7") { Group = "official" },
        new("tavily", "Web search", "tavily") { Group = "community" },
    ];

    [Theory]
    [InlineData(24, 10)]
    [InlineData(80, 24)]
    [InlineData(120, 35)]
    public void Browser_renders_search_tabs_rows_and_footer_within_viewport(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No });
        console.Profile.Width = width;
        console.Profile.Height = height;

        new PluginBrowser(Items).Draw(console);

        var output = System.Text.RegularExpressions.Regex.Replace(writer.ToString(), "\u001b\\[[0-9;?]*[a-zA-Z]", string.Empty);
        output.Should().Contain("Plugins").And.Contain("Search").And.NotContain("Errors").And.NotContain("Space to toggle");
        if (width >= 80) output.Should().Contain("Marketplaces").And.Contain("Tab to switch sections").And.Contain("Enter to install").And.Contain("Documentation lookup");
    }

    [Fact]
    public void Search_filters_real_rows_and_enter_installs_plugin()
    {
        var browser = new PluginBrowser(Items);
        Type(browser, "context7");
        browser.HandleKey(Key(ConsoleKey.Enter));
        browser.Command.Should().Be("@plugins:install:context7");
    }

    [Fact]
    public void Space_stays_in_search_and_control_s_filters_marketplace()
    {
        var browser = new PluginBrowser(Items);
        browser.HandleKey(new ConsoleKeyInfo('s', ConsoleKey.S, false, false, true));
        browser.HandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false));
        browser.Command.Should().BeNull();
        browser.Closed.Should().BeFalse();
        browser.Query.Should().Be(" ");
    }

    [Theory]
    [InlineData("Discover", ConsoleKey.Tab, "Installed")]
    [InlineData("Discover", ConsoleKey.LeftArrow, "Marketplaces")]
    [InlineData("Marketplaces", ConsoleKey.RightArrow, "Discover")]
    public void Tabs_navigate_in_place(string tab, ConsoleKey key, string expected)
    {
        var browser = new PluginBrowser(Items, tab);
        browser.HandleKey(Key(key));
        browser.Command.Should().Be("@plugins:tab:" + expected);
    }

    [Fact]
    public async Task Workflow_starts_with_default_catalog_and_keeps_tabs_after_install_cancellation()
    {
        var harness = new SessionHarness();
        using var http = new HttpClient(new CatalogHandler());
        harness.Ui.EnqueueSelection(items =>
        {
            items.Select(item => item.Label).Should().Contain("context7").And.Contain("tavily");
            items.Select(item => item.Group).Distinct().Should().BeEquivalentTo("claude-plugins-official", "copilot-plugins", "awesome-copilot");
            return "@plugins:install:0";
        });
        harness.Ui.EnqueueChoice("cancel");
        harness.Ui.EnqueueChoice("@plugins:tab:Installed");
        harness.Ui.EnqueueChoice("@plugins:tab:Marketplaces");
        harness.Ui.EnqueueSelection(items =>
        {
            items.Should().Contain(item => item.Label == "claude-plugins-official");
            return null;
        });
        await new SessionSettingsWorkflow(harness.Ui, harness.Inspector,
            new() { ProjectRoot = harness.Root, ConfigurationDirectory = harness.Root, Provider = "ollama", Model = "local" }, harness.FileSystem,
            pluginMarketplaces: new PluginMarketplaceManager(httpClient: http)).RunAsync("plugins");
        harness.Ui.Menus.Select(menu => menu.PluginTab).Should().Equal("Discover", "Discover", "Installed", "Marketplaces");
        harness.Ui.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData(null)]
    public async Task Automatic_approval_does_not_skip_plugin_publisher_trust(string? answer)
    {
        var harness = new SessionHarness();
        using var http = new HttpClient(new CatalogHandler());
        harness.Ui.EnqueueChoice("@plugins:install:0");
        harness.Ui.EnqueueSelection(items =>
        {
            items.Select(item => item.Label).Should().Equal("Install", "Cancel");
            items.Select(item => item.Value).Should().Equal("install", "cancel");
            return answer;
        });
        await new SessionSettingsWorkflow(new AutoApprovingUserInterface(harness.Ui, new ApprovalState(true)), harness.Inspector,
            new() { ProjectRoot = harness.Root, ConfigurationDirectory = harness.Root, Provider = "ollama", Model = "local" }, harness.FileSystem,
            pluginMarketplaces: new PluginMarketplaceManager(httpClient: http)).RunAsync("plugins");
        harness.Ui.Dialogs.Should().Contain(title => title.Contains("Trust this publisher", StringComparison.Ordinal));
        harness.Ui.ProgressLabels.Should().NotContain("Installing plugin");
        harness.Ui.Infos.Should().NotContain(message => message.StartsWith("Auto-approved:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trusted_install_enables_its_mcp_and_skills_without_enabling_other_contributions()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-plugin-selection-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "project");
        var global = Path.Combine(root, "private");
        var source = Path.Combine(root, "bundle");
        Directory.CreateDirectory(Path.Combine(source, "skills", "lookup"));
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(global);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(source, "plugin.json"), """{"name":"context7"}""");
            await File.WriteAllTextAsync(Path.Combine(source, ".mcp.json"), """{"mcpServers":{"context7":{"type":"http","url":"https://mcp.context7.com/mcp"}}}""");
            await File.WriteAllTextAsync(Path.Combine(source, "skills", "lookup", "SKILL.md"), "---\nname: lookup\ndescription: Look up documentation\n---\nUse documentation.\n");
            var marketplace = Path.Combine(root, "marketplace.json");
            await File.WriteAllTextAsync(marketplace, JsonSerializer.Serialize(new
            {
                name = "local", plugins = new[] { new { name = "context7", source } },
            }));
            Directory.CreateDirectory(Path.Combine(project, ".codai"));
            await File.WriteAllTextAsync(Path.Combine(project, ".codai", "plugin-marketplaces.json"), JsonSerializer.Serialize(new[] { marketplace }));
            var harness = new SessionHarness(project);
            using var http = new HttpClient(new CatalogHandler());
            var fileSystem = Codai.Core.IO.PhysicalFileSystem.Instance;
            var skills = new Codai.Core.Customization.SkillCatalog(fileSystem, global);
            var mcps = new Codai.Mcp.Client.McpManifestCatalog(fileSystem, global);
            Directory.CreateDirectory(Path.Combine(project, ".codai", "skills", "other"));
            await File.WriteAllTextAsync(Path.Combine(project, ".codai", "skills", "other", "SKILL.md"), "---\nname: other\ndescription: Another skill\n---\nOther guidance.\n");
            await skills.SaveEnabledAsync(project, []);
            await Codai.Core.Customization.UserCustomizationSettings.UpdateAsync(fileSystem, global, state =>
            {
                foreach (var selection in state["skills"]!.AsObject())
                    selection.Value!["disabled"]!.AsArray().Add("context7:lookup");
            });
            await File.WriteAllTextAsync(Path.Combine(project, ".mcp.json"), """{"mcpServers":{"other":{"type":"http","url":"https://example.com/mcp"}}}""");
            harness.Ui.EnqueueChoice("@plugins:install:6");
            harness.Ui.EnqueueChoice("install");
            await new SessionSettingsWorkflow(harness.Ui, harness.Inspector,
                new() { ProjectRoot = project, ConfigurationDirectory = project, GlobalConfigurationDirectory = global, Provider = "ollama", Model = "local" },
                fileSystem, skills, mcps, new PluginMarketplaceManager(httpClient: http)).RunAsync("plugins");

            harness.Ui.Errors.Should().BeEmpty();
            (await new Codai.Mcp.Client.McpManifestCatalog(fileSystem, global).LoadAsync(project, []))
                .Should().ContainSingle(server => server.Name == "context7/context7" && server.Enabled);
            (await new Codai.Core.Customization.SkillCatalog(fileSystem, global).LoadAllAsync(project))
                .Should().ContainSingle(skill => skill.Name == "context7:lookup" && skill.IsEnabled);
            (await skills.LoadAllAsync(project)).Should().ContainSingle(skill => skill.Name == "other" && !skill.IsEnabled);
            (await mcps.LoadAsync(project, [])).Should().ContainSingle(server => server.Name == "other" && !server.Enabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_bundle_is_reported_without_crashing_browser()
    {
        var harness = new SessionHarness();
        using var http = new HttpClient(new CatalogHandler());
        harness.Ui.EnqueueChoice("@plugins:install:0");
        harness.Ui.EnqueueChoice("install");
        await new SessionSettingsWorkflow(harness.Ui, harness.Inspector,
            new() { ProjectRoot = harness.Root, ConfigurationDirectory = harness.Root, Provider = "ollama", Model = "local" }, harness.FileSystem,
            pluginMarketplaces: new PluginMarketplaceManager(httpClient: http)).RunAsync("plugins");
        harness.Ui.Errors.Should().Contain("Could not install plugin bundle.");
        harness.Ui.Successes.Should().BeEmpty();
    }

    [Fact]
    public async Task Installed_plugin_enter_opens_uninstall_page_and_removes_bundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-plugin-uninstall-" + Guid.NewGuid().ToString("N"));
        var pluginPath = Path.Combine(root, ".codai", "plugins", "context7");
        Directory.CreateDirectory(pluginPath);
        try
        {
            var harness = new SessionHarness(root);
            using var http = new HttpClient(new CatalogHandler());
            harness.Ui.EnqueueChoice("@plugins:tab:Installed");
            harness.Ui.EnqueueSelection(items =>
            {
                items.Should().ContainSingle().Which.Label.Should().Be("context7");
                return "@plugins:view:" + pluginPath;
            });
            harness.Ui.EnqueueSelection(items =>
            {
                items.Select(item => item.Label).Should().Equal("Uninstall", "Cancel");
                items.Select(item => item.Value).Should().Equal("uninstall", "cancel");
                return "uninstall";
            });
            harness.Ui.EnqueueSelection(_ => null);

            await new SessionSettingsWorkflow(harness.Ui, harness.Inspector,
                new() { ProjectRoot = root, ConfigurationDirectory = root, Provider = "ollama", Model = "local" },
                Codai.Core.IO.PhysicalFileSystem.Instance,
                pluginMarketplaces: new PluginMarketplaceManager(httpClient: http)).RunAsync("plugins");

            Directory.Exists(pluginPath).Should().BeFalse();
            harness.Ui.Successes.Should().Contain("Plugin removed. Restart Codai to apply the change.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Installed_plugin_cancel_keeps_bundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-plugin-cancel-" + Guid.NewGuid().ToString("N"));
        var pluginPath = Path.Combine(root, ".codai", "plugins", "context7");
        Directory.CreateDirectory(pluginPath);
        try
        {
            var harness = new SessionHarness(root);
            using var http = new HttpClient(new CatalogHandler());
            harness.Ui.EnqueueChoice("@plugins:tab:Installed");
            harness.Ui.EnqueueChoice("@plugins:view:" + pluginPath);
            harness.Ui.EnqueueSelection(items =>
            {
                items.Select(item => item.Label).Should().Equal("Uninstall", "Cancel");
                return "cancel";
            });
            harness.Ui.EnqueueSelection(_ => null);

            await new SessionSettingsWorkflow(harness.Ui, harness.Inspector,
                new() { ProjectRoot = root, ConfigurationDirectory = root, Provider = "ollama", Model = "local" },
                Codai.Core.IO.PhysicalFileSystem.Instance,
                pluginMarketplaces: new PluginMarketplaceManager(httpClient: http)).RunAsync("plugins");

            Directory.Exists(pluginPath).Should().BeTrue();
            harness.Ui.Successes.Should().NotContain("Plugin removed. Restart Codai to apply the change.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_catalog_is_reported_as_notice_without_errors_tab()
    {
        var harness = new SessionHarness();
        using var http = new HttpClient(new CatalogHandler(fail: true));
        harness.Ui.EnqueueSelection(items =>
        {
            items.Should().BeEmpty();
            return null;
        });
        await new SessionSettingsWorkflow(harness.Ui, harness.Inspector,
            new() { ProjectRoot = harness.Root, ConfigurationDirectory = harness.Root, Provider = "ollama", Model = "local" }, harness.FileSystem,
            pluginMarketplaces: new PluginMarketplaceManager(httpClient: http)).RunAsync("plugins");
        harness.Ui.Errors.Should().HaveCount(3).And.OnlyContain(message => message.Contains("Could not load", StringComparison.Ordinal));
        harness.Ui.Menus.Should().OnlyContain(menu => menu.PluginTab != "Errors");
    }

    [Fact]
    public async Task Custom_repository_is_persisted_reopened_and_removed_without_changing_defaults()
    {
        var harness = new SessionHarness();
        using var http = new HttpClient(new CatalogHandler());
        var context = new Codai.Application.Inspection.ConfigurationInspectionContext
        {
            ProjectRoot = harness.Root, ConfigurationDirectory = harness.Root, Provider = "ollama", Model = "local",
        };
        var statePath = Path.Combine(harness.Root, ".codai", "plugin-marketplaces.json");
        harness.Ui.EnqueueChoice("@plugins:tab:Marketplaces");
        harness.Ui.EnqueueChoice("@plugins:view:add");
        harness.Ui.EnqueueValue("example/custom-marketplace");
        harness.Ui.EnqueueSelection(items =>
        {
            items.Should().Contain(item => item.Label == "custom-marketplace");
            return null;
        });
        await new SessionSettingsWorkflow(harness.Ui, harness.Inspector, context, harness.FileSystem,
            pluginMarketplaces: new PluginMarketplaceManager(httpClient: http)).RunAsync("plugins");
        var source = "https://raw.githubusercontent.com/example/custom-marketplace/HEAD/.github/plugin/marketplace.json";
        JsonSerializer.Deserialize<string[]>(await harness.FileSystem.ReadAllTextAsync(statePath)).Should().Equal(source);

        harness.Ui.EnqueueSelection(items =>
        {
            items.Should().Contain(item => item.Group == "custom-marketplace");
            return "@plugins:tab:Marketplaces";
        });
        harness.Ui.EnqueueChoice("@plugins:view:" + source);
        harness.Ui.EnqueueChoice("remove");
        harness.Ui.EnqueueConfirmation(true);
        harness.Ui.EnqueueSelection(items =>
        {
            items.Should().HaveCount(4).And.NotContain(item => item.Label == "custom-marketplace");
            return null;
        });
        await new SessionSettingsWorkflow(harness.Ui, harness.Inspector, context, harness.FileSystem,
            pluginMarketplaces: new PluginMarketplaceManager(httpClient: http)).RunAsync("plugins");
        JsonSerializer.Deserialize<string[]>(await harness.FileSystem.ReadAllTextAsync(statePath)).Should().BeEmpty();
    }

    [Fact]
    public void Crowded_marketplace_filters_keep_active_source_and_complete_count_visible()
    {
        var browser = new PluginBrowser([
            new("first", "", "0") { Group = "claude-plugins-official" },
            new("second", "", "1") { Group = "copilot-plugins" },
            new("third", "", "2") { Group = "awesome-copilot" },
            new("fourth", "", "3") { Group = "superpowers-marketplace" },
        ]);
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.No, Out = new AnsiConsoleOutput(output) });
        console.Profile.Width = 120;
        console.Profile.Height = 35;
        browser.Draw(console);
        output.ToString().Should().Contain("All 4  ·  4 marketplaces (ctrl+s to filter)");
        for (var index = 0; index < 4; index++) browser.HandleKey(new ConsoleKeyInfo('s', ConsoleKey.S, false, false, true));
        output.GetStringBuilder().Clear();
        browser.Draw(console);
        output.ToString().Should().Contain("superpowers-marketplace 1  ·  4 marketplaces (ctrl+s to filter)");
    }

    [Fact]
    public async Task Custom_marketplace_registration_creates_project_state_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-marketplace-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var harness = new SessionHarness(root);
            using var http = new HttpClient(new CatalogHandler());
            harness.Ui.EnqueueChoice("@plugins:tab:Marketplaces");
            harness.Ui.EnqueueChoice("@plugins:view:add");
            harness.Ui.EnqueueValue("example/custom-marketplace");
            await new SessionSettingsWorkflow(harness.Ui, harness.Inspector,
                new() { ProjectRoot = root, ConfigurationDirectory = root, Provider = "ollama", Model = "local" },
                Codai.Core.IO.PhysicalFileSystem.Instance,
                pluginMarketplaces: new PluginMarketplaceManager(httpClient: http)).RunAsync("plugins");
            File.Exists(Path.Combine(root, ".codai", "plugin-marketplaces.json")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_custom_registration_reports_notice_and_does_not_save_source()
    {
        var harness = new SessionHarness();
        using var http = new HttpClient(new CatalogHandler(fail: true));
        harness.Ui.EnqueueChoice("@plugins:tab:Marketplaces");
        harness.Ui.EnqueueChoice("@plugins:view:add");
        harness.Ui.EnqueueValue("example/unavailable");
        harness.Ui.EnqueueSelection(items =>
        {
            items.Should().ContainSingle().Which.Value.Should().Be("add");
            return null;
        });
        await new SessionSettingsWorkflow(harness.Ui, harness.Inspector,
            new() { ProjectRoot = harness.Root, ConfigurationDirectory = harness.Root, Provider = "ollama", Model = "local" }, harness.FileSystem,
            pluginMarketplaces: new PluginMarketplaceManager(httpClient: http)).RunAsync("plugins");
        harness.FileSystem.FileExists(Path.Combine(harness.Root, ".codai", "plugin-marketplaces.json")).Should().BeFalse();
        harness.Ui.Errors.Should().Contain(message => message.Contains("Could not add", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Default_marketplace_does_not_offer_removal()
    {
        var harness = new SessionHarness();
        using var http = new HttpClient(new CatalogHandler());
        harness.Ui.EnqueueChoice("@plugins:tab:Marketplaces");
        harness.Ui.EnqueueChoice("@plugins:view:" + PluginMarketplaceManager.CopilotMarketplaceSource);
        harness.Ui.EnqueueSelection(items =>
        {
            items.Should().ContainSingle().Which.Value.Should().Be("refresh");
            return null;
        });
        await new SessionSettingsWorkflow(harness.Ui, harness.Inspector,
            new() { ProjectRoot = harness.Root, ConfigurationDirectory = harness.Root, Provider = "ollama", Model = "local" }, harness.FileSystem,
            pluginMarketplaces: new PluginMarketplaceManager(httpClient: http)).RunAsync("plugins");
    }

    [Fact]
    public async Task Live_Copilot_and_custom_marketplaces_resolve_and_load_real_entries()
    {
        if (Environment.GetEnvironmentVariable("CODAI_RUN_PLUGIN_LIVE") != "1") return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var manager = new PluginMarketplaceManager();
        foreach (var repository in new[] { "github/copilot-plugins", "github/awesome-copilot", "obra/superpowers-marketplace" })
        {
            var source = await manager.ResolveSourceAsync(repository, Path.GetTempPath(), deadline.Token);
            var catalog = await manager.LoadAsync(source, Path.GetTempPath(), deadline.Token);
            catalog.Name.Should().Be(repository.Split('/')[1]);
            catalog.Plugins.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task Live_official_catalog_can_find_context7_and_tavily_in_browser()
    {
        if (Environment.GetEnvironmentVariable("CODAI_RUN_PLUGIN_LIVE") != "1") return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var catalog = await new PluginMarketplaceManager().LoadAsync(PluginMarketplaceManager.OfficialMarketplaceSource,
            Path.GetTempPath(), deadline.Token);
        catalog.Plugins.Should().Contain(plugin => plugin.Name == "context7").And.Contain(plugin => plugin.Name == "tavily");
        var items = catalog.Plugins.Select(plugin => new SessionMenuItem(plugin.Name, plugin.Description ?? "", plugin.Name)
        { Group = catalog.Name }).ToArray();
        foreach (var name in new[] { "context7", "tavily" })
        {
            var browser = new PluginBrowser(items);
            Type(browser, name);
            browser.HandleKey(Key(ConsoleKey.Enter));
            browser.Command.Should().Be("@plugins:install:" + name);
        }
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static void Type(PluginBrowser browser, string query)
    {
        foreach (var character in query) browser.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false));
    }

    private sealed class CatalogHandler(bool fail = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = request.RequestUri!.AbsolutePath.Contains("awesome-copilot", StringComparison.Ordinal) ? "awesome-copilot" :
                request.RequestUri.AbsolutePath.Contains("/github/copilot-plugins/", StringComparison.Ordinal) ? "copilot-plugins" :
                request.RequestUri.AbsolutePath.Contains("custom-marketplace", StringComparison.Ordinal) ? "custom-marketplace" : "claude-plugins-official";
            return Task.FromResult(new HttpResponseMessage(fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    name,
                    plugins = new[]
                    {
                        new { name = "context7", source = "./external_plugins/context7", description = "Documentation lookup" },
                        new { name = "tavily", source = "./tavily", description = "Web search" },
                    },
                }), Encoding.UTF8, "application/json"),
            });
        }
    }
}