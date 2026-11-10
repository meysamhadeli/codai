using System.Text.Json;
using Codai.Core.Tools;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Tools;

internal sealed class ToolSearchAIFunction(IReadOnlyList<AIFunction> deferred, IList<AITool> selected) : AIFunction
{
    private int _searches;

    public override string Name => "tool_search";

    public override string Description =>
        "Discover relevant available MCP tools by task keywords or exact tool name. Their argument schemas " +
        "are deferred: search before calling them, then follow the returned schema. Search does not run tools " +
        "or grant permissions. Use concise keywords; if no match, try the exact name from this inventory:\n" +
        string.Join('\n', deferred.Select(tool => $"- {tool.Name}: {tool.Description}"));

    public override JsonElement JsonSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { query = new { type = "string", minLength = 1, maxLength = 512 } },
        required = new[] { "query" },
        additionalProperties = false,
    });

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++_searches > 12)
        {
            return ValueTask.FromResult<object?>("MCP discovery limit reached for this request. Use already discovered tools or explain the limitation.");
        }

        var payload = JsonSerializer.SerializeToElement(arguments.ToDictionary(argument => argument.Key, argument => argument.Value),
            AIJsonUtilities.DefaultOptions);
        if (!ToolArguments.TryGetString(payload, "query", out var query, out var error))
        {
            return ValueTask.FromResult<object?>(error);
        }
        if (string.IsNullOrWhiteSpace(query) || query.Length > 512)
        {
            return ValueTask.FromResult<object?>("query must contain 1 to 512 characters, including a non-whitespace character.");
        }

        var terms = query.Split([' ', '\t', '\r', '\n', ',', '.', ':', '/', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
        var matches = deferred.Select(tool => new
        {
            Tool = tool,
            Score = string.Equals(tool.Name, query.Trim(), StringComparison.OrdinalIgnoreCase) ? 1000 :
                terms.Sum(term => (tool.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ? 4 : 0) +
                    (tool.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0)),
        }).Where(match => match.Score > 0).OrderByDescending(match => match.Score)
            .ThenBy(match => match.Tool.Name, StringComparer.Ordinal).Take(5).ToArray();

        if (matches.Length == 0)
        {
            return ValueTask.FromResult<object?>("No matching available MCP tools. Try different keywords or an exact tool name; do not invent a tool or enable a server.");
        }

        foreach (var match in matches)
        {
            if (!selected.Any(tool => tool.Name == match.Tool.Name))
            {
                selected.Add(match.Tool);
            }
        }

        return ValueTask.FromResult<object?>(JsonSerializer.Serialize(matches.Select(match => new
        {
            name = match.Tool.Name,
            description = match.Tool.Description,
            inputSchema = match.Tool.JsonSchema,
        }), AIJsonUtilities.DefaultOptions));
    }
}
