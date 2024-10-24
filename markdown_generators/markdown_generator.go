package markdown_generators

import (
	"bytes"
	"errors"
	"fmt"
	"github.com/charmbracelet/glamour"
	"github.com/meysamhadeli/codai/markdown_generators/contracts"
	"github.com/meysamhadeli/codai/markdown_generators/models"
	"os"
	"os/exec"
	"regexp"
	"strings"
)

type MarkdownConfig struct {
	Theme      string `mapstructure:"theme"`
	DiffViewer string `mapstructure:"diff_viewer"`
}

func (m *MarkdownConfig) ExtractCodeChanges(text string) ([]models.CodeChange, error) {
	// Validate the input text
	if text == "" {
		return nil, errors.New("input text is empty")
	}

	// Regex to match the relative file path (e.g., **File: tests\fakes\Foo.cs**)
	filePathPattern := regexp.MustCompile(`\*\*File:\s*(.+?)\*\*`)
	// Regex to match code blocks (we don't care about language now, just the code content)
	codeBlockPattern := regexp.MustCompile("(?s)```[a-zA-Z0-9]*\\s*(.*?)\\s*```")

	var codeChanges []models.CodeChange

	// Find all file path matches
	filePathMatches := filePathPattern.FindAllStringSubmatch(text, -1)
	// Find all code block matches
	codeMatches := codeBlockPattern.FindAllStringSubmatch(text, -1)

	// Ensure there is a one-to-one correspondence between file paths and code blocks
	if len(filePathMatches) == len(codeMatches) {
		for i, match := range filePathMatches {
			// Extract relative path
			relativePath := strings.TrimSpace(match[1])

			// Extract the code block content
			code := strings.TrimSpace(codeMatches[i][1])

			// Create a new CodeChange struct and append it to the array
			codeChange := models.CodeChange{
				RelativePath: relativePath,
				Code:         code,
			}
			codeChanges = append(codeChanges, codeChange)
		}
	}

	return codeChanges, nil
}

// NewMarkdownGenerator NewCodeAnalyzer initializes a new CodeAnalyzer.
func NewMarkdownGenerator(config *MarkdownConfig) contracts.IMarkdownGenerator {
	return &MarkdownConfig{Theme: config.Theme, DiffViewer: config.DiffViewer}
}

func (m *MarkdownConfig) GenerateMarkdown(results string) error {

	out, err := glamour.Render(results, m.Theme)
	if err != nil {
		return err
	}
	fmt.Print(out)
	return nil
}

// GenerateDiff Run diff to show the difference between original file and temp file
func (m *MarkdownConfig) GenerateDiff(change models.CodeChange) error {
	originalFilePath := change.RelativePath
	tempFilePath := originalFilePath + ".tmp"

	//Check if VSCode is available
	if m.DiffViewer == "vscode" {
		fmt.Printf("Showing diff in VSCode for: %s\n", originalFilePath)
		// Run the diff command: code --diff originalFile tempFile
		cmd := exec.Command("code", "--diff", originalFilePath, tempFilePath)
		cmd.Stdout = os.Stdout
		cmd.Stderr = os.Stderr

		err := cmd.Run()
		if err != nil {
			return fmt.Errorf("error opening VSCode diff for %s: %v", originalFilePath, err)
		}
	} else {
		//Fallback to CLI diff
		cmd := exec.Command("git", "diff", "--no-index", originalFilePath, tempFilePath)

		// Capture the output and error streams
		var diffOut bytes.Buffer
		var stderr bytes.Buffer
		cmd.Stdout = &diffOut
		cmd.Stderr = &stderr

		// Run the command
		err := cmd.Run()

		// Check for exit status 1 (differences found) and other errors
		if err != nil {
			// Check if the error is an exit status 1, which is normal when differences are found
			var exitError *exec.ExitError
			if errors.As(err, &exitError) && exitError.ExitCode() == 1 {

				if diffOut.String() == "" {
					return fmt.Errorf("error cli diff for %s: %v", originalFilePath, err)
				}
				// Print the diff with the styled background
				err = m.GenerateMarkdown(fmt.Sprintf("### DIFF\n\n```%s```", diffOut.String()))
				if err != nil {
					return err
				}
			}
		} else {
			fmt.Println("No diff found.")
		}
	}
	return nil
}
