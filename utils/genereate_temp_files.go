package utils

import (
	"fmt"
	"os"
	"path/filepath"
)

// WriteToTempFile code to a temporary file (without overwriting the original)
func WriteToTempFile(relativePath string, code string) (string, error) {
	tempPath := relativePath + ".tmp"
	// Create directories if they don't exist
	err := os.MkdirAll(filepath.Dir(tempPath), os.ModePerm)
	if err != nil {
		return "", fmt.Errorf("failed to create directories for %s: %v", tempPath, err)
	}

	// Write the code to the temp file
	err = os.WriteFile(tempPath, []byte(code), 0644)
	if err != nil {
		return "", fmt.Errorf("failed to write code to temp file %s: %v", tempPath, err)
	}

	return tempPath, nil // Return the path of the temp file
}

// CleanupTempFiles Cleanup temporary files
func CleanupTempFiles(dir string) error {
	// Walk through the directory and find all .tmp files
	err := filepath.Walk(dir, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			return err
		}
		// Check for .tmp file extension
		if filepath.Ext(path) == ".tmp" {
			// Attempt to remove the temporary file
			err = os.Remove(path)
			if err != nil {
				return fmt.Errorf("failed to delete temporary file %s: %v", path, err)
			}
		}
		return nil
	})

	return err
}
