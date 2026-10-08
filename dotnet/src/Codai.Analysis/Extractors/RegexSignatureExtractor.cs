using System.Text.RegularExpressions;
using Codai.Core.Abstractions;

namespace Codai.Analysis.Extractors;

/// <summary>
/// A single signature rule: a tag and a regex whose first group is the symbol name.
/// </summary>
public sealed record SignatureRule(string Tag, string Pattern, RegexOptions Options = RegexOptions.None)
{
    public Regex Compile() => new(Pattern, Options | RegexOptions.Compiled);
}

/// <summary>
/// Regex-based signature extraction. Used for languages where a full parser is not
/// (yet) wired up; the tag names match the original tree-sitter captures.
/// </summary>
public sealed partial class RegexSignatureExtractor : ISignatureExtractor
{
    private readonly string _language;
    private readonly IReadOnlyList<(SignatureRule Rule, Regex Regex)> _rules;

    public RegexSignatureExtractor(string language, IReadOnlyList<SignatureRule> rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(rules);

        _language = language;
        _rules = rules.Select(rule => (rule, rule.Compile())).ToList();
    }

    public bool Supports(string filePath) =>
        string.Equals(LanguageId.DetectFromPath(filePath), _language, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> Extract(string filePath, string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var elements = new List<string>();

        foreach (var line in source.Split('\n'))
        {
            foreach (var (rule, regex) in _rules)
            {
                var match = regex.Match(line);
                if (match.Success && match.Groups.Count > 1)
                {
                    var name = match.Groups[1].Value.Trim();
                    if (name.Length > 0)
                    {
                        elements.Add($"{rule.Tag}: {name}");
                    }
                }
            }
        }

        return elements;
    }
}
