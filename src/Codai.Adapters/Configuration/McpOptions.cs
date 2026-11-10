using Microsoft.Extensions.Configuration;

namespace Codai.Configuration;

/// <summary>Configuration for Model Context Protocol integration.</summary>
public sealed class McpOptions
{
    /// <summary>Configured local or remote MCP servers whose tools should be offered to Codai.</summary>
    [ConfigurationKeyName("servers")]
    public List<McpServerOptions> Servers { get; set; } = [];
}

/// <summary>A local or remote MCP server used by Codai for discovery and calls.</summary>
public sealed class McpServerOptions
{
    private string _transport = "stdio";

    /// <summary>Manifest path for a discovered server; null for native configuration.</summary>
    public string? DiscoverySourcePath { get; set; }

    /// <summary>Discovery scope used for source display.</summary>
    public string? DiscoveryScope { get; set; }

    /// <summary>Definition fingerprint bound to explicit private startup approval.</summary>
    public string? DiscoveryFingerprint { get; set; }

    /// <summary>Whether this server participates in the next host composition.</summary>
    [ConfigurationKeyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Stable server name used for display and generated tool names.</summary>
    [ConfigurationKeyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Effective transport; HTTP aliases normalize to <c>streamableHttp</c>. An explicit <see cref="Type"/> takes precedence.</summary>
    [ConfigurationKeyName("transport")]
    public string Transport
    {
        get => (Type ?? _transport).ToLowerInvariant() switch
        {
            "http" or "streamablehttp" or "streamable-http" => "streamableHttp",
            "stdio" => "stdio",
            "sse" => "sse",
            _ => Type ?? _transport,
        };
        set => _transport = value;
    }

    /// <summary>VS Code-compatible transport key; takes precedence over <see cref="Transport"/> when supplied.</summary>
    [ConfigurationKeyName("type")]
    public string? Type { get; set; }

    /// <summary>Optional gallery provenance metadata; does not trigger gallery requests or installation.</summary>
    [ConfigurationKeyName("gallery")]
    public string? Gallery { get; set; }

    /// <summary>Optional gallery version metadata; does not pin or install an executable version.</summary>
    [ConfigurationKeyName("version")]
    public string? Version { get; set; }

    /// <summary>Remote MCP endpoint used by HTTP transports.</summary>
    [ConfigurationKeyName("url")]
    public string? Url { get; set; }

    /// <summary>Executable to start, for example <c>npx</c>, <c>uvx</c> or <c>dotnet</c>.</summary>
    [ConfigurationKeyName("command")]
    public string Command { get; set; } = string.Empty;

    /// <summary>Arguments passed to <see cref="Command"/>.</summary>
    [ConfigurationKeyName("args")]
    public List<string> Args { get; set; } = [];

    /// <summary>Optional working directory. Relative paths are resolved from the project root.</summary>
    [ConfigurationKeyName("working_directory")]
    public string? WorkingDirectory { get; set; }

    /// <summary>Environment variables added to the server process.</summary>
    [ConfigurationKeyName("env")]
    public Dictionary<string, string?> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Headers sent to a remote MCP endpoint.</summary>
    [ConfigurationKeyName("headers")]
    public Dictionary<string, string?> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optional prefix for exposed tool names. Defaults to <c>mcp_{server}_</c>.</summary>
    [ConfigurationKeyName("tool_prefix")]
    public string? ToolPrefix { get; set; }

    /// <summary>
    /// Whether tools from this server are safe to call in read-only modes without approval.
    /// Defaults to false because Codai cannot prove what a remote server does.
    /// </summary>
    [ConfigurationKeyName("read_only")]
    public bool ReadOnly { get; set; }

    /// <summary>Exposed tool names that may run without an approval prompt.</summary>
    [ConfigurationKeyName("auto_approve")]
    public List<string> AutoApprove { get; set; } = [];

    /// <summary>Maximum seconds allowed for one MCP operation, including discovery.</summary>
    [ConfigurationKeyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>OAuth authorization endpoint opened in the user's browser.</summary>
    [ConfigurationKeyName("oauth_authorization_endpoint")]
    public string? OAuthAuthorizationEndpoint { get; set; }

    /// <summary>OAuth token endpoint used by the local callback exchange.</summary>
    [ConfigurationKeyName("oauth_token_endpoint")]
    public string? OAuthTokenEndpoint { get; set; }

    /// <summary>OAuth client identifier.</summary>
    [ConfigurationKeyName("oauth_client_id")]
    public string? OAuthClientId { get; set; }

    /// <summary>Environment variable containing the OAuth client secret.</summary>
    [ConfigurationKeyName("oauth_client_secret_environment")]
    public string? OAuthClientSecretEnvironment { get; set; }

    /// <summary>OAuth scopes requested during browser authorization.</summary>
    [ConfigurationKeyName("oauth_scopes")]
    public List<string> OAuthScopes { get; set; } = [];

    /// <summary>Local callback port for OAuth authorization.</summary>
    [ConfigurationKeyName("oauth_callback_port")]
    public int OAuthCallbackPort { get; set; } = 0;
}
