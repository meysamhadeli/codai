package utils

import (
	"fmt"
	"github.com/charmbracelet/glamour"
)

// RenderAndPrintMarkdown handles the rendering of markdown content,
func RenderAndPrintMarkdown(content string, theme string) error {
	// Render markdown using Glamour
	md, err := glamour.Render(content, theme)
	if err != nil {
		return fmt.Errorf("error rendering markdown: %v", err)
	}

	// Print the rendered markdown
	fmt.Print(md)

	return nil
}
