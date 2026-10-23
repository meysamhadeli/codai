using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Projects;
using Codai.Core.Tools;

namespace Codai.Application;

public sealed record RepairWorkflowResult(bool Succeeded, int Attempts, string Report);

public sealed class AutomaticRepairWorkflow
{
    public const int DefaultMaxAttempts = 3;

    private readonly IToolExecutor _tools;
    private readonly ChatExchangeService _chat;
    private readonly ChangeApplicator _changes;
    private readonly IUserInterface _ui;

    public AutomaticRepairWorkflow(
        IToolExecutor tools,
        ChatExchangeService chat,
        ChangeApplicator changes,
        IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(ui);
        _tools = tools;
        _chat = chat;
        _changes = changes;
        _ui = ui;
    }

    public async Task<RepairWorkflowResult> RunAsync(
        ProjectContext context,
        ProjectGuidance guidance,
        int maxAttempts = DefaultMaxAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(guidance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        var reports = new List<string>();
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ui.WriteInfo($"Repair attempt {attempt}/{maxAttempts}: running diagnostics.");
            var diagnostics = await RunDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            reports.Add(diagnostics);

            if (diagnostics.StartsWith("Diagnostics passed", StringComparison.Ordinal))
            {
                return new RepairWorkflowResult(true, attempt, diagnostics);
            }

            if (attempt == maxAttempts)
            {
                break;
            }

            var prompt =
                "Diagnose and repair the current project. Latest validation output:\n\n" +
                diagnostics +
                "\n\nInspect relevant files, make the smallest approved repair, and leave the " +
                "project ready for validation. Do not merely describe a fix.";
            var response = await _chat.SendAsync(context, prompt, guidance, cancellationToken).ConfigureAwait(false);
            await _changes.ApplyAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return new RepairWorkflowResult(false, Math.Min(maxAttempts, reports.Count), string.Join("\n\n", reports));
    }

    private async Task<string> RunDiagnosticsAsync(CancellationToken cancellationToken)
    {
        using var arguments = JsonDocument.Parse("{}");
        var test = await _tools.ExecuteAsync("dotnet_test", arguments.RootElement, cancellationToken).ConfigureAwait(false);
        var build = await _tools.ExecuteAsync("dotnet_build", arguments.RootElement, cancellationToken).ConfigureAwait(false);

        if (test.IsSuccess && build.IsSuccess)
        {
            return "Diagnostics passed: dotnet_test and dotnet_build.";
        }

        return $"dotnet_test {(test.IsSuccess ? "passed" : "failed")}:\n{test.Content}\n\n" +
            $"dotnet_build {(build.IsSuccess ? "passed" : "failed")}:\n{build.Content}";
    }
}