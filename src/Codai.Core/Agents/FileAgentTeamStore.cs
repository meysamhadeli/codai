using System.Text.Json;
using Codai.Core.Abstractions;

namespace Codai.Core.Agents;

public sealed class FileAgentTeamStore : IAgentTeamStore
{
    public const string RelativeFolder = ".codai/tasks/teams";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IFileSystem _fileSystem;

    public FileAgentTeamStore(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    public async Task<IReadOnlyList<AgentTeam>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var folder = GetFolder(rootDirectory);
        if (!_fileSystem.DirectoryExists(folder))
        {
            return [];
        }

        var teams = new List<AgentTeam>();
        foreach (var path in _fileSystem.EnumerateFiles(folder))
        {
            try
            {
                var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                if (JsonSerializer.Deserialize<AgentTeam>(json) is { } team)
                {
                    teams.Add(team);
                }
            }
            catch (JsonException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return teams.OrderByDescending(team => team.UpdatedAt).ToArray();
    }

    public async Task SaveAsync(
        string rootDirectory,
        AgentTeam team,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(team);

        var folder = GetFolder(rootDirectory);
        _fileSystem.CreateDirectory(folder);
        await _fileSystem.WriteAllTextAtomicallyAsync(
            Path.Combine(folder, $"{team.Id}.json"),
            JsonSerializer.Serialize(team, JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private static string GetFolder(string rootDirectory) =>
        Path.Combine(rootDirectory, RelativeFolder.Replace('/', Path.DirectorySeparatorChar));
}
