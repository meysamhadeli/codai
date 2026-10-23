using System.Text.Json;
using System.Text.Json.Serialization;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Tools;

namespace Codai.Application;

public sealed class HookRunner(
    IFileSystem fileSystem,
    IProcessRunner processes,
    IToolApprover approver,
    AgentModeState mode,
    IReadOnlyList<string> directories,
    bool allowShell = false,
    IAgentProgressSink? progress = null,
    IEnumerable<IPluginAgentHook>? pluginHooks = null) : IAgentHooks
{
    public const string ManifestFileName = "hooks.json";
    public const int MaxManifestBytes = 64 * 1024;
    public const int MaxPayloadChars = 256 * 1024;
    public const int MaxHooks = 32;

    public async Task<AgentHookDecision> InvokeAsync(AgentHookContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var pluginHook in pluginHooks ?? [])
        {
            var decision = await pluginHook.InvokeAsync(context, cancellationToken).ConfigureAwait(false);
            if (!decision.Allowed)
            {
                return decision;
            }
        }

        var eventName = context.Event.ToString();
        var count = 0;
        var configured = new List<(string Path, HookCommand Hook)>();
        HookCommand? activeHook = null;
        try
        {
            foreach (var directory in directories.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            {
                var path = Path.Combine(directory, ManifestFileName);
                if (!fileSystem.FileExists(path))
                {
                    continue;
                }

                if (fileSystem.GetFileSize(path) > MaxManifestBytes)
                {
                    return AgentHookDecision.Block($"Hook manifest '{path}' exceeds {MaxManifestBytes} bytes.");
                }

                var json = await fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                if (json.Length > MaxManifestBytes)
                {
                    return AgentHookDecision.Block($"Hook manifest '{path}' is too large.");
                }

                var manifest = JsonSerializer.Deserialize(json, HookJsonContext.Default.HookManifest);
                if (manifest?.Hooks is null)
                {
                    return AgentHookDecision.Block($"Invalid hook manifest '{path}'.");
                }

                count += manifest.Hooks.Count;
                if (count > MaxHooks)
                {
                    return AgentHookDecision.Block($"At most {MaxHooks} hooks may be configured per invocation.");
                }

                foreach (var hook in manifest.Hooks)
                {
                    if (hook is null)
                    {
                        return AgentHookDecision.Block($"Null hook definition in '{path}'.");
                    }

                    if (hook.Enabled == false)
                    {
                        continue;
                    }

                    var timeoutSeconds = hook.TimeoutSeconds ?? 10;
                    if (string.IsNullOrWhiteSpace(hook.Name) || string.IsNullOrWhiteSpace(hook.Command) ||
                        !Enum.GetNames<AgentHookEvent>().Contains(hook.Event, StringComparer.Ordinal) ||
                        timeoutSeconds is < 1 or > 300)
                    {
                        return AgentHookDecision.Block($"Invalid enabled hook definition in '{path}'.");
                    }

                    if (!string.Equals(hook.Event, eventName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    configured.Add((path, hook));
                }
            }

            foreach (var (path, hook) in configured)
            {
                var timeoutSeconds = hook.TimeoutSeconds ?? 10;

                if (!allowShell || !AgentModePolicy.AllowsWrites(mode.Mode))
                {
                    return AgentHookDecision.Block($"Hook '{hook.Name}' requires Agent mode with shell execution enabled.");
                }

                var payload = JsonSerializer.Serialize(context, HookJsonContext.Default.AgentHookContext);
                if (payload.Length > MaxPayloadChars)
                {
                    return AgentHookDecision.Block($"Hook '{hook.Name}' payload exceeds {MaxPayloadChars} characters.");
                }

                if (!await approver.ApproveAsync(new ToolApprovalRequest("hook", hook.Name!,
                    $"{hook.Event} hook '{hook.Name}' from '{path}': {hook.Command}"), cancellationToken).ConfigureAwait(false))
                {
                    return AgentHookDecision.Block($"Hook '{hook.Name}' was not approved.");
                }

                activeHook = hook;
                await PublishAsync(AgentProgressEventKind.HookStarted, context, hook, cancellationToken).ConfigureAwait(false);
                var result = await processes.RunWithInputAsync(hook.Command!, context.ProjectRoot, payload,
                    TimeSpan.FromSeconds(timeoutSeconds), cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    await PublishAsync(AgentProgressEventKind.HookFailed, context, hook, cancellationToken).ConfigureAwait(false);
                    return AgentHookDecision.Block(result.TimedOut
                        ? $"Hook '{hook.Name}' timed out."
                        : $"Hook '{hook.Name}' failed with exit code {result.ExitCode}: {result.StandardError}");
                }

                if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    var response = JsonSerializer.Deserialize(result.StandardOutput, HookJsonContext.Default.HookResponse);
                    if (response?.Allow is null)
                    {
                        await PublishAsync(AgentProgressEventKind.HookFailed, context, hook, cancellationToken).ConfigureAwait(false);
                        return AgentHookDecision.Block($"Hook '{hook.Name}' must return an 'allow' boolean or empty stdout.");
                    }

                    if (!response.Allow.Value)
                    {
                        await PublishAsync(AgentProgressEventKind.HookFailed, context, hook, cancellationToken).ConfigureAwait(false);
                        return AgentHookDecision.Block(response.Reason ?? $"Hook '{hook.Name}' blocked {eventName}.");
                    }
                }

                await PublishAsync(AgentProgressEventKind.HookCompleted, context, hook, cancellationToken).ConfigureAwait(false);
                activeHook = null;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (activeHook is not null)
            {
                await PublishAsync(AgentProgressEventKind.HookFailed, context, activeHook, cancellationToken).ConfigureAwait(false);
            }

            return AgentHookDecision.Block($"Hook execution failed: {ex.Message}");
        }

        return AgentHookDecision.Continue;
    }

    private ValueTask PublishAsync(AgentProgressEventKind kind, AgentHookContext context, HookCommand hook,
        CancellationToken cancellationToken) => (progress ?? NullAgentProgressSink.Instance).PublishAsync(
            new AgentProgressEvent(context.TaskId ?? "foreground", hook.Event!, kind, hook.Name, DateTimeOffset.UtcNow),
            cancellationToken);
}

internal sealed record HookManifest
{
    public IReadOnlyList<HookCommand?>? Hooks { get; init; }
}

internal sealed record HookCommand
{
    public string? Name { get; init; }
    public string? Event { get; init; }
    public string? Command { get; init; }
    public bool? Enabled { get; init; }
    public int? TimeoutSeconds { get; init; }
}

internal sealed record HookResponse
{
    public bool? Allow { get; init; }
    public string? Reason { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(HookManifest))]
[JsonSerializable(typeof(HookResponse))]
[JsonSerializable(typeof(AgentHookContext))]
internal sealed partial class HookJsonContext : JsonSerializerContext;
