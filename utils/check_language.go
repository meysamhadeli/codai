package utils

import "strings"

func GetSupportedLanguage(path string) string {
	// Use a switch statement to check for file extensions
	switch {
	case strings.HasSuffix(path, ".cs"):
		return "csharp"
	case strings.HasSuffix(path, ".go"):
		return "golang"
	default:
		return "" // Return empty string if no match
	}
}
