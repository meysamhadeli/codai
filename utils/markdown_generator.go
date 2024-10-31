package utils

import (
	"github.com/alecthomas/chroma/v2/quick"
	"os"
)

// RenderAndPrintMarkdown handles the rendering of markdown content,
func RenderAndPrintMarkdown(content string, language string, theme string) error {

	err := quick.Highlight(os.Stdout, content, language, "terminal256", theme)
	if err != nil {
		return err
	}
	return nil
}
