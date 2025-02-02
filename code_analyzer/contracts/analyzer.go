package contracts

import (
	"github.com/meysamhadeli/codai/code_analyzer/models"
)

type ICodeAnalyzer interface {
	GetProjectFiles(rootDir string) (*models.FullContextData, error)
	ProcessFile(filePath string, sourceCode []byte) []string
	GeneratePrompt(codes []string, history []string, userInput string, requestedContext string) (string, string)
	ExtractCodeChanges(text string) []models.CodeChange
	ApplyChanges(relativePath, code string) error
	TryGetInCompletedCodeBlocK(relativePaths string) (string, error)
}
