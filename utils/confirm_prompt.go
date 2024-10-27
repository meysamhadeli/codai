package utils

import (
	"bufio"
	"fmt"
	"github.com/charmbracelet/lipgloss"
	"github.com/meysamhadeli/codai/constants/lipgloss_color"
	"os"
	"strings"
)

// ConfirmPrompt PromptUser prompts the user to accept or reject the changes with a charming interface
func ConfirmPrompt(path string) error {
	reader := bufio.NewReader(os.Stdin)

	// Define charming styles for the prompt
	charmStyle := lipgloss.NewStyle().Foreground(lipgloss.Color("205")).Bold(true)
	responseStyle := lipgloss.NewStyle().Foreground(lipgloss.Color("76"))  // Green color for positive response
	negativeStyle := lipgloss.NewStyle().Foreground(lipgloss.Color("196")) // Red color for negative response

	for {
		// Styled prompt message
		fmt.Printf(charmStyle.Render(fmt.Sprintf("🌟 Do you want to accept the changes for file: `%s`? (y/n): ", path)))

		// Read user input
		input, _ := reader.ReadString('\n')
		input = strings.TrimSpace(input)

		if input == "y" || input == "Y" {
			fmt.Println(responseStyle.Render("✔️ Changes accepted!"))
			return nil
		} else if input == "n" || input == "N" {
			fmt.Println(negativeStyle.Render("❌ Changes rejected."))
			return nil
		}

		// Invalid input, ask again with charm
		fmt.Println(lipgloss_color.Red.Render("🚫 Invalid input. Please enter 'y' or 'n'."))
	}
}
