package contracts

import (
	"github.com/meysamhadeli/codai/code_analyzer/models"
)

type ICodeAnalyzer interface {
	GetProjectFiles(rootDir string) ([]models.FileData, error)
	ProcessFile(filePath string, sourceCode []byte) []string
	ApplyChanges(relativePath string) error
}
