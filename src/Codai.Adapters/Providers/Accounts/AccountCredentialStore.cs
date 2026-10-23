using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Codai.Providers.Accounts;

public sealed class AccountSession
{
    public required string ClientId { get; init; }
    public required string Subject { get; init; }
    public string? Email { get; init; }
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required string IdToken { get; init; }
    public required string[] Scopes { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string Id => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ClientId + "\0" + Subject)));
}

public sealed class AccountCredentialStore(string? directory = null)
{
    private readonly string _directory = Path.GetFullPath(directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Codai", "accounts"));

    public async Task<string> GetHostIdAsync(CancellationToken cancellationToken = default)
    {
        using var gate = await LockAsync("host", cancellationToken).ConfigureAwait(false);
        var path = Path.Combine(_directory, "host-id");
        if (File.Exists(path))
        {
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        var value = "urn:uuid:" + Guid.NewGuid().ToString("D");
        await WriteAsync(path, Encoding.UTF8.GetBytes(value), cancellationToken).ConfigureAwait(false);
        return value;
    }

    public async Task<IReadOnlyList<AccountSession>> ListAsync(CancellationToken cancellationToken = default)
    {
        PrepareDirectory();
        List<AccountSession> sessions = [];
        foreach (var path in Directory.EnumerateFiles(_directory, "*.account"))
        {
            var session = await ReadAsync(Path.GetFileNameWithoutExtension(path), cancellationToken).ConfigureAwait(false);
            if (session is not null)
            {
                sessions.Add(session);
            }
        }
        return sessions;
    }

    public async Task<AccountSession?> ReadAsync(string id, CancellationToken cancellationToken = default)
    {
        var path = AccountPath(id);
        if (!File.Exists(path))
        {
            return null;
        }
        byte[] bytes;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            bytes = buffer.ToArray();
        }
        if (OperatingSystem.IsWindows())
        {
            bytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        }
        try
        {
            var session = JsonSerializer.Deserialize<AccountSession>(bytes);
            if (session is null || session.Id != id)
            {
                throw new ProviderConfigurationException("Account credentials are invalid. Run 'codai auth' to sign in again.");
            }
            return session;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public async Task SaveAsync(AccountSession session, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session);
        try
        {
            var protectedBytes = OperatingSystem.IsWindows()
                ? ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser) : bytes;
            await WriteAsync(AccountPath(session.Id), protectedBytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public async Task<FileStream> LockAsync(string id, CancellationToken cancellationToken = default)
    {
        PrepareDirectory();
        var path = Path.Combine(_directory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".lock");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private string AccountPath(string id)
    {
        if (id.Length != 64 || !id.All(char.IsAsciiHexDigit))
        {
            throw new ProviderConfigurationException("Account selection is invalid. Run 'codai auth' to select an account.");
        }
        return Path.Combine(_directory, id + ".account");
    }

    private void PrepareDirectory()
    {
        Directory.CreateDirectory(_directory);
        if (new DirectoryInfo(_directory).LinkTarget is not null)
        {
            throw new IOException("Account storage cannot be a symbolic link.");
        }
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private async Task WriteAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        PrepareDirectory();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            await using (var stream = new FileStream(temporary, options))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(temporary, path, overwrite: true);
                    break;
                }
                catch (IOException exception) when (OperatingSystem.IsWindows() && attempt < 5 && (exception.HResult & 0xffff) is 5 or 32 or 33)
                {
                    await Task.Delay(40 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException exception) when (OperatingSystem.IsWindows() && attempt < 5 && (exception.HResult & 0xffff) == 5)
                {
                    await Task.Delay(40 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}