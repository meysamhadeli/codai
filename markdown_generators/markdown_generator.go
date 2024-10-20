package markdown_generators

import (
	"bufio"
	"bytes"
	"errors"
	"fmt"
	"github.com/charmbracelet/glamour"
	"github.com/meysamhadeli/codai/markdown_generators/contracts"
	"github.com/meysamhadeli/codai/markdown_generators/models"
	"os"
	"os/exec"
	"strings"
)

type MarkdownConfig struct {
	Theme      string `mapstructure:"theme"`
	DiffViewer string `mapstructure:"diff_viewer"`
}

func (m *MarkdownConfig) ExtractCodeChanges(text, language string) ([]models.CodeChange, error) {
	// Validate the input text
	if text == "" {
		return nil, errors.New("input text is empty")
	}

	var changes []models.CodeChange
	scanner := bufio.NewScanner(strings.NewReader(text))

	// Define markers for starting and ending code blocks
	startMarker := fmt.Sprintf("```%s", language)
	endMarker := "```"

	var currentFile string
	var currentCode strings.Builder
	inCodeBlock := false

	for scanner.Scan() {
		line := scanner.Text()

		// Detect the start or end of a code block
		if strings.TrimSpace(line) == startMarker {
			// Start a new code block
			inCodeBlock = true
		} else if strings.TrimSpace(line) == endMarker {
			// End of a code block
			if inCodeBlock && currentFile != "" && currentCode.Len() > 0 {
				// Add the completed CodeChange
				changes = append(changes, models.CodeChange{
					RelativePath: strings.TrimSpace(currentFile),
					Code:         strings.TrimSpace(currentCode.String()),
				})
			} else if inCodeBlock && currentFile == "" {
				// If we reach the end without a file path
				return nil, errors.New("code block ended without a file path")
			}

			// Reset for the next block
			inCodeBlock = false
			currentCode.Reset()
			currentFile = ""
		} else if strings.HasPrefix(line, "// File: ") && inCodeBlock {
			// Capture the file path inside the code block
			currentFile = strings.TrimSpace(strings.TrimPrefix(line, "// File: "))
			if currentFile == "" {
				// If the file path is empty
				return nil, errors.New("empty file path found")
			}
		} else if inCodeBlock {
			// Capture code lines inside the current code block
			currentCode.WriteString(line + "\n")
		}
	}

	// Check for any scanning errors
	if err := scanner.Err(); err != nil {
		return nil, fmt.Errorf("error while scanning text: %v", err)
	}

	return changes, nil
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
