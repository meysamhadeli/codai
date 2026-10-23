using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Tools.Tests;

/// <summary>Shared plumbing for tool tests: a project root, argument parsing, a context.</summary>
internal static class ToolTestHost
{
    /// <summary>A stable, non-existent root used by every test.</summary>
    public static string Root { get; } =
        Path.Combine(Path.GetTempPath(), "codai-tools-tests", "project");

    /// <summary>Builds a project whose root directory exists.</summary>
    public static InMemoryFileSystem NewProject() => new InMemoryFileSystem().AddDirectory(Root);

    /// <summary>Wraps a JSON literal as the argument element a tool receives.</summary>
    public static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>Creates the context handed to a tool.</summary>
    public static ToolContext Context(IFileSystem fileSystem, IToolObserver? observer = null) =>
        new(Root, fileSystem, observer ?? NullToolObserver.Instance);

    /// <summary>
    /// Splits tool output into lines. Tool output is joined with the platform newline,
    /// so tests must not hardcode <c>\n</c>.
    /// </summary>
    public static string[] Lines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
}
