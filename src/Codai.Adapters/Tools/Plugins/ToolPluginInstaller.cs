namespace Codai.Tools.Plugins;

/// <summary>Installs .NET tool plugins into a project's discovered plugin directory.</summary>
public sealed class ToolPluginInstaller(HttpClient? httpClient = null)
{
    public const long MaxPluginBytes = 20 * 1024 * 1024;

    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    public async Task<string> InstallAsync(
        string source,
        string projectRoot,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var destinationDirectory = Path.Combine(Path.GetFullPath(projectRoot), ".codai", "plugins");
        Directory.CreateDirectory(destinationDirectory);
        var fileName = GetFileName(source);
        var destination = Path.Combine(destinationDirectory, fileName);
        if (File.Exists(destination) && !overwrite)
        {
            throw new IOException($"Plugin '{fileName}' is already installed. Set overwrite to replace it.");
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0)
            {
                throw new InvalidOperationException("Plugin URLs must use HTTPS and contain no credentials.");
            }

            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxPluginBytes)
            {
                throw new InvalidOperationException("Plugin exceeds the 20 MB limit.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await CopyBoundedAsync(input, output, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var local = Path.GetFullPath(source, Path.GetFullPath(projectRoot));
            if (!File.Exists(local))
            {
                throw new FileNotFoundException($"Plugin source was not found: {source}", local);
            }

            var length = new FileInfo(local).Length;
            if (length > MaxPluginBytes)
            {
                throw new InvalidOperationException("Plugin exceeds the 20 MB limit.");
            }

            File.Copy(local, destination, overwrite);
        }

        return destination;
    }

    private static string GetFileName(string source)
    {
        var fileName = Uri.TryCreate(source, UriKind.Absolute, out var uri)
            ? Path.GetFileName(uri.LocalPath)
            : Path.GetFileName(source);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Plugin source must identify a .dll file.", nameof(source));
        }

        return fileName;
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxPluginBytes)
            {
                throw new InvalidOperationException("Plugin exceeds the 20 MB limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}