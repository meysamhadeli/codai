package utils

import (
	"errors"
	"os"
	"path/filepath"
)

// GetRootPath finds the root path where the go.mod file is located.
func GetRootPath() (string, error) {
	// Start with the current working directory
	dir, err := os.Getwd()
	if err != nil {
		return "", err
	}

	// Traverse up the directory tree
	for {
		// Check for the presence of go.mod
		goModPath := filepath.Join(dir, "go.mod")
		if _, err := os.Stat(goModPath); err == nil {
			return dir, nil
		}

		// Move up one directory
		parentDir := filepath.Dir(dir)
		if parentDir == dir { // We reached the root directory
			break
		}
		dir = parentDir
	}

	return "", errors.New("go.mod not found")
}
