package contracts

import (
	"github.com/meysamhadeli/codai/markdown_generators/models"
)

type IMarkdownGenerator interface {
	GenerateMarkdown(results string) error
	GenerateDiff(change models.CodeChange) error
	ExtractCodeChanges(text string) ([]models.CodeChange, error)
}
