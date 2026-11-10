using System.Text.Json;
using Codai.Configuration;
using ModelContextProtocol.Client;

namespace Codai.Mcp.Client;

/// <summary>Discovers tools from stdio MCP servers using the official SDK.</summary>
public sealed class McpStdioToolDiscoverer : IMcpToolDiscoverer
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<McpToolDefinition>> DiscoverAsync(
        IEnumerable<McpServerOptions> servers,
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var tools = new List<McpToolDefinition>();
        foreach (var server in servers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (server.Transport.Length > 0 && !string.Equals(server.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"MCP server '{server.Name}' uses unsupported transport '{server.Transport}'.");
            }

            if (string.IsNullOrWhiteSpace(server.Name))
            {
                throw new InvalidOperationException("Every MCP server needs a name.");
            }

            if (string.IsNullOrWhiteSpace(server.Command))
            {
                throw new InvalidOperationException($"MCP server '{server.Name}' needs a command.");
            }

            using var timeout = CreateTimeout(server, cancellationToken);
            var client = await CreateClientAsync(server, projectRoot, timeout.Token).ConfigureAwait(false);
            var serverTools = await ListToolsAsync(client, timeout.Token).ConfigureAwait(false);
            foreach (var tool in serverTools)
            {
                var remoteName = tool.Name;
                var prefix = string.IsNullOrWhiteSpace(server.ToolPrefix)
                    ? "mcp_" + Sanitize(server.Name) + "_"
                    : server.ToolPrefix;

                tools.Add(new McpToolDefinition(
                    prefix + Sanitize(remoteName),
                    Description(server.Name, remoteName, tool.Description),
                    tool.JsonSchema.GetRawText(),
                    server.ReadOnly,
                    !IsAutoApproved(server, prefix + Sanitize(remoteName)),
                    (arguments, _, ct) => InvokeToolWithTimeoutAsync(tool, arguments, server.TimeoutSeconds, ct)));
            }
        }

        return tools;
    }

    private static async Task<string> InvokeToolWithTimeoutAsync(
        McpClientTool tool,
        JsonElement arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeout(timeoutSeconds, cancellationToken);
        return await InvokeToolAsync(tool, arguments, timeout.Token).ConfigureAwait(false);
    }

    private static CancellationTokenSource CreateTimeout(McpServerOptions server, CancellationToken cancellationToken)
    {
        if (server.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException($"MCP server '{server.Name}' timeout_seconds must be greater than zero.");
        }

        return CreateTimeout(server.TimeoutSeconds, cancellationToken);
    }

    private static CancellationTokenSource CreateTimeout(int timeoutSeconds, CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return timeout;
    }

    private static bool IsAutoApproved(McpServerOptions server, string exposedName) =>
        server.AutoApprove.Any(name => string.Equals(name, exposedName, StringComparison.OrdinalIgnoreCase));

    private static async Task<McpClient> CreateClientAsync(
        McpServerOptions server,
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var options = new StdioClientTransportOptions
        {
            Name = server.Name,
            Command = server.Command,
            Arguments = server.Args,
            WorkingDirectory = string.IsNullOrWhiteSpace(server.WorkingDirectory)
                ? null : Path.GetFullPath(server.WorkingDirectory, projectRoot),
            EnvironmentVariables = server.Environment.ToDictionary(
                pair => pair.Key, pair => ExpandEnvironmentReference(pair.Value), StringComparer.OrdinalIgnoreCase),
        };
        return await McpClient.CreateAsync(new StdioClientTransport(options), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<McpClientTool>> ListToolsAsync(McpClient client, CancellationToken cancellationToken) =>
        (await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).ToArray();

    private static async Task<string> InvokeToolAsync(
        McpClientTool tool,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var result = await tool.CallAsync(ToDictionary(arguments), cancellationToken: cancellationToken).ConfigureAwait(false);
        var content = JsonSerializer.Serialize(result);
        if (result.IsError == true)
        {
            throw new InvalidOperationException(content);
        }
        return content;
    }

    private static Dictionary<string, object?> ToDictionary(JsonElement arguments)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return values;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            values[property.Name] = ToClrValue(property.Value);
        }

        return values;
    }

    private static object? ToClrValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDouble(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => value.EnumerateArray().Select(ToClrValue).ToArray(),
        JsonValueKind.Object => ToDictionary(value),
        _ => value.GetRawText(),
    };

    private static string Description(string server, string remoteName, string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? $"MCP tool '{remoteName}' from server '{server}'."
            : $"MCP server '{server}': {description}";

    private static string Sanitize(string value)
    {
        var chars = value.Trim().Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray();
        return new string(chars).Trim('_');
    }

    private static string? ExpandEnvironmentReference(string? value)
    {
        if (value is null || value.Length < 4 || !value.StartsWith("${", StringComparison.Ordinal) || !value.EndsWith('}'))
        {
            return value;
        }

        var variable = value[2..^1];
        return string.IsNullOrWhiteSpace(variable)
            ? value
            : Environment.GetEnvironmentVariable(variable) ?? string.Empty;
    }
}
