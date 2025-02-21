package utils

import (
	"bufio"
	"fmt"
	"io"
	"strings"

	"github.com/meysamhadeli/codai/constants/lipgloss"
)

// InputPrompt prompts the user to enter their request for code assistance in a charming way
// and reads multi-line input until a custom delimiter is entered.
func InputPrompt(reader *bufio.Reader) (string, error) {
	// Beautifully styled prompt message
	fmt.Print("\r")
	fmt.Print(lipgloss.BlueSky.Render("> "))

	var inputLines []string
	delimiter := ";;" // Custom delimiter to signal end of input

	for {
		line, err := reader.ReadString('\n')
		if err != nil {
			if err == io.EOF {
				break // Exit on EOF (Ctrl+D or Ctrl+Z)
			}
			return "", fmt.Errorf(lipgloss.Red.Render("🚫 Error reading input: "))
		}

		// Check if the line contains the custom delimiter
		if strings.Contains(line, delimiter) {
			break // Exit if delimiter is found
		}

		// Append the line to the input
		inputLines = append(inputLines, line)
	}

	// Combine the lines into a single string and trim trailing whitespace
	return strings.TrimSpace(strings.Join(inputLines, "")), nil
}
