using Codai.Core.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Codai.Analysis.Extractors;

/// <summary>
/// Extracts C# structural signatures using Roslyn. Tag names mirror the original
/// tree-sitter query captures (<c>namespace</c>, <c>class</c>, <c>method</c>, ...).
/// </summary>
public sealed class CSharpSignatureExtractor : ISignatureExtractor
{
    public bool Supports(string filePath) =>
        string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> Extract(string filePath, string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest));

        var root = tree.GetRoot();
        var elements = new List<string>();

        // Document order is deterministic (the original Go implementation iterated a
        // map and so produced non-deterministic ordering).
        foreach (var node in root.DescendantNodes())
        {
            var entry = node switch
            {
                FileScopedNamespaceDeclarationSyntax fileScoped =>
                    Tag("file_scoped_namespace", fileScoped.Name),
                NamespaceDeclarationSyntax blockScoped =>
                    Tag("namespace", blockScoped.Name),
                InterfaceDeclarationSyntax declaration =>
                    Tag("interface", declaration.Identifier),
                RecordDeclarationSyntax declaration =>
                    Tag("record", declaration.Identifier),
                StructDeclarationSyntax declaration =>
                    Tag("struct", declaration.Identifier),
                EnumDeclarationSyntax declaration =>
                    Tag("enum", declaration.Identifier),
                ClassDeclarationSyntax declaration =>
                    Tag("class", declaration.Identifier),
                MethodDeclarationSyntax declaration =>
                    Tag("method", declaration.Identifier),
                PropertyDeclarationSyntax declaration =>
                    Tag("property", declaration.Identifier),
                _ => null,
            };

            if (entry is not null)
            {
                elements.Add(entry);
            }
        }

        return elements;
    }

    private static string Tag(string tag, SyntaxNode? name) =>
        $"{tag}: {name?.ToString().Trim()}";

    private static string Tag(string tag, SyntaxToken identifier) =>
        $"{tag}: {identifier.ValueText}";
}
