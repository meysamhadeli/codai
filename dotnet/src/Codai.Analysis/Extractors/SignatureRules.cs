using System.Text.RegularExpressions;

namespace Codai.Analysis.Extractors;

/// <summary>
/// Signature rules per language, mirroring the original tree-sitter query captures.
/// </summary>
public static class SignatureRules
{
    public static IReadOnlyList<RegexSignatureExtractor> All { get; } =
    [
        Go(),
        Python(),
        Java(),
        JavaScript(),
        TypeScript(),
    ];

    public static RegexSignatureExtractor Go() => new(LanguageId.Go,
    [
        new SignatureRule("package", @"^\s*package\s+(\w+)"),
        new SignatureRule("method", @"^\s*func\s+\([^)]*\)\s*(\w+)\s*\("),
        new SignatureRule("function", @"^\s*func\s+(\w+)\s*\("),
        new SignatureRule("interface", @"^\s*type\s+(\w+)\s+interface\b"),
        new SignatureRule("struct", @"^\s*type\s+(\w+)\s+struct\b"),
    ]);

    public static RegexSignatureExtractor Python() => new(LanguageId.Python,
    [
        new SignatureRule("class", @"^\s*class\s+(\w+)"),
        new SignatureRule("decorated", @"^\s*@(\w+)"),
        new SignatureRule("function", @"^\s*(?:async\s+)?def\s+(\w+)"),
    ]);

    public static RegexSignatureExtractor Java() => new(LanguageId.Java,
    [
        new SignatureRule("package", @"^\s*package\s+([\w.]+)\s*;"),
        new SignatureRule("interface", @"\binterface\s+(\w+)"),
        new SignatureRule("enum", @"\benum\s+(\w+)"),
        new SignatureRule("class", @"\bclass\s+(\w+)"),
        new SignatureRule(
            "method",
            @"^\s*(?:@\w+\s*)*(?:public|private|protected|static|final|abstract|synchronized|native|default|\s)+[\w<>\[\].,?\s]+\s+(\w+)\s*\("),
    ]);

    public static RegexSignatureExtractor JavaScript() => new(LanguageId.JavaScript,
    [
        new SignatureRule("class", @"\bclass\s+(\w+)"),
        new SignatureRule("function", @"\bfunction\s+(\w+)"),
        new SignatureRule("anonymous_function", @"\b(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s*)?(?:function|\(|[\w$]+\s*=>)"),
        new SignatureRule("method", @"^\s*(?:async\s+)?(\w+)\s*\([^)]*\)\s*\{"),
    ]);

    public static RegexSignatureExtractor TypeScript() => new(LanguageId.TypeScript,
    [
        new SignatureRule("class", @"\bclass\s+(\w+)"),
        new SignatureRule("interface", @"\binterface\s+(\w+)"),
        new SignatureRule("enum", @"\benum\s+(\w+)"),
        new SignatureRule("function", @"\bfunction\s+(\w+)"),
        new SignatureRule("anonymous_function", @"\b(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s*)?(?:function|\(|[\w$]+\s*=>)"),
        new SignatureRule("method", @"^\s*(?:public|private|protected|static|async|readonly|\s)*(\w+)\s*\([^)]*\)\s*(?::\s*[\w<>\[\].,|\s]+)?\{"),
    ]);
}
