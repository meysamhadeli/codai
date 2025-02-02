package utils

import (
	"fmt"
	"github.com/alecthomas/chroma/v2/quick"
	"os"
	"strings"
)

var isCodeBlock = false

// RenderAndPrintMarkdown handles the rendering of markdown content,
func RenderAndPrintMarkdown(line string, language string, theme string) error {

	if strings.HasPrefix(line, "```") {
		isCodeBlock = !isCodeBlock
	}
	// Process the line based on its prefix
	if strings.HasPrefix(line, "+") && isCodeBlock {
		line = "\x1b[92m" + line + "\x1b[0m"
		fmt.Print(line)
	} else if strings.HasPrefix(line, "-") && isCodeBlock {
		line = "\x1b[91m" + line + "\x1b[0m"
		fmt.Print(line)
	} else {
		// Render the processed line
		err := quick.Highlight(os.Stdout, line, language, "terminal256", theme)
		if err != nil {
			return err
		}
	}

	return nil
}
