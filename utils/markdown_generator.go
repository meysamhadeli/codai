package utils

import (
	"fmt"
	"github.com/charmbracelet/glamour"
	"log"
	"strings"
)

// RenderAndPrintMarkdown renders Markdown with syntax highlighting using Glamour
func RenderAndPrintMarkdown(content string, inCodeBlock *bool, buffer *strings.Builder, theme string) {
	if content == "" {
		return
	}

	// Check for code block delimiters and toggle code block state
	if strings.Contains(content, "```") {
		if *inCodeBlock {
			*inCodeBlock = false
			buffer.WriteString("```") // Close code block
		} else {
			*inCodeBlock = true
			buffer.WriteString("```") // Open code block
		}
		content = strings.ReplaceAll(content, "```", "")
	}

	// Append the current content to the buffer
	buffer.WriteString(content)

	// Render and print if outside code block and content has a new line or sufficient length
	if !*inCodeBlock && len(content) > 10 {
		formatted := buffer.String()
		buffer.Reset()

		// Using Glamour to render Markdown with syntax highlighting
		rendered, err := glamour.NewTermRenderer(glamour.WithStylePath(theme))
		if err != nil {
			log.Printf("Error creating renderer: %v", err)
			return
		}

		// Render and print the formatted Markdown
		output, err := rendered.Render(strings.TrimSpace(formatted))
		if err != nil {
			log.Printf("Error rendering markdown: %v", err)
			return
		}
		fmt.Print(output)
	}
}
