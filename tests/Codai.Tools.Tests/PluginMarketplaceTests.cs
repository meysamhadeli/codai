using System.IO.Compression;
using System.Text.Json;
using Codai.Tools.Plugins;
using Codai.Core.Customization;
using Codai.Core.IO;
using Codai.Mcp.Client;
using FluentAssertions;

namespace Codai.Tools.Tests;

public sealed class PluginMarketplaceTests
{
    [Fact]
    public async Task InstallAsync_fetches_repository_subdirectory_and_accepts_nested_manifest()
    {
        using var root = new TemporaryDirectory();
        using var archiveBytes = new MemoryStream();
        using (var archive = new ZipArchive(archiveBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using var manifest = new StreamWriter(archive.CreateEntry("repo-main/plugins/reviewer/.claude-plugin/plugin.json").Open());
            await manifest.WriteAsync("{\"name\":\"reviewer\"}");
        }
        using var http = new HttpClient(new ArchiveHandler(archiveBytes.ToArray()));
        var catalog = Path.Combine(root.Path, "marketplace.json");
        await File.WriteAllTextAsync(catalog, """
            {"name":"community","plugins":[{"name":"reviewer","source":{"source":"github","repo":"example/repo","path":"plugins/reviewer"}}]}
            """);
        var installed = await new PluginMarketplaceManager(httpClient: http).InstallAsync(catalog, "reviewer", root.Path);
        File.Exists(Path.Combine(installed, ".claude-plugin", "plugin.json")).Should().BeTrue();
        Directory.Exists(Path.Combine(root.Path, ".codai", "plugin-staging")).Should().BeTrue();
        Directory.EnumerateDirectories(Path.Combine(root.Path, ".codai", "plugin-staging")).Should().BeEmpty();
    }

    private sealed class ArchiveHandler(byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.Host.Should().Be("codeload.github.com");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        }
    }

    [Fact]
    public async Task Installed_bundle_contributes_namespaced_skills_and_unapproved_Mcp_servers()
    {
        using var root = new TemporaryDirectory();
        var plugin = Path.Combine(root.Path, "source", "reviewer");
        Directory.CreateDirectory(Path.Combine(plugin, "skills", "review"));
        await File.WriteAllTextAsync(Path.Combine(plugin, "plugin.json"), "{\"name\":\"reviewer\"}");
        await File.WriteAllTextAsync(Path.Combine(plugin, "skills", "review", "SKILL.md"), "---\ndescription: Review changes\n---\nUse the review checklist.");
        Directory.CreateDirectory(Path.Combine(plugin, "commands"));
        await File.WriteAllTextAsync(Path.Combine(plugin, "commands", "audit.md"), "Audit $ARGUMENTS using ${CLAUDE_PLUGIN_ROOT}.");
        await File.WriteAllTextAsync(Path.Combine(plugin, ".mcp.json"), "{\"mcpServers\":{\"docs\":{\"type\":\"http\",\"url\":\"https://mcp.context7.com/mcp?client=claude-code-plugin\"}}}");
        var catalog = Path.Combine(root.Path, "marketplace.json");
        await File.WriteAllTextAsync(catalog, JsonSerializer.Serialize(new { name = "community", plugins = new[] { new { name = "reviewer", source = plugin } } }));
        await new PluginMarketplaceManager().InstallAsync(catalog, "reviewer", root.Path);
        var skills = await new SkillCatalog(PhysicalFileSystem.Instance).LoadAsync(root.Path);
        skills.Should().ContainSingle().Which.Name.Should().Be("reviewer:review");
        (await new SkillCatalog(PhysicalFileSystem.Instance).FindAsync(root.Path, "reviewer:review")).Should().NotBeNull();
        var command = await new CommandCatalog(PhysicalFileSystem.Instance).FindAsync(root.Path, "reviewer:audit");
        command.Should().NotBeNull();
        command!.Body.Should().NotContain("${CLAUDE_PLUGIN_ROOT}");
        var servers = await new McpManifestCatalog(PhysicalFileSystem.Instance).LoadAsync(root.Path, []);
        servers.Should().ContainSingle().Which.Name.Should().Be("reviewer/docs");
        servers[0].Enabled.Should().BeFalse();
        servers[0].Url.Should().Be("https://mcp.context7.com/mcp");
    }

    [Theory]
    [InlineData("github/copilot-plugins")]
    [InlineData("https://github.com/github/copilot-plugins")]
    [InlineData("https://github.com/github/copilot-plugins.git")]
    public async Task Repository_inputs_resolve_Copilot_catalog_location(string input)
    {
        using var root = new TemporaryDirectory();
        using var http = new HttpClient(new MarketplaceHandler());
        var manager = new PluginMarketplaceManager(httpClient: http);
        var source = await manager.ResolveSourceAsync(input, root.Path);
        source.Should().Be("https://raw.githubusercontent.com/github/copilot-plugins/HEAD/.github/plugin/marketplace.json");
    }

    private sealed class MarketplaceHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"name\":\"copilot-plugins\",\"plugins\":[]}"),
            });
    }

    [Theory]
    [InlineData(".github/plugin")]
    [InlineData(".claude-plugin")]
    [InlineData("")]
    public async Task Local_repository_resolves_catalog_and_relative_bundle_source(string layout)
    {
        using var root = new TemporaryDirectory();
        var repository = Path.Combine(root.Path, "custom-repo");
        var plugin = Path.Combine(repository, "reviewer");
        Directory.CreateDirectory(plugin);
        var catalogDirectory = Path.Combine(repository, layout);
        Directory.CreateDirectory(catalogDirectory);
        await File.WriteAllTextAsync(Path.Combine(plugin, "plugin.json"), "{\"name\":\"reviewer\"}");
        await File.WriteAllTextAsync(Path.Combine(catalogDirectory, "marketplace.json"),
            "{\"name\":\"custom-local\",\"plugins\":[{\"name\":\"reviewer\",\"source\":\"./reviewer\"}]}");
        var manager = new PluginMarketplaceManager();
        var source = await manager.ResolveSourceAsync(repository, root.Path);
        var installed = await manager.InstallAsync(source, "reviewer", root.Path);
        File.Exists(Path.Combine(installed, "plugin.json")).Should().BeTrue();
    }

    [Fact]
    public async Task DiscoverAsync_reads_Claude_style_marketplace_metadata_without_loading_code()
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "marketplace.json");
        await File.WriteAllTextAsync(source, """
            {
              "name": "community-tools",
              "description": "Community plugins",
              "owner": { "name": "Codai contributors" },
              "plugins": [
                { "name": "reviewer", "description": "Review changes", "version": "1.2.0", "source": "./reviewer" }
              ]
            }
            """);

        var entries = await new PluginMarketplaceManager().DiscoverAsync(source, root.Path);

        entries.Should().ContainSingle().Which.Should().Match<PluginMarketplaceEntry>(entry =>
            entry.Name == "reviewer" && entry.Version == "1.2.0" && entry.Description == "Review changes");
    }

    [Fact]
    public async Task DiscoverAsync_rejects_missing_plugin_lists()
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "marketplace.json");
        await File.WriteAllTextAsync(source, "{\"name\":\"community-tools\"}");
        var action = () => new PluginMarketplaceManager().DiscoverAsync(source, root.Path);
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public async Task DiscoverAsync_rejects_special_directory_names(string name)
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "marketplace.json");
        await File.WriteAllTextAsync(source, JsonSerializer.Serialize(new { name, plugins = Array.Empty<object>() }));
        var action = () => new PluginMarketplaceManager().DiscoverAsync(source, root.Path);
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task DiscoverAsync_rejects_path_traversal_marketplace_name()
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "marketplace.json");
        await File.WriteAllTextAsync(source, "{\"name\":\"../official\",\"plugins\":[]}");

        var action = () => new PluginMarketplaceManager().DiscoverAsync(source, root.Path);

        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task InstallAsync_copies_manifested_local_plugin_directory()
    {
        using var root = new TemporaryDirectory();
        var plugin = Path.Combine(root.Path, "reviewer");
        Directory.CreateDirectory(plugin);
        await File.WriteAllTextAsync(Path.Combine(plugin, "plugin.json"), "{\"name\":\"reviewer\"}");
        var source = Path.Combine(root.Path, "marketplace.json");
        await File.WriteAllTextAsync(source, $"{{\"name\":\"community-tools\",\"plugins\":[{{\"name\":\"reviewer\",\"source\":{JsonSerializer.Serialize(plugin)}}}]}}");

        var installed = await new PluginMarketplaceManager().InstallAsync(source, "reviewer", root.Path);

        File.Exists(Path.Combine(installed, "plugin.json")).Should().BeTrue();
    }

    [Fact]
    public async Task InstallAsync_rejects_zip_path_traversal()
    {
        using var root = new TemporaryDirectory();
        var zip = Path.Combine(root.Path, "reviewer.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            await using var writer = new StreamWriter(archive.CreateEntry("../escape.txt").Open());
            await writer.WriteAsync("unsafe");
        }
        var source = Path.Combine(root.Path, "marketplace.json");
        await File.WriteAllTextAsync(source, $"{{\"name\":\"community-tools\",\"plugins\":[{{\"name\":\"reviewer\",\"source\":{JsonSerializer.Serialize(zip)}}}]}}");

        var action = () => new PluginMarketplaceManager().InstallAsync(source, "reviewer", root.Path);

        await action.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Path.Combine(root.Path, "escape.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Failed_replacement_preserves_existing_bundle_and_cleans_staging()
    {
        using var root = new TemporaryDirectory();
        var plugin = Path.Combine(root.Path, "reviewer");
        Directory.CreateDirectory(plugin);
        var manifest = Path.Combine(plugin, "plugin.json");
        await File.WriteAllTextAsync(manifest, "{\"name\":\"reviewer\"}");
        var catalog = Path.Combine(root.Path, "marketplace.json");
        await File.WriteAllTextAsync(catalog, JsonSerializer.Serialize(new { name = "community", plugins = new[] { new { name = "reviewer", source = plugin } } }));
        var manager = new PluginMarketplaceManager();
        var installed = await manager.InstallAsync(catalog, "reviewer", root.Path);
        await File.WriteAllTextAsync(manifest, "{\"name\":\"wrong-name\"}");

        var replace = () => manager.InstallAsync(catalog, "reviewer", root.Path, overwrite: true);

        await replace.Should().ThrowAsync<InvalidDataException>();
        (await File.ReadAllTextAsync(Path.Combine(installed, "plugin.json"))).Should().Contain("reviewer");
        Directory.EnumerateDirectories(Path.Combine(root.Path, ".codai", "plugin-staging")).Should().BeEmpty();
    }

    [Fact]
    public async Task InstallAsync_rejects_archive_symbolic_links()
    {
        using var root = new TemporaryDirectory();
        var zip = Path.Combine(root.Path, "reviewer.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("linked-file");
            entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("../outside");
        }
        var catalog = Path.Combine(root.Path, "marketplace.json");
        await File.WriteAllTextAsync(catalog, JsonSerializer.Serialize(new { name = "community", plugins = new[] { new { name = "reviewer", source = zip } } }));

        var install = () => new PluginMarketplaceManager().InstallAsync(catalog, "reviewer", root.Path);

        await install.Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(Path.Combine(root.Path, ".codai", "plugins", "reviewer")).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Installed_receipts_reject_escaping_contributions_and_ignore_disabled_bundles(bool enabled)
    {
        using var root = new TemporaryDirectory();
        var plugin = Path.Combine(root.Path, ".codai", "plugins", "reviewer");
        Directory.CreateDirectory(plugin);
        await File.WriteAllTextAsync(Path.Combine(plugin, InstalledPluginCatalog.ReceiptFileName), JsonSerializer.Serialize(new
        {
            name = "reviewer", enabled, skills = new[] { "../outside" },
        }));
        var load = () => InstalledPluginCatalog.LoadAsync(root.Path, PhysicalFileSystem.Instance);
        if (enabled)
        {
            await load.Should().ThrowAsync<IOException>();
        }
        else
        {
            (await load()).Should().BeEmpty();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codai-marketplace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; } = null!;

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}