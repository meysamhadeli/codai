package contracts

import (
	"github.com/meysamhadeli/codai/code_analyzer/models"
)

type ICodeAnalyzer interface {
	GetProjectFiles(rootDir string) ([]models.FileData, []string, error)
	ProcessFile(filePath string, sourceCode []byte) []string
	GeneratePrompt(codes []string, history []string, userInput string, requestedContext string) (string, string)
	ExtractCodeChanges(text string) ([]models.CodeChange, error)
	ApplyChanges(relativePath string) error
	TryGetInCompletedCodeBlocK(relativePaths string) (string, error)
}
