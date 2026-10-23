namespace Codai.Core.Customization;

/// <summary>A bounded project-memory document loaded into the session prompt.</summary>
public sealed record ProjectMemoryDocument(
    string Name,
    string SourcePath,
    string Content);
