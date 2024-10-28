package utils

import "strings"

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
