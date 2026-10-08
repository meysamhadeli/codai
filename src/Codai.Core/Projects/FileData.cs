namespace Codai.Core.Projects;

/// <summary>
/// A single project file discovered by the scanner.
/// </summary>
/// <param name="RelativePath">Path relative to the scanned root, using forward slashes.</param>
/// <param name="Content">Full raw content of the file.</param>
/// <param name="Signature">Extracted structural signature (namespaces, types, members).</param>
public sealed record FileData(string RelativePath, string Content, string Signature);
