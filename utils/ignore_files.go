package utils

import (
	"fmt"
	"io/ioutil"
	"os"
	"path/filepath"
	"strings"
)

// GetGitignorePatterns reads and returns the patterns from the .gitignore file.
// If the file does not exist, it returns an empty pattern list.
func GetGitignorePatterns(cwd string) ([]string, error) {
	gitignorePath := filepath.Join(cwd, ".codai-gitignore")

	// Check if the .gitignore file exists
	if _, err := os.Stat(gitignorePath); os.IsNotExist(err) {
		// .gitignore doesn't exist, return an empty slice
		return []string{}, nil
	} else if err != nil {
		// Some other error occurred while checking the file
		return nil, fmt.Errorf("error checking .codai-gitignore: %w", err)
	}

	// Read and parse the .gitignore file if it exists
	ignorePatterns, err := readGitignore(gitignorePath)
	if err != nil {
		return nil, fmt.Errorf("failed to read .codai-gitignore: %w", err)
	}

	// Validate patterns to ignore those that start with .git and .idea
	var validPatterns []string
	for _, pattern := range ignorePatterns {
		if !IsDefaultIgnored(pattern) {
			validPatterns = append(validPatterns, pattern)
		}
	}

	return validPatterns, nil
}

func IsDefaultIgnored(path string) bool {
	// Define ignore patterns
	ignorePatterns := []string{
		"codai-config.yml",
		".git",
		".svn",
		".sum",
		".tmp",
		".tmpl",
		".idea",
		".vscode",
		"bin",
		"obj",
		"dist",
		"out",
		"node_modules",
		"*.exe",
		"*.dll",
		"*.log",
		"*.bak",
		".mp3",
		".wav",
		".aac",
		".flac",
		".ogg",
		".jpg",
		".jpeg",
		".png",
		".gif",
		".mkv",
		".mp4",
		".avi",
		".mov",
		".wmv",
	}

	// Split the path into parts based on the file separator
	parts := strings.Split(path, string(filepath.Separator))

	// Check each part for any ignore patterns
	for _, part := range parts {
		part = strings.ToLower(part)
		for _, pattern := range ignorePatterns {
			if strings.HasPrefix(pattern, "*") {
				// If the pattern starts with '*', check for suffix
				suffix := strings.TrimPrefix(pattern, "*")
				if strings.HasSuffix(part, suffix) {
					return true
				}
			} else {
				// Check for both prefix and suffix matches
				if strings.HasPrefix(part, pattern) || strings.HasSuffix(part, pattern) {
					return true
				}
			}
		}
	}
	return false
}

// readGitignore reads the .gitignore file and returns the list of ignore patterns.
func readGitignore(gitignorePath string) ([]string, error) {
	content, err := ioutil.ReadFile(gitignorePath)
	if err != nil {
		return nil, err
	}
	lines := strings.Split(string(content), "\n")
	var patterns []string
	for _, line := range lines {
		line = strings.TrimSpace(line)
		if line != "" && !strings.HasPrefix(line, "#") {
			patterns = append(patterns, line)
		}
	}
	return patterns, nil
}

// IsGitIgnored checks if a file path matches any of the patterns in .gitignore.
func IsGitIgnored(path string, patterns []string) bool {
	for _, pattern := range patterns {
		match, _ := filepath.Match(pattern, path)
		if match {
			return true
		}
		// Handle patterns like "dir/" that ignore entire directories
		if strings.HasSuffix(pattern, "/") && strings.HasPrefix(path, pattern) {
			return true
		}
	}
	return false
}
