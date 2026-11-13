using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Codai.Application.Abstractions;
using Codai.Configuration;
using NuGet.Versioning;

namespace Codai.Cli;

internal sealed class ApplicationUpdater : IApplicationUpdater, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _currentVersion;
    private readonly string? _toolDirectory;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<(int ExitCode, string Error)>> _run;
    private readonly bool _ownsHttp;
    private string? _approvedVersion;

    public ApplicationUpdater()
        : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = 1024 * 1024,
        }, CodaiDefaults.Version, AppContext.BaseDirectory)
    {
        _ownsHttp = true;
    }

    internal ApplicationUpdater(HttpClient http, string currentVersion, string baseDirectory,
        Func<ProcessStartInfo, CancellationToken, Task<(int ExitCode, string Error)>>? run = null)
    {
        _http = http;
        _currentVersion = currentVersion;
        _toolDirectory = FindToolDirectory(baseDirectory);
        _run = run ?? RunProcessAsync;
    }

    public async Task<ApplicationUpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await CheckReleaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The update check timed out. Check your connection and try again.");
        }
    }

    private async Task<ApplicationUpdateInfo> CheckReleaseAsync(CancellationToken cancellationToken)
    {
        _approvedVersion = null;
        using var response = await _http.GetAsync("https://api.nuget.org/v3-flatcontainer/codai/index.json",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("NuGet returned an empty version list.");
        var latest = document.RootElement.GetProperty("versions").EnumerateArray()
            .Select(item => NuGetVersion.TryParse(item.GetString(), out var version) ? version : null)
            .Where(version => version is not null && !version.IsPrerelease)
            .OrderByDescending(version => version)
            .FirstOrDefault() ?? throw new InvalidDataException("NuGet returned no stable Codai releases.");
        if (!NuGetVersion.TryParse(_currentVersion, out var current))
        {
            throw new InvalidDataException($"Cannot compare running version '{_currentVersion}'.");
        }

        var available = latest > current;
        var versionText = latest.ToNormalizedString();
        string? reason = _toolDirectory is null
            ? "Automatic updates require a global or --tool-path .NET tool installation. Source builds, local tools, and standalone builds must be updated manually."
            : null;
        if (available && reason is null)
        {
            _approvedVersion = versionText;
        }
        return new(_currentVersion, versionText, available, reason);
    }

    public async Task InstallAsync(string version, CancellationToken cancellationToken = default)
    {
        if (_toolDirectory is null || _approvedVersion is null || version != _approvedVersion)
        {
            throw new InvalidOperationException("Check for updates before installing the latest stable version.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (var argument in new[] { "tool", "update", "Codai", "--tool-path", _toolDirectory,
            "--version", version, "--source", "https://api.nuget.org/v3/index.json", "--verbosity", "minimal" })
        {
            start.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        (int ExitCode, string Error) result;
        try
        {
            result = await _run(start, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The update timed out. Run /update to check again.");
        }
        if (result.ExitCode != 0)
        {
            var detail = result.Error.Trim();
            throw new InvalidOperationException($"dotnet tool update failed (exit {result.ExitCode}). {detail[..Math.Min(detail.Length, 1000)]}");
        }
        _approvedVersion = null;
    }

    internal static string? FindToolDirectory(string baseDirectory)
    {
        var directory = new DirectoryInfo(baseDirectory);
        while (directory.Parent is not null)
        {
            if (directory.Name.Equals(".store", StringComparison.OrdinalIgnoreCase))
            {
                var parts = Path.GetRelativePath(directory.FullName, baseDirectory)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return parts.Length >= 7 && parts[0].Equals("codai", StringComparison.OrdinalIgnoreCase) &&
                    parts[2].Equals("codai", StringComparison.OrdinalIgnoreCase) && parts[1] == parts[3] &&
                    NuGetVersion.TryParse(parts[1], out _) && parts[4] == "tools"
                    ? directory.Parent.FullName : null;
            }
            directory = directory.Parent;
        }
        return null;
    }

    private static async Task<(int ExitCode, string Error)> RunProcessAsync(ProcessStartInfo start,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = start };
        process.Start();
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, await error.ConfigureAwait(false) + await output.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await Task.WhenAll(output, error).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            throw;
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}