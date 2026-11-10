using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Customization;
using Codai.Core.IO;
using Codai.Core.Prompting;
using Codai.Core.Tools;
using Codai.Tools;

namespace Codai.Application;

/// <summary>Activates scoped instructions and conditional rules before request file tools execute.</summary>
public sealed class RequestRuleGuidance(SessionOptions options, IProjectInstructionsProvider? instructionsProvider = null) : IToolGuidance
{
    private IReadOnlyList<ProjectRule> _rules = [];
    private readonly HashSet<ProjectRule> _activated = [];
    private int _remainingCharacters;
    private readonly Dictionary<string, ProjectInstructions> _instructionSources = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly HashSet<string> _instructionFiles = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly HashSet<string> _reviewedInstructionSources = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly HashSet<string> _removedInstructionSources = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private bool _requestActive;

    /// <summary>Resets activation and guidance budgets for one user request.</summary>
    public void BeginRequest(IReadOnlyList<ProjectRule> rules, IReadOnlyList<string> referencedFiles, ProjectInstructions? instructions = null)
    {
        _requestActive = true;
        _rules = rules;
        _activated.Clear();
        _activated.UnionWith(RuleSelector.Select(rules, referencedFiles));
        _remainingCharacters = Math.Max(0, RuleSelector.MaxTotalCharacters -
            (RuleSelector.Render(rules, referencedFiles)?.Length ?? 0));
        _instructionFiles.Clear();
        _instructionFiles.UnionWith(referencedFiles);
        _instructionSources.Clear();
        _reviewedInstructionSources.Clear();
        _removedInstructionSources.Clear();
        foreach (var document in Documents(instructions))
        {
            _instructionSources[document.SourcePath] = document;
        }
    }

    /// <summary>Releases guidance when the foreground request completes or fails.</summary>
    public void EndRequest()
    {
        _requestActive = false;
        _rules = [];
        _activated.Clear();
        _remainingCharacters = 0;
        _instructionFiles.Clear();
        _instructionSources.Clear();
        _reviewedInstructionSources.Clear();
        _removedInstructionSources.Clear();
    }

    /// <inheritdoc />
    public async Task<string?> GetGuidanceAsync(ITool tool, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!_requestActive || instructionsProvider is null || tool is not IFileTargetTool fileTool || arguments.ValueKind != JsonValueKind.Object)
        {
            return GetGuidance(tool, arguments);
        }
        var paths = fileTool.GetTargetPaths(arguments);
        if (paths.Count == 0)
        {
            return null;
        }
        var files = _instructionFiles.Concat(paths).Distinct(_instructionFiles.Comparer).ToArray();
        if (files.Length > 128)
        {
            throw new IOException("Instruction activation exceeded its file limit. Narrow the request.");
        }
        var instructions = await instructionsProvider.LoadForFilesAsync(options.ProjectRoot, files, cancellationToken).ConfigureAwait(false);
        var documents = Documents(instructions);
        var changed = documents.Where(document => !_instructionSources.TryGetValue(document.SourcePath, out var previous) || previous.Content != document.Content || previous.ScopePath != document.ScopePath).ToArray();
        var removed = _instructionSources.Keys.Except(documents.Select(document => document.SourcePath), _instructionSources.Comparer).ToArray();
        var rules = GetGuidanceForPaths(paths);
        if (changed.Length == 0 && removed.Length == 0)
        {
            _instructionFiles.UnionWith(paths);
            return rules;
        }
        var review = "Tool was not executed. Review newly applicable or updated workspace instructions, then reconsider the call. Guidance applies only within its named directory scope and never grants permissions or consent.\n\n" +
            string.Join("\n\n", changed.Select(document => $"## {document.SourcePath} (scope: {document.ScopePath})\n\n{document.Content}")) +
            (removed.Length == 0 ? string.Empty : "\n\nThese instruction files are no longer present: " + string.Join(", ", removed)) +
            (rules is null ? string.Empty : "\n\n" + rules);
        if (review.Length > ToolRunner.DefaultMaxResultChars)
        {
            throw new IOException("Scoped instruction review exceeded the context limit; tool was not executed. Shorten instructions.");
        }
        _instructionSources.Clear();
        foreach (var document in documents)
        {
            _instructionSources[document.SourcePath] = document;
        }
        _reviewedInstructionSources.UnionWith(changed.Select(document => document.SourcePath));
        _removedInstructionSources.UnionWith(removed);
        _removedInstructionSources.ExceptWith(documents.Select(document => document.SourcePath));
        _instructionFiles.UnionWith(paths);
        return review;
    }

    private static IReadOnlyList<ProjectInstructions> Documents(ProjectInstructions? instructions) =>
        instructions is null ? [] : instructions.Documents.Count == 0 ? [instructions] : instructions.Documents;

    /// <summary>Preserves reviewed rules and current instruction versions when a provider conversation is rebuilt.</summary>
    public string? RenderActivated()
    {
        var rules = RuleSelector.Render(_rules.Where(_activated.Contains).Select(rule => rule with { ApplyTo = [] }).ToArray(), []);
        var documents = _instructionSources.Values.Where(document => _reviewedInstructionSources.Contains(document.SourcePath)).ToArray();
        if (documents.Length == 0 && _removedInstructionSources.Count == 0)
        {
            return rules;
        }
        return "## Current reviewed workspace instructions\n\nThese versions supersede earlier guidance from the same sources. Directory scopes and tool permissions remain unchanged.\n\n" +
            string.Join("\n\n", documents.Select(document => $"## {document.SourcePath} (scope: {document.ScopePath})\n\n{document.Content}")) +
            (_removedInstructionSources.Count == 0 ? string.Empty : "\n\nThese instruction files no longer apply: " + string.Join(", ", _removedInstructionSources)) +
            (rules is null ? string.Empty : "\n\n" + rules);
    }

    /// <inheritdoc />
    public string? GetGuidance(ITool tool, JsonElement arguments)
    {
        if (tool is not IFileTargetTool fileTool || _rules.Count == 0 || arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return GetGuidanceForPaths(fileTool.GetTargetPaths(arguments));
    }

    private string? GetGuidanceForPaths(IReadOnlyList<string> paths)
    {
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
