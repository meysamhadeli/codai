package utils

import (
	"bufio"
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss_color"
	"os"
	"strings"
)

// PromptUser Prompt the user to accept or reject the changes
func PromptUser(path string) (bool, error) {

	reader := bufio.NewReader(os.Stdin)
	for {
		fmt.Printf(lipgloss_color.Violet.Render(fmt.Sprintf("Do you want to accept the changes for %s? (y/n): ", path)))

		fmt.Print()
		input, _ := reader.ReadString('\n')
		input = strings.TrimSpace(input)

		if input == "y" || input == "Y" {
			return true, nil
		} else if input == "n" || input == "N" {
			return false, nil
		}
		// Invalid input, ask again
		return false, fmt.Errorf("invalid input. Please enter 'y' or 'n'")
	}
}
