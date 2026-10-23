using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Mcp.Client;

/// <summary>Discovers and invokes capabilities through remote MCP HTTP endpoints.</summary>
public sealed class McpRemoteToolDiscoverer(HttpClient? httpClient = null) : IMcpToolDiscoverer
{
    private const int MaxResponseBytes = 1_048_576;
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    public async Task<IReadOnlyList<McpToolDefinition>> DiscoverAsync(
        IEnumerable<McpServerOptions> servers,
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servers);
        var result = new List<McpToolDefinition>();
        foreach (var server in servers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(server.Transport, "stdio", StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(server.Transport, "streamableHttp", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(server.Transport, "http", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(server.Transport, "sse", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"MCP server '{server.Name}' uses unsupported transport '{server.Transport}'.");
            }

            using var timeout = CreateTimeout(server, cancellationToken);
            var connection = await McpRemoteConnection.ConnectAsync(_httpClient, server, timeout.Token).ConfigureAwait(false);
            result.AddRange(await connection.ListToolsAsync(timeout.Token).ConfigureAwait(false));
            result.AddRange(await connection.ListResourceToolsAsync(timeout.Token).ConfigureAwait(false));
            result.AddRange(await connection.ListPromptToolsAsync(timeout.Token).ConfigureAwait(false));
        }

        return result;
    }

    private static CancellationTokenSource CreateTimeout(McpServerOptions server, CancellationToken cancellationToken)
    {
        if (server.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException($"MCP server '{server.Name}' timeout_seconds must be greater than zero.");
        }

        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(server.TimeoutSeconds));
        return timeout;
    }

    private sealed class McpRemoteConnection
    {
        private readonly HttpClient _client;
        private readonly McpServerOptions _server;
        private readonly string _url;
        private string? _sessionId;
        private string? _authorization;
        private int _requestId;

        private McpRemoteConnection(HttpClient client, McpServerOptions server, string url)
        {
            _client = client;
            _server = server;
            _url = url;
        }

        public static async Task<McpRemoteConnection> ConnectAsync(
            HttpClient client, McpServerOptions server, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(server.Name) || !Uri.TryCreate(server.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            {
                throw new InvalidOperationException($"MCP server '{server.Name}' needs a public HTTP(S) url without credentials.");
            }

            var connection = new McpRemoteConnection(client, server, uri.AbsoluteUri)
            {
                _authorization = await McpOAuthAuthenticator.GetAccessTokenAsync(server, cancellationToken).ConfigureAwait(false),
            };
            await connection.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }

        public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken)
        {
            using var result = await CallAsync("tools/list", new { }, null, cancellationToken).ConfigureAwait(false);
            var definitions = new List<McpToolDefinition>();
            if (!result.RootElement.TryGetProperty("tools", out var tools)) return definitions;
            foreach (var tool in tools.EnumerateArray())
            {
                var remoteName = tool.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(remoteName)) continue;
                var name = Prefix(Sanitize(remoteName));
                var description = tool.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString() ?? remoteName : remoteName;
                var schema = tool.TryGetProperty("inputSchema", out var schemaElement)
                    ? schemaElement.GetRawText() : "{\"type\":\"object\",\"properties\":{}}";
                definitions.Add(new McpToolDefinition(name, $"{_server.Name}: {description}", schema,
                    _server.ReadOnly, !IsAutoApproved(name),
                    (arguments, context, ct) => CallToolWithTimeoutAsync(remoteName, arguments, context, ct)));
            }

            return definitions;
        }

        public async Task<IReadOnlyList<McpToolDefinition>> ListResourceToolsAsync(CancellationToken cancellationToken)
        {
            using var result = await CallAsync("resources/list", new { }, null, cancellationToken).ConfigureAwait(false);
            var definitions = new List<McpToolDefinition>();
            if (!result.RootElement.TryGetProperty("resources", out var resources)) return definitions;
            foreach (var resource in resources.EnumerateArray())
            {
                var uri = resource.TryGetProperty("uri", out var uriElement) ? uriElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(uri)) continue;
                var name = Prefix("resource_" + Sanitize(uri));
                var description = resource.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString() ?? uri : uri;
                definitions.Add(new McpToolDefinition(name, $"{_server.Name} resource: {description}",
                    "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}", true,
                    !IsAutoApproved(name), (_, _, ct) => ReadResourceAsync(uri, ct)));
            }

            return definitions;
        }

        public async Task<IReadOnlyList<McpToolDefinition>> ListPromptToolsAsync(CancellationToken cancellationToken)
        {
            using var result = await CallAsync("prompts/list", new { }, null, cancellationToken).ConfigureAwait(false);
            var definitions = new List<McpToolDefinition>();
            if (!result.RootElement.TryGetProperty("prompts", out var prompts)) return definitions;
            foreach (var prompt in prompts.EnumerateArray())
            {
                var promptName = prompt.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(promptName)) continue;
                var name = Prefix("prompt_" + Sanitize(promptName));
                var description = prompt.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString() ?? promptName : promptName;
                definitions.Add(new McpToolDefinition(name, $"{_server.Name} prompt: {description}",
                    "{\"type\":\"object\",\"properties\":{\"arguments\":{\"type\":\"object\"}},\"additionalProperties\":false}", true,
                    !IsAutoApproved(name), (arguments, _, ct) => GetPromptAsync(promptName, arguments, ct)));
            }

            return definitions;
        }

        private async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await CallAsync("initialize", new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { },
                clientInfo = new { name = "codai", version = "1.0" },
            }, null, cancellationToken, false).ConfigureAwait(false);
            await NotifyAsync("notifications/initialized", cancellationToken).ConfigureAwait(false);
        }

        private string Prefix(string suffix) =>
            (string.IsNullOrWhiteSpace(_server.ToolPrefix) ? "mcp_" + Sanitize(_server.Name) + "_" : _server.ToolPrefix) + suffix;

        private bool IsAutoApproved(string name) =>
            _server.AutoApprove.Any(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));

        private async Task<string> CallToolWithTimeoutAsync(string name, JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
        {
            using var timeout = CreateTimeout(_server, cancellationToken);
            using var result = await CallAsync("tools/call", new { name, arguments }, context, timeout.Token).ConfigureAwait(false);
            return result.RootElement.TryGetProperty("content", out var content) ? content.GetRawText() : result.RootElement.GetRawText();
        }

        private async Task<string> ReadResourceAsync(string uri, CancellationToken cancellationToken)
        {
            using var timeout = CreateTimeout(_server, cancellationToken);
            using var result = await CallAsync("resources/read", new { uri }, null, timeout.Token).ConfigureAwait(false);
            return result.RootElement.GetRawText();
        }

        private async Task<string> GetPromptAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
        {
            var values = arguments.TryGetProperty("arguments", out var nested) ? nested : arguments;
            using var timeout = CreateTimeout(_server, cancellationToken);
            using var result = await CallAsync("prompts/get", new { name, arguments = values }, null, timeout.Token).ConfigureAwait(false);
            return result.RootElement.GetRawText();
        }

        private async Task<JsonDocument> CallAsync(string method, object parameters, ToolContext? context,
            CancellationToken cancellationToken, bool allowReconnect = true)
        {
            var request = new { jsonrpc = "2.0", id = Interlocked.Increment(ref _requestId), method, @params = parameters };
            using var message = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
            };
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            AddHeaders(message);

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (allowReconnect && method != "initialize")
            {
                await InitializeAsync(cancellationToken).ConfigureAwait(false);
                return await CallAsync(method, parameters, context, cancellationToken, false).ConfigureAwait(false);
            }

            using (response)
            {
                if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIds)) _sessionId = sessionIds.FirstOrDefault();
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (bytes.Length > MaxResponseBytes) throw new InvalidOperationException("MCP response exceeded the 1 MB limit.");
                var messages = ExtractMessages(Encoding.UTF8.GetString(bytes)).ToArray();
                foreach (var notification in messages.SkipLast(1))
                {
                    if (notification.RootElement.TryGetProperty("method", out var methodElement) &&
                        methodElement.GetString() == "notifications/progress" && context is not null)
                    {
                        context.Observer.OnToolOutput(_server.Name, new ProcessOutput(notification.RootElement.GetRawText(), false));
                    }
                    notification.Dispose();
                }

                using var document = messages.Length == 0 ? throw new InvalidOperationException("MCP response contained no JSON message.") : messages[^1];
                if (document.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException($"MCP '{method}' failed: {error.GetRawText()}");
                return JsonDocument.Parse(document.RootElement.GetProperty("result").GetRawText());
            }
        }

        private async Task NotifyAsync(string method, CancellationToken cancellationToken)
        {
            var request = new { jsonrpc = "2.0", method, @params = new { } };
            using var message = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
            };
            AddHeaders(message);
            using var response = await _client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        private void AddHeaders(HttpRequestMessage message)
        {
            foreach (var header in _server.Headers)
            {
                if (!string.IsNullOrWhiteSpace(header.Key) && header.Value is not null)
                    message.Headers.TryAddWithoutValidation(header.Key, Expand(header.Value));
            }
            if (!string.IsNullOrWhiteSpace(_sessionId)) message.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
            if (!string.IsNullOrWhiteSpace(_authorization)) message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _authorization);
        }

        private static IEnumerable<JsonDocument> ExtractMessages(string text)
        {
            if (!text.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                yield return JsonDocument.Parse(text);
                yield break;
            }
            foreach (var line in text.Split('\n'))
            {
                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && line[5..].Trim() is { Length: > 0 } data)
                {
                    yield return JsonDocument.Parse(data);
                }
            }
        }

        private static string Expand(string value) => value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}')
            ? Environment.GetEnvironmentVariable(value[2..^1]) ?? string.Empty : value;

        private static string Sanitize(string value) => new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());

        private static CancellationTokenSource CreateTimeout(McpServerOptions server, CancellationToken cancellationToken)
        {
            if (server.TimeoutSeconds <= 0) throw new InvalidOperationException($"MCP server '{server.Name}' timeout_seconds must be greater than zero.");
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(server.TimeoutSeconds));
            return timeout;
        }
    }
}
