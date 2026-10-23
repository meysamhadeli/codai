using System.Text.Json;
using Codai.Core.Customization;
using Codai.Core.IO;
using Codai.Core.Tools;
using Codai.Tools;

namespace Codai.Application;

/// <summary>Activates conditional rules before foreground file tools execute.</summary>
public sealed class RequestRuleGuidance(SessionOptions options) : IToolGuidance
{
    private IReadOnlyList<ProjectRule> _rules = [];
    private readonly HashSet<ProjectRule> _activated = [];
    private int _remainingCharacters;

    /// <summary>Resets activation and guidance budgets for one user request.</summary>
    public void BeginRequest(IReadOnlyList<ProjectRule> rules, IReadOnlyList<string> referencedFiles)
    {
        _rules = rules;
        _activated.Clear();
        _activated.UnionWith(RuleSelector.Select(rules, referencedFiles));
        _remainingCharacters = Math.Max(0, RuleSelector.MaxTotalCharacters -
            (RuleSelector.Render(rules, referencedFiles)?.Length ?? 0));
    }

    /// <summary>Releases guidance when the foreground request completes or fails.</summary>
    public void EndRequest()
    {
        _rules = [];
        _activated.Clear();
        _remainingCharacters = 0;
    }

    /// <summary>Preserves reviewed rules when a provider conversation must be rebuilt.</summary>
    public string? RenderActivated() => RuleSelector.Render(
        _rules.Where(_activated.Contains).Select(rule => rule with { ApplyTo = [] }).ToArray(), []);

    /// <inheritdoc />
    public string? GetGuidance(ITool tool, JsonElement arguments)
    {
        if (tool is not IFileTargetTool || _rules.Count == 0 || arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var paths = new List<string>();
        if (arguments.TryGetProperty("paths", out var batch))
        {
            if (arguments.TryGetProperty("path", out _) || batch.ValueKind != JsonValueKind.Array ||
                batch.GetArrayLength() is < 1 or > ReadFileTool.MaxFileCount)
            {
                return null;
            }

            foreach (var entry in batch.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                paths.Add(entry.GetString()!);
            }
        }
        else if (arguments.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
        {
            paths.Add(path.GetString()!);
        }

        var relativePaths = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > ReadFileTool.MaxPathChars)
            {
                return null;
            }

            try
            {
                var resolved = ProjectPaths.ResolveInside(options.ProjectRoot, path);
                if (resolved is null)
                {
                    return null;
                }

                relativePaths.Add(Path.GetRelativePath(options.ProjectRoot, resolved).Replace('\\', '/'));
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                return null;
            }
        }

        var pending = RuleSelector.Select(_rules, relativePaths)
            .Where(rule => !_activated.Contains(rule)).ToArray();
        if (pending.Length == 0)
        {
            return null;
        }

        const string header = "Tool was not executed. Review these newly applicable rules, reconsider the call, then request it again if appropriate. Rules do not grant permissions or consent.\n\n";
        var maxCharacters = Math.Min(_remainingCharacters, ToolRunner.DefaultMaxResultChars) - header.Length;
        if (maxCharacters <= 0)
        {
            throw new InvalidOperationException("Conditional rule context budget exhausted; tool was not executed. Narrow the task or shorten project rules.");
        }

        var rendered = RuleSelector.Render(pending, relativePaths, maxCharacters)!;
        if (rendered.Length > maxCharacters)
        {
            throw new InvalidOperationException("Conditional rules exceed the remaining context budget; tool was not executed. Narrow the task or shorten project rules.");
        }

        _remainingCharacters -= rendered.Length + header.Length;
        _activated.UnionWith(pending);
        return header + rendered;
    }
}
