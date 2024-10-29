package utils

import (
	"bufio"
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss_color"
	"os"
	"strings"
)

// ConfirmPrompt PromptUser prompts the user to accept or reject the changes with a charming interface
func ConfirmPrompt(path string) error {
	reader := bufio.NewReader(os.Stdin)

	// Styled prompt message
	fmt.Printf(lipgloss_color.BlueSky.Render(fmt.Sprintf("Do you want to accept the change for file %v%v", lipgloss_color.LightBlueB.Render(path), lipgloss_color.BlueSky.Render(" ? (y/n): "))))

	for {
		// Read user input
		input, _ := reader.ReadString('\n')
		input = strings.TrimSpace(input)

		if input == "" {
			continue
		}

		if input == "y" || input == "Y" {
			fmt.Println(lipgloss_color.Green.Render("✔️ Changes accepted!"))
			return nil
		} else if input == "n" || input == "N" {
			fmt.Println(lipgloss_color.Red.Render("❌ Changes rejected."))
			return nil
		}
	}
}
