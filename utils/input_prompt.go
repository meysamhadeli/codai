package utils

import (
	"bufio"
	"fmt"
	"io"
	"strings"

	"github.com/meysamhadeli/codai/constants/lipgloss_color"
)

// InputPrompt prompts the user to enter their request for code assistance in a charming way
func InputPrompt(reader *bufio.Reader) (string, error) {

	// Beautifully styled prompt message
	fmt.Print(lipgloss_color.BlueSky.Render("> "))

	// Read user input
	userInput, err := reader.ReadString('\n')
	if err != nil {
		if err == io.EOF {
			return "", nil
		}
		fmt.Println(lipgloss_color.Red.Render("🚫 Error reading input: "), err)
		return userInput, nil
	}

	return strings.TrimSpace(userInput), nil
}
