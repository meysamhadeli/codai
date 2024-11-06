package code_analyzer

import (
	"encoding/json"
	"fmt"
	"github.com/charmbracelet/lipgloss"
	"github.com/meysamhadeli/codai/code_analyzer/contracts"
	"github.com/meysamhadeli/codai/code_analyzer/models"
	"github.com/meysamhadeli/codai/embed_data"
	"github.com/meysamhadeli/codai/utils"
	sitter "github.com/smacker/go-tree-sitter"
	"github.com/smacker/go-tree-sitter/csharp"
	"github.com/smacker/go-tree-sitter/golang"
	"github.com/smacker/go-tree-sitter/java"
	"github.com/smacker/go-tree-sitter/javascript"
	"github.com/smacker/go-tree-sitter/python"
	"github.com/smacker/go-tree-sitter/typescript/typescript"
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

// Define styles for the box
var (
	boxStyle = lipgloss.NewStyle().Border(lipgloss.NormalBorder()).Align(lipgloss.Center)
)

func (analyzer *CodeAnalyzer) GeneratePrompt(codes []string, history []string, userInput string, requestedContext string) (string, string) {

	// Combine the relevant code into a single string
	code := strings.Join(codes, "\n---------\n\n")

	prompt := fmt.Sprintf("%s\n\n______\n%s\n\n", fmt.Sprintf(boxStyle.Render("Here is the summary of context of project")+"\n\n%s", code), fmt.Sprintf(boxStyle.Render("Here is the general template prompt for using AI")+"\n\n%s", string(embed_data.CodeBlockTemplate)))
	if requestedContext != "" {
		prompt = prompt + fmt.Sprintf(boxStyle.Render("Here are the details of the context of the project that was requested for use in your task")+"\n\n%s", requestedContext)
	}

	userInputPrompt := fmt.Sprintf(boxStyle.Render("Here is user request")+"\n%s", userInput)
	historyPrompt := boxStyle.Render("Here is the history of chats") + "\n\n" + strings.Join(history, "\n---------\n\n")
	finalPrompt := fmt.Sprintf("%s\n\n______\n\n%s", historyPrompt, prompt)
	return finalPrompt, userInputPrompt
}

// NewCodeAnalyzer initializes a new CodeAnalyzer.
func NewCodeAnalyzer(cwd string) contracts.ICodeAnalyzer {
	return &CodeAnalyzer{Cwd: cwd}
}

// ApplyChanges updates or creates a file at the given relativePath with the specified code.
func (analyzer *CodeAnalyzer) ApplyChanges(relativePath, code string) error {
	// Ensure the directory structure exists
	dir := filepath.Dir(relativePath)
	if err := os.MkdirAll(dir, os.ModePerm); err != nil {
		return fmt.Errorf("failed to create directory: %w", err)
	}

	// Check if file exists
	if _, err := os.Stat(relativePath); os.IsNotExist(err) {
		// File does not exist, create and write code
		if err := ioutil.WriteFile(relativePath, []byte(code), 0644); err != nil {
			return fmt.Errorf("failed to create file: %w", err)
		}
	} else {
		// File exists, update the content
		if err := ioutil.WriteFile(relativePath, []byte(code), 0644); err != nil {
			return fmt.Errorf("failed to update file: %w", err)
		}
	}
	return nil
}

func (analyzer *CodeAnalyzer) GetProjectFiles(rootDir string) ([]models.FileData, []string, error) {
	var result []models.FileData
	var codes []string

	// Retrieve the ignore patterns from .gitignore, if it exists
	gitIgnorePatterns, err := utils.GetGitignorePatterns()
	if err != nil {
		return nil, nil, err
	}

	// Walk the directory tree and find all files
	err = filepath.WalkDir(rootDir, func(path string, d fs.DirEntry, err error) error {
		if err != nil {
			return err
		}

		relativePath, err := filepath.Rel(rootDir, path)
		relativePath = strings.ReplaceAll(relativePath, "\\", "/")

		// Check if the current directory or file should be skipped based on default ignore patterns
		if utils.IsDefaultIgnored(relativePath) {
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

			// Read the file content using the full path
			content, err := ioutil.ReadFile(path) // Use full path from WalkDir
			if err != nil {
				return fmt.Errorf("failed to read file: %s, error: %w", relativePath, err)
			}

			codeParts := analyzer.ProcessFile(relativePath, content)

			// Append the file data to the result
			result = append(result, models.FileData{RelativePath: relativePath, Code: string(content), TreeSitterCode: strings.Join(codeParts, "\n")})
			codes = append(codes, fmt.Sprintf("File: %s\n\n%s", relativePath, strings.Join(codeParts, "\n")))
		}

		return nil
	})

	if err != nil {
		return nil, nil, err
	}

	return result, codes, nil
}

// ProcessFile processes a single file using Tree-sitter for syntax analysis (for .cs files).
func (analyzer *CodeAnalyzer) ProcessFile(filePath string, sourceCode []byte) []string {
	var elements []string

	var parser *sitter.Parser
	var lang *sitter.Language
	var query []byte

	language := utils.GetSupportedLanguage(filePath)
	parser = sitter.NewParser()

	// Determine the parser and language to use
	switch language {
	case "csharp":
		parser.SetLanguage(csharp.GetLanguage())
		lang = csharp.GetLanguage()
		query = embed_data.CSharpQuery
	case "go":
		parser.SetLanguage(golang.GetLanguage())
		lang = golang.GetLanguage()
		query = embed_data.GoQuery
	case "python":
		parser.SetLanguage(python.GetLanguage())
		lang = python.GetLanguage()
		query = embed_data.PythonQuery
	case "java":
		parser.SetLanguage(java.GetLanguage())
		lang = java.GetLanguage()
		query = embed_data.JavaQuery
	case "javascript":
		parser.SetLanguage(javascript.GetLanguage())
		lang = javascript.GetLanguage()
		query = embed_data.JavascriptQuery
	case "typescript":
		parser.SetLanguage(typescript.GetLanguage())
		lang = typescript.GetLanguage()
		query = embed_data.TypescriptQuery
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
		log.Fatalf("failed to parse JSON: %v", err)
	}

	elements = append(elements, filePath)

	// Execute each query and capture results
	for tag, queryStr := range queries {
		query, err := sitter.NewQuery([]byte(queryStr), lang) // Use the appropriate language
		if err != nil {
			log.Fatalf("failed to compile query: %v", err)
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
	if text == "" {
		return nil, nil
	}

	// Regex patterns
	filePathPattern := regexp.MustCompile(`(?i)(?:.*?File:\s*)([^\s*]+?\.[a-zA-Z0-9]+)\b`)
	codeBlockPattern := regexp.MustCompile("(?s)```[a-zA-Z0-9]*\\s*(.*?)\\s*```")

	var codeChanges []models.CodeChange

	// Find all file path matches and code block matches
	filePathMatches := filePathPattern.FindAllStringSubmatch(text, -1)
	codeMatches := codeBlockPattern.FindAllStringSubmatch(text, -1)

	// Ensure pairs are processed up to the minimum count of matches
	minLength := len(filePathMatches)
	if len(codeMatches) < minLength {
		minLength = len(codeMatches)
	}

	// Create code changes up to the minimum length
	for i := 0; i < minLength; i++ {
		relativePath := strings.TrimSpace(filePathMatches[i][1])
		code := strings.TrimSpace(codeMatches[i][1])

		codeChange := models.CodeChange{
			RelativePath: relativePath,
			Code:         code,
		}
		codeChanges = append(codeChanges, codeChange)
	}

	return codeChanges, nil
}

func (analyzer *CodeAnalyzer) TryGetInCompletedCodeBlocK(relativePaths string) (string, error) {
	var codes []string

	// Simplified regex to capture only the array of files
	re := regexp.MustCompile(`\[.*?\]`)
	match := re.FindString(relativePaths)

	if match == "" {
		return "", fmt.Errorf("no file paths found in input")
	}

	// Parse the match into a slice of strings
	var filePaths []string
	err := json.Unmarshal([]byte(match), &filePaths)
	if err != nil {
		return "", fmt.Errorf("failed to unmarshal JSON: %v", err)
	}

	// Loop through each relative path and read the file content
	for _, relativePath := range filePaths {
		content, err := os.ReadFile(relativePath)
		if err != nil {
			continue
		}

		codes = append(codes, fmt.Sprintf("File: %s\n\n%s", relativePath, content))
	}

	if len(codes) == 0 {
		return "", fmt.Errorf("no valid files read")
	}

	requestedContext := strings.Join(codes, "\n---------\n\n")

	return requestedContext, nil
}
