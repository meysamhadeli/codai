package code_analyzer

import (
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/code_analyzer/contracts"
	"github.com/meysamhadeli/codai/code_analyzer/models"
	"github.com/meysamhadeli/codai/embed_data"
	"github.com/meysamhadeli/codai/utils"
	sitter "github.com/smacker/go-tree-sitter"
	"github.com/smacker/go-tree-sitter/csharp"
	"github.com/smacker/go-tree-sitter/golang"
	"io/fs"
	"io/ioutil"
	"log"
	"os"
	"path/filepath"
	"regexp"
	"strings"
)

// CodeAnalyzer handles the analysis of project files.
type CodeAnalyzer struct {
	Cwd string
}

// NewCodeAnalyzer initializes a new CodeAnalyzer.
func NewCodeAnalyzer(cwd string) contracts.ICodeAnalyzer {
	return &CodeAnalyzer{Cwd: cwd}
}

// ApplyChanges Apply changes by replacing original files with the temp files
func (analyzer *CodeAnalyzer) ApplyChanges(relativePath string) error {
	tempPath := relativePath + ".tmp"
	// Replace the original file with the temp file
	err := os.Rename(tempPath, relativePath)
	if err != nil {
		return fmt.Errorf("failed to apply changes to file %s: %v", relativePath, err)
	}
	return nil
}

func (analyzer *CodeAnalyzer) GetProjectFiles(rootDir string) ([]models.FileData, error) {
	var result []models.FileData

	// Retrieve the ignore patterns from .gitignore, if it exists
	gitIgnorePatterns, err := utils.GetGitignorePatterns()
	if err != nil {
		return nil, err
	}

	// Walk the directory tree and find all files
	err = filepath.WalkDir(rootDir, func(path string, d fs.DirEntry, err error) error {
		if err != nil {
			return err
		}

		relativePath, err := filepath.Rel(rootDir, path)

		// Check if the current directory or file should be skipped based on default ignore patterns
		if utils.IsDefaultIgnored(path) {
			// Skip the directory or file
			if d.IsDir() {
				// If it's a directory, skip the whole directory
				return filepath.SkipDir
			}
			// If it's a file, just skip the file
			return nil
		}

		// Ensure that the current entry is a file, not a directory
		if !d.IsDir() {

			if err != nil {
				return err
			}
			if utils.IsGitIgnored(relativePath, gitIgnorePatterns) {
				// Debugging: Print the ignored file
				return nil // Skip this file
			}

			// Read the file content
			content, err := ioutil.ReadFile(relativePath)
			if err != nil {
				return fmt.Errorf("failed to read file: %s, error: %w", relativePath, err)
			}

			codeParts := analyzer.ProcessFile(relativePath, content)

			// Append the file data to the result
			result = append(result, models.FileData{RelativePath: relativePath, Code: string(content), TreeSitterCode: strings.Join(codeParts, "\n")})
		}

		return nil
	})

	if err != nil {
		return nil, err
	}

	return result, nil
}

// ProcessFile processes a single file using Tree-sitter for syntax analysis (for .cs files).
func (analyzer *CodeAnalyzer) ProcessFile(filePath string, sourceCode []byte) []string {
	var elements []string

	var parser *sitter.Parser
	var lang *sitter.Language
	var query []byte

	language := utils.GetSupportedLanguage(filePath)

	// Determine the parser and language to use
	switch language {
	case "csharp":
		parser = sitter.NewParser()
		parser.SetLanguage(csharp.GetLanguage())
		lang = csharp.GetLanguage()
		query = embed_data.CSharpQuery
	case "go":
		parser = sitter.NewParser()
		parser.SetLanguage(golang.GetLanguage())
		lang = golang.GetLanguage()
		query = embed_data.GolangQuery
	default:
		// If the language doesn't match, process the original source code directly
		elements = append(elements, filePath)
		elements = append(elements, string(sourceCode)) // Adding original source code
		return elements
	}

	// Parse the source code
	tree := parser.Parse(nil, sourceCode)

	// Parse JSON data into a map
	queries := make(map[string]string)
	err := json.Unmarshal(query, &queries)
	if err != nil {
		log.Fatalf("Failed to parse JSON: %v", err)
	}

	elements = append(elements, filePath)

	// Execute each query and capture results
	for tag, queryStr := range queries {
		query, err := sitter.NewQuery([]byte(queryStr), lang) // Use the appropriate language
		if err != nil {
			log.Fatalf("Failed to compile query: %v", err)
		}

		cursor := sitter.NewQueryCursor()
		cursor.Exec(query, tree.RootNode())

		// Collect the results of the query
		for {
			match, ok := cursor.NextMatch()
			if !ok {
				break
			}

			for _, cap := range match.Captures {
				element := cap.Node.Content(sourceCode)
				// Tag the element with its type (e.g., namespace, class, method, interface)
				taggedElement := fmt.Sprintf("%s\n: %s", tag, element)
				elements = append(elements, taggedElement)
			}
		}
	}

	return elements
}

// ExtractCodeChanges extracts code changes from the given text.
func (analyzer *CodeAnalyzer) ExtractCodeChanges(text string) ([]models.CodeChange, error) {
	// Validate the input text
	if text == "" {
		return nil, errors.New("input text is empty")
	}

	// Regex to match the relative file path (e.g., File: tests\fakes\Foo.cs)
	filePathPattern := regexp.MustCompile(`(?i)[^*]*?File:\s*([^\n*]+?\.[^\s*]+)`) // Matches any characters before 'File:'
	// Regex to match code blocks (we don't care about language now, just the code content)
	codeBlockPattern := regexp.MustCompile("(?s)```[a-zA-Z0-9]*\\s*(.*?)\\s*```")

	var codeChanges []models.CodeChange

	// Find all file path matches
	filePathMatches := filePathPattern.FindAllStringSubmatch(text, -1)
	// Find all code block matches
	codeMatches := codeBlockPattern.FindAllStringSubmatch(text, -1)

	// Ensure there is a one-to-one correspondence between file paths and code blocks
	if len(filePathMatches) == len(codeMatches) {
		for i, match := range filePathMatches {
			// Extract relative path
			relativePath := strings.TrimSpace(match[1])

			// Extract the code block content
			code := strings.TrimSpace(codeMatches[i][1])

			// Create a new CodeChange struct and append it to the array
			codeChange := models.CodeChange{
				RelativePath: relativePath,
				Code:         code,
			}
			codeChanges = append(codeChanges, codeChange)
		}
	}

	return codeChanges, nil
}
