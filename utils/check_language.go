package utils

import (
	"regexp"
	"strings"
)

var languageBuilder strings.Builder
var isInCodeBlock bool

func DetectLanguageFromCodeBlock(content string) string {

	codeBlockRegex := regexp.MustCompile("^```([a-zA-Z]+)?\\s*$")

	if codeBlockRegex.MatchString(content) {
		isInCodeBlock = !isInCodeBlock
		languageBuilder.Reset()
	}

	if isInCodeBlock && languageBuilder.Len() <= 15 {
		languageBuilder.WriteString(content)
	}

	// Define the regular expression to capture characters before the first newline
	lanRegex := regexp.MustCompile("(?m)^```([a-zA-Z]+)(?:\\s*\\n)?")

	// Find the substring before the first newline
	match := lanRegex.FindStringSubmatch(languageBuilder.String())

	// Check if there was a match and print the result
	if len(match) > 1 {
		return match[1]
	}

	return "markdown"
}

func GetSupportedLanguage(path string) string {
	// Use a switch statement to check for file extensions
	switch {
	case strings.HasSuffix(path, ".cs"):
		return "csharp"
	case strings.HasSuffix(path, ".go"):
		return "go"
	case strings.HasSuffix(path, ".ts"):
		return "typescript"
	case strings.HasSuffix(path, ".js"):
		return "javascript"
	case strings.HasSuffix(path, ".py"):
		return "python"
	case strings.HasSuffix(path, ".java"):
		return "java"
	default:
		return "" // Return empty string if no match
	}
}
