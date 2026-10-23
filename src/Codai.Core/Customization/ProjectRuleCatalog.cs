using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.IO;

namespace Codai.Core.Customization;

/// <summary>
/// Loads project rules and optionally combines them with a host-selected global rules root.
/// </summary>
/// <remarks>
/// Each markdown file is one rule. Optional front matter names the files it applies to:
/// <c>applyTo: "src/Api/**/*.cs, src/Api/**/*.ts"</c>. Patterns may be separated by
/// commas, semicolons or whitespace. A rule with no patterns always applies. The walk is
/// bounded in depth and file count so a pathological tree cannot stall a session, and a
/// malformed or unreadable file is skipped rather than failing the session.
/// Project rules replace global rules with the same relative path and receive count priority.
/// </remarks>
public sealed class ProjectRuleCatalog : IProjectRuleCatalog
{
    /// <summary>Upper bound on how many rules a project may define.</summary>
    public const int MaxRules = 100;

    /// <summary>Upper bound on a rule body, past which it is truncated.</summary>
    public const int MaxCharacters = 8 * 1024;

    /// <summary>Upper bound on how many globs one rule may name.</summary>
    public const int MaxPatterns = 32;

    /// <summary>Upper bound on a single glob's length; longer patterns are ignored.</summary>
    public const int MaxPatternLength = 256;

    /// <summary>How deep the rule directory is walked looking for markdown files.</summary>
    public const int MaxDepth = 4;

    private const int MaxCandidates = 1000;

    private const string TruncationNotice =
        "\n\n[... rule truncated, keep this file shorter ...]";

    private static readonly char[] PatternSeparators = [' ', '\t', ',', ';'];

    private readonly IFileSystem _fileSystem;
    private readonly string? _globalConfigurationDirectory;

    /// <summary>Creates the catalog.</summary>
    public ProjectRuleCatalog(IFileSystem fileSystem, string? globalConfigurationDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
        _globalConfigurationDirectory = globalConfigurationDirectory is null
            ? null : Path.GetFullPath(globalConfigurationDirectory);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectRule>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var projectDirectories = ProjectRuleDirectories(rootDirectory);
        var disabled = await LoadDisabledNamesAsync(rootDirectory, cancellationToken).ConfigureAwait(false);
        var projectRules = new List<ProjectRule>();
        foreach (var directory in projectDirectories)
        {
            projectRules.AddRange(await LoadDirectoryAsync(rootDirectory, directory, false, cancellationToken).ConfigureAwait(false));
        }

        projectRules = projectRules
            .Where(rule => !disabled.Contains(rule.Name))
            .OrderBy(ProjectRuleKey, StringComparer.Ordinal)
            .ThenBy(rule => rule.SourcePath, StringComparer.Ordinal)
            .Take(MaxRules)
            .ToList();
        if (_globalConfigurationDirectory is null || projectRules.Count >= MaxRules)
        {
            return projectRules;
        }

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var projectPaths = projectRules.Select(ProjectRuleKey).ToHashSet(comparer);
        var projectDirectoryPaths = projectDirectories.Select(Path.GetFullPath).ToHashSet(comparer);
        var globalRules = new List<ProjectRule>();
        foreach (var globalDirectory in GlobalRuleDirectories().Distinct(comparer))
        {
            if (projectDirectoryPaths.Contains(Path.GetFullPath(globalDirectory)))
            {
                continue;
            }

            var loaded = await LoadDirectoryAsync(_globalConfigurationDirectory, globalDirectory, true, cancellationToken)
                .ConfigureAwait(false);
            globalRules.AddRange(loaded.Where(rule =>
                !projectPaths.Contains(Path.GetRelativePath(globalDirectory, rule.SourcePath))));
        }

        return [.. projectRules, .. globalRules
            .Where(rule => !disabled.Contains(rule.Name) && !projectPaths.Contains(rule.SourcePath))
            .Take(MaxRules - projectRules.Count)];
    }

    private static IReadOnlyList<string> ProjectRuleDirectories(string rootDirectory) =>
        [Path.Combine(rootDirectory, ".codai", "rules")];

    private static string ProjectRuleKey(ProjectRule rule)
    {
        var normalized = rule.SourcePath.Replace('\\', '/');
        foreach (var marker in new[] { ".codai/rules/" })
        {
            var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return normalized[(index + marker.Length)..];
            }
        }

        return normalized;
    }

    private IReadOnlyList<string> GlobalRuleDirectories() =>
        [Path.Combine(_globalConfigurationDirectory!, "rules")];

    private async Task<HashSet<string>> LoadDisabledNamesAsync(string rootDirectory, CancellationToken cancellationToken)
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await ReadDisabledNamesAsync(
            Path.Combine(rootDirectory, ".codai", CodaiPaths.RulesConfigurationFileName), disabled, cancellationToken)
            .ConfigureAwait(false);

        if (_globalConfigurationDirectory is not null)
        {
            await ReadDisabledNamesAsync(
                Path.Combine(_globalConfigurationDirectory, CodaiPaths.RulesConfigurationFileName), disabled, cancellationToken)
                .ConfigureAwait(false);
        }

        return disabled;
    }

    private async Task ReadDisabledNamesAsync(string file, HashSet<string> disabled, CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(file))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(
                await _fileSystem.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("disabled", out var values) ||
                values.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    disabled.Add(value.GetString()!.Trim());
                }
            }
        }
        catch (JsonException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<IReadOnlyList<ProjectRule>> LoadDirectoryAsync(
        string rootDirectory, string directory, bool isGlobal, CancellationToken cancellationToken)
    {
        List<string> candidates;
        try
        {
            if (!_fileSystem.DirectoryExists(directory))
            {
                return [];
            }

            var collected = new List<string>();
            Walk(directory, collected, depth: 0, cancellationToken);
            collected.Sort(StringComparer.Ordinal);
            candidates = collected;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var rules = new List<ProjectRule>();

        foreach (var file in candidates.Take(MaxRules))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rule = await TryReadAsync(rootDirectory, file, isGlobal, cancellationToken).ConfigureAwait(false);
            if (rule is not null)
            {
                rules.Add(rule);
            }
        }

        return rules;
    }

    private void Walk(string directory, List<string> files, int depth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > MaxDepth || files.Count >= MaxCandidates)
        {
            return;
        }

        try
        {
            foreach (var file in _fileSystem.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (files.Count >= MaxCandidates)
                {
                    return;
                }

                if (Path.GetExtension(file).Equals(".md", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        IEnumerable<string> subdirectories;
        try
        {
            subdirectories = _fileSystem.EnumerateDirectories(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var subdirectory in subdirectories)
        {
            if (files.Count >= MaxCandidates)
            {
                return;
            }

            Walk(subdirectory, files, depth + 1, cancellationToken);
        }
    }

    private async Task<ProjectRule?> TryReadAsync(
        string rootDirectory,
        string path,
        bool isGlobal,
        CancellationToken cancellationToken)
    {
        string text;

        try
        {
            text = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }

        var (fields, body) = FrontMatter.Parse(text);

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        body = Truncate(body.Trim());
        var patterns = fields.TryGetValue("applyTo", out var declared) || fields.TryGetValue("paths", out declared)
            ? ParsePatterns(declared)
            : [];

        var relative = Path.GetRelativePath(rootDirectory, path).Replace('\\', '/');

        return new ProjectRule(Path.GetFileNameWithoutExtension(path), patterns, body,
            isGlobal ? Path.GetFullPath(path).Replace('\\', '/') : relative)
        { IsGlobal = isGlobal };
    }

    /// <summary>
    /// Splits an <c>applyTo</c> or <c>paths</c> value into bounded, distinct globs. An over-long pattern
    /// is dropped rather than truncated, because a truncated glob matches the wrong files.
    /// </summary>
    public static IReadOnlyList<string> ParsePatterns(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var patterns = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in value.Split(PatternSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length is 0 or > MaxPatternLength || !seen.Add(part))
            {
                continue;
            }

            patterns.Add(part);

            if (patterns.Count >= MaxPatterns)
            {
                break;
            }
        }

        return patterns;
    }

    private static string Truncate(string body) =>
        body.Length <= MaxCharacters ? body : body[..MaxCharacters] + TruncationNotice;
}
