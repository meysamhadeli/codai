package utils

import (
	"bufio"
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss"
	"strings"
)

// ConfirmPrompt prompts the user to accept or reject the changes in a file path
func ConfirmPrompt(path string, reader *bufio.Reader) (bool, error) {

	// Styled prompt message
	fmt.Print("\r")
	fmt.Printf(lipgloss.BlueSky.Render(fmt.Sprintf("Do you want to accept the change for file %v%s", lipgloss.LightBlueB.Render(path), lipgloss.BlueSky.Render(" ? (y/n): "))))

	// Read user input
	input, _ := reader.ReadString('\n')
	input = strings.TrimSpace(input)

	if input == "y" || input == "Y" {
		return true, nil
	}

	return false, nil
}

// ConfirmAdditinalContext prompts the user to accept or reject additional context
func ConfirmAdditinalContext(reader *bufio.Reader) (bool, error) {

	// Styled prompt message
	fmt.Print("\r")
	fmt.Printf(lipgloss.Gray.Render(fmt.Sprintf("Do you want to add above files to context %s", lipgloss.Gray.Render("? (y/n): "))))

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
