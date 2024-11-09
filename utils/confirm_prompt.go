package utils

import (
	"bufio"
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss"
	"os"
	"strings"
)

// ConfirmPrompt PromptUser prompts the user to accept or reject the changes with a charming interface
func ConfirmPrompt(path string) (bool, error) {
	reader := bufio.NewReader(os.Stdin)

	// Styled prompt message
	fmt.Printf(lipgloss.BlueSky.Render(fmt.Sprintf("Do you want to accept the change for file '%v'%v", lipgloss.LightBlueB.Render(path), lipgloss.BlueSky.Render(" ? (y/n): "))))

	for {
		// Read user input
		input, _ := reader.ReadString('\n')
		input = strings.TrimSpace(input)

		if input == "" {
			continue
		}

		if input == "y" || input == "Y" {
			return true, nil
		} else if input == "n" || input == "N" {
			return false, nil
		}
	}
}
