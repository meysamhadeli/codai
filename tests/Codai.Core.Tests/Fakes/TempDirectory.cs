namespace Codai.Core.Tests.Fakes;

/// <summary>A throwaway directory that is removed when disposed.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "codai-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) =>
        System.IO.Path.Combine([Path, .. parts]);

    public void WriteFile(string relativePath, string contents)
    {
        var fullPath = Combine(relativePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, contents);
    }

    public string ReadFile(string relativePath) => File.ReadAllText(Combine(relativePath));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
