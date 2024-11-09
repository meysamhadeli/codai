package utils

import (
	"bufio"
	"fmt"
	"io"
	"strings"

	"github.com/meysamhadeli/codai/constants/lipgloss"
)

// InputPrompt prompts the user to enter their request for code assistance in a charming way
func InputPrompt(reader *bufio.Reader) (string, error) {

	// Beautifully styled prompt message
	fmt.Print(lipgloss.BlueSky.Render("> "))

	// Read user input
	userInput, err := reader.ReadString('\n')
	if userInput == "" {
		return "", nil
	}

	if err != nil {
		if err == io.EOF {
			return "", nil
		}
		return "", fmt.Errorf(lipgloss.Red.Render("🚫 Error reading input: "))
	}

	return strings.TrimSpace(userInput), nil
}
