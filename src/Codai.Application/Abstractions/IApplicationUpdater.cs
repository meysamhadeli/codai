namespace Codai.Application.Abstractions;

/// <summary>Checks published Codai releases and updates the running tool installation.</summary>
public interface IApplicationUpdater
{
    /// <summary>Checks the latest stable release without installing it.</summary>
    Task<ApplicationUpdateInfo> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Installs the exact stable version approved by the user.</summary>
    Task InstallAsync(string version, CancellationToken cancellationToken = default);
}

/// <summary>Running and published versions, including automatic-installation support.</summary>
/// <param name="CurrentVersion">Version running in this process.</param>
/// <param name="LatestVersion">Latest published stable version.</param>
/// <param name="UpdateAvailable">Whether the published version is newer.</param>
/// <param name="InstallUnavailableReason">Why automatic installation is unavailable, if applicable.</param>
public sealed record ApplicationUpdateInfo(string CurrentVersion, string LatestVersion, bool UpdateAvailable,
    string? InstallUnavailableReason = null);