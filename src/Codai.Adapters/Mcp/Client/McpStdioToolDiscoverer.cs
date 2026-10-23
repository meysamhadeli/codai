using System.Collections;
using System.Reflection;
using System.Text.Json;
using Codai.Configuration;

namespace Codai.Mcp.Client;

/// <summary>Discovers tools from stdio MCP servers using the official SDK.</summary>
public sealed class McpStdioToolDiscoverer : IMcpToolDiscoverer
{
    private const string EmptySchema = """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

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
                var remoteName = RequiredString(tool, "Name");
                var prefix = string.IsNullOrWhiteSpace(server.ToolPrefix)
                    ? "mcp_" + Sanitize(server.Name) + "_"
                    : server.ToolPrefix;

                tools.Add(new McpToolDefinition(
                    prefix + Sanitize(remoteName),
                    Description(server.Name, remoteName, OptionalString(tool, "Description")),
                    Schema(tool),
                    server.ReadOnly,
                    !IsAutoApproved(server, prefix + Sanitize(remoteName)),
                    (arguments, _, ct) => InvokeToolWithTimeoutAsync(tool, arguments, server.TimeoutSeconds, ct)));
            }
        }

        return tools;
    }

    private static async Task<string> InvokeToolWithTimeoutAsync(
        object tool,
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

    private static async Task<object> CreateClientAsync(
        McpServerOptions server,
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var optionsType = Type.GetType("ModelContextProtocol.Client.StdioClientTransportOptions, ModelContextProtocol", throwOnError: true)!;
        var transportType = Type.GetType("ModelContextProtocol.Client.StdioClientTransport, ModelContextProtocol", throwOnError: true)!;
        var factoryType = Type.GetType("ModelContextProtocol.Client.McpClientFactory, ModelContextProtocol", throwOnError: true)!;

        var options = Activator.CreateInstance(optionsType)!;
        Set(options, "Name", server.Name);
        Set(options, "Command", server.Command);
        Set(options, "Arguments", server.Args);

        if (!string.IsNullOrWhiteSpace(server.WorkingDirectory))
        {
            Set(options, "WorkingDirectory", Path.GetFullPath(server.WorkingDirectory, projectRoot));
        }

        if (server.Environment.Count > 0)
        {
            Set(
                options,
                "EnvironmentVariables",
                server.Environment.ToDictionary(
                    pair => pair.Key,
                    pair => ExpandEnvironmentReference(pair.Value),
                    StringComparer.OrdinalIgnoreCase));
        }

        var transport = Activator.CreateInstance(transportType, options)!;
        var create = factoryType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "CreateAsync")
            .OrderBy(method => method.GetParameters().Length)
            .FirstOrDefault(method => method.GetParameters().Length > 0 &&
                method.GetParameters()[0].ParameterType.IsAssignableFrom(transportType))
            ?? throw new MissingMethodException(factoryType.FullName, "CreateAsync");

        var result = create.Invoke(null, Arguments(create, transport, cancellationToken))
            ?? throw new InvalidOperationException("MCP client factory returned null.");

        return await AwaitObjectAsync(result).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<object>> ListToolsAsync(object client, CancellationToken cancellationToken)
    {
        var method = client.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(candidate => candidate.Name == "ListToolsAsync")
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new MissingMethodException(client.GetType().FullName, "ListToolsAsync");

        var result = method.Invoke(client, Arguments(method, cancellationToken))
            ?? throw new InvalidOperationException("MCP client returned no tool list.");

        var value = await AwaitObjectAsync(result).ConfigureAwait(false);
        if (value is not IEnumerable enumerable)
        {
            throw new InvalidOperationException("MCP ListToolsAsync result was not enumerable.");
        }

        return enumerable.Cast<object>().ToArray();
    }

    private static async Task<string> InvokeToolAsync(
        object tool,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var method = tool.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(candidate => candidate.Name is "InvokeAsync" or "CallAsync")
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new MissingMethodException(tool.GetType().FullName, "InvokeAsync");

        var result = method.Invoke(tool, Arguments(method, arguments, cancellationToken))
            ?? throw new InvalidOperationException("MCP tool returned null.");

        var value = await AwaitObjectAsync(result).ConfigureAwait(false);
        return value switch
        {
            null => string.Empty,
            string text => text,
            JsonElement json => json.GetRawText(),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static object?[] Arguments(MethodBase method, object first, CancellationToken cancellationToken)
    {
        var parameters = method.GetParameters();
        var values = new object?[parameters.Length];
        values[0] = first;

        for (var i = 1; i < parameters.Length; i++)
        {
            values[i] = ValueFor(parameters[i], cancellationToken);
        }

        return values;
    }

    private static object?[] Arguments(MethodBase method, CancellationToken cancellationToken) =>
        [.. method.GetParameters().Select(parameter => ValueFor(parameter, cancellationToken))];

    private static object?[] Arguments(MethodBase method, JsonElement arguments, CancellationToken cancellationToken)
    {
        var values = new object?[method.GetParameters().Length];
        var parameters = method.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(JsonElement))
            {
                values[i] = arguments;
            }
            else if (parameters[i].ParameterType == typeof(string))
            {
                values[i] = arguments.GetRawText();
            }
            else if (typeof(IDictionary).IsAssignableFrom(parameters[i].ParameterType) ||
                parameters[i].ParameterType.Name.Contains("Arguments", StringComparison.Ordinal))
            {
                values[i] = ToDictionary(arguments);
            }
            else
            {
                values[i] = ValueFor(parameters[i], cancellationToken);
            }
        }

        return values;
    }

    private static object? ValueFor(ParameterInfo parameter, CancellationToken cancellationToken)
    {
        if (parameter.ParameterType == typeof(CancellationToken))
        {
            return cancellationToken;
        }

        if (parameter.HasDefaultValue)
        {
            return parameter.DefaultValue;
        }

        return parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
    }

    private static async Task<object> AwaitObjectAsync(object result)
    {
        if (result is not Task task)
        {
            return result;
        }

        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")?.GetValue(task)
            ?? throw new InvalidOperationException("MCP async operation completed without a result.");
    }

    private static void Set(object instance, string name, object? value)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property is not null && property.CanWrite)
        {
            property.SetValue(instance, value);
        }
    }

    private static string RequiredString(object value, string property) =>
        OptionalString(value, property) ?? throw new InvalidOperationException($"MCP tool has no {property}.");

    private static string? OptionalString(object value, string property) =>
        value.GetType().GetProperty(property)?.GetValue(value)?.ToString();

    private static string Schema(object tool)
    {
        foreach (var propertyName in new[] { "JsonSchema", "InputSchema", "ParametersSchema", "Schema" })
        {
            var value = tool.GetType().GetProperty(propertyName)?.GetValue(tool);
            switch (value)
            {
                case null:
                    continue;
                case JsonElement json:
                    return json.GetRawText();
                case string text when !string.IsNullOrWhiteSpace(text):
                    return text;
                default:
                    var stringValue = value.ToString();
                    if (!string.IsNullOrWhiteSpace(stringValue))
                    {
                        return stringValue;
                    }

                    break;
            }
        }

        return EmptySchema;
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
