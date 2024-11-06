package code_analyzer

import (
	"fmt"
	"github.com/meysamhadeli/codai/code_analyzer/contracts"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/stretchr/testify/assert"
)

// Global variables to store the relative test directory and analyzer
var (
	relativePathTestDir string
	analyzer            contracts.ICodeAnalyzer
)

// setup initializes the relative test directory for all tests
func setup(t *testing.T) {
	rootDir, err := os.Getwd()
	assert.NoError(t, err)

	testDir := t.TempDir() // Create a temporary directory
	relativePathTestDir, err = filepath.Rel(rootDir, testDir)

	if filepath.IsAbs(relativePathTestDir) {
		t.Fatalf("relativeTestDir should be relative, but got an absolute path: %s", relativePathTestDir)
	}

	analyzer = NewCodeAnalyzer(relativePathTestDir)

	// Register cleanup to remove everything inside relativePathTestDir
	t.Cleanup(func() {
		err := os.RemoveAll(relativePathTestDir)
		assert.NoError(t, err, "failed to remove test directory")
	})
}

// TestMain runs tests sequentially in the specified order
func TestMain(m *testing.M) {
	// Setup before running tests
	code := m.Run()
	// Teardown after running tests (if needed)
	os.Exit(code)
}

func TestRunInSequence(t *testing.T) {
	setup(t) // setup before the first test runs

	// Call tests in order
	t.Run("TestGeneratePrompt", TestGeneratePrompt)
	t.Run("TestGeneratePrompt_ActualImplementation", TestGeneratePrompt_ActualImplementation)
	t.Run("TestNewCodeAnalyzer", TestNewCodeAnalyzer)
	t.Run("TestGetProjectFiles", TestGetProjectFiles)
	t.Run("TestProcessFile", TestProcessFile)
	t.Run("TestExtractCodeChanges", TestExtractCodeChanges)
	t.Run("TestTryGetInCompletedCodeBlock", TestTryGetInCompletedCodeBlock)
	t.Run("TestTryGetInCompletedCodeBlockWithAdditionalCharacters", TestTryGetInCompletedCodeBlockWithAdditionalsCharacters)
}

func TestGeneratePrompt(t *testing.T) {
	// Call the setup function to initialize the test environment
	setup(t)

	codes := []string{"code1", "code2"}
	history := []string{"prev1", "prev2"}
	requestedContext := "Requested context"
	userInput := "User request"

	finalPrompt, userInputPrompt := analyzer.GeneratePrompt(codes, history, userInput, requestedContext)

	// Assert that the outputs contain the expected mocked strings
	assert.Contains(t, finalPrompt, "code1")
	assert.Contains(t, finalPrompt, "code2")
	assert.Contains(t, finalPrompt, "prev1")
	assert.Contains(t, finalPrompt, "prev2")
	assert.Contains(t, finalPrompt, "Requested context")
	assert.Contains(t, userInputPrompt, "User request")
}

func TestGeneratePrompt_ActualImplementation(t *testing.T) {
	setup(t)

	// Assuming boxStyle.Render and embed_data.CodeBlockTemplate are set up correctly
	codes := []string{"code1", "code2"}
	history := []string{"prev1", "prev2"}
	userInput := "User request"
	requestedContext := "Requested context"

	finalPrompt, userInputPrompt := analyzer.GeneratePrompt(codes, history, userInput, requestedContext)

	// Check the content of the actual prompts here
	// This will depend on how you set up boxStyle and embed_data
	assert.NotEmpty(t, finalPrompt)
	assert.NotEmpty(t, userInputPrompt)
}

// Test for NewCodeAnalyzer
func TestNewCodeAnalyzer(t *testing.T) {
	setup(t)

	assert.NotNil(t, analyzer)
}

// Test for GetProjectFiles
func TestGetProjectFiles(t *testing.T) {
	setup(t)

	testFilePath := filepath.Join(relativePathTestDir, "test.go")
	ignoreFilePath := filepath.Join(relativePathTestDir, ".gitignore")

	_ = os.WriteFile(testFilePath, []byte("package main\nfunc main() {}"), 0644)
	_ = os.WriteFile(ignoreFilePath, []byte("node_modules\n"), 0644)

	files, codes, err := analyzer.GetProjectFiles(relativePathTestDir)

	assert.NoError(t, err)
	assert.Len(t, files, 1)
	assert.Len(t, codes, 1)

	for _, file := range files {
		assert.NotEmpty(t, file.RelativePath)
		assert.Equal(t, "test.go", filepath.Base(file.RelativePath))
	}
}

// Test for ProcessFile
func TestProcessFile(t *testing.T) {
	setup(t)
	content := []byte("class Test {}")

	result := analyzer.ProcessFile("test.cs", content)

	assert.Contains(t, result, "test.cs")
	assert.NotEmpty(t, result)
}

// TestApplyChanges_NewFile tests if ApplyChanges creates a new file when it doesn't exist.
func TestApplyChanges_NewFile(t *testing.T) {
	setup(t)

	// Define the relative path for a new file and its content
	filePath := filepath.Join(relativePathTestDir, "newfile.go")
	content := "package main\nfunc main() {}"

	// Call ApplyChanges to create the new file
	err := analyzer.ApplyChanges(filePath, content)
	assert.NoError(t, err)

	// Verify the file was created with the expected content
	savedContent, err := os.ReadFile(filePath)
	assert.NoError(t, err)
	assert.Equal(t, content, string(savedContent))
}

// TestApplyChanges_ModifyFile tests if ApplyChanges updates content of an existing file.
func TestApplyChanges_ModifyFile(t *testing.T) {
	setup(t)

	// Define the relative path and initial content for an existing file
	filePath := filepath.Join(relativePathTestDir, "existingfile.go")
	initialContent := "package main\nfunc main() {}"
	modifiedContent := "package main\nfunc updatedMain() {}"

	// Create the file with initial content
	err := os.WriteFile(filePath, []byte(initialContent), 0644)
	assert.NoError(t, err)

	// Use ApplyChanges to modify the content
	err = analyzer.ApplyChanges(filePath, modifiedContent)
	assert.NoError(t, err)

	// Verify that the file content was modified
	savedContent, err := os.ReadFile(filePath)
	assert.NoError(t, err)
	assert.Equal(t, modifiedContent, string(savedContent))
}

// TestApplyChanges_DeletedFile tests if ApplyChanges re-creates a file if it was deleted.
func TestApplyChanges_DeletedFile(t *testing.T) {
	setup(t)

	// Define the relative path and content for the file
	filePath := filepath.Join(relativePathTestDir, "deletedfile.go")
	content := "package main\nfunc deletedMain() {}"

	// Initially create the file and verify its existence
	err := os.WriteFile(filePath, []byte(content), 0644)
	assert.NoError(t, err)
	assert.FileExists(t, filePath)

	// Delete the file to simulate the "file missing" condition
	err = os.Remove(filePath)
	assert.NoError(t, err)
	assert.NoFileExists(t, filePath)

	// Use ApplyChanges to recreate the file
	err = analyzer.ApplyChanges(filePath, content)
	assert.NoError(t, err)

	// Verify the file was recreated with the correct content
	savedContent, err := os.ReadFile(filePath)
	assert.NoError(t, err)
	assert.Equal(t, content, string(savedContent))
}

// Test for ExtractCodeChanges
func TestExtractCodeChanges(t *testing.T) {
	setup(t)
	text := "File: test.go\n```go\npackage main\n```\nFile: test2.go\n```go\npackage main\n```"

	codeChanges, err := analyzer.ExtractCodeChanges(text)

	assert.NoError(t, err)
	assert.Len(t, codeChanges, 2)
	assert.Equal(t, "test.go", codeChanges[0].RelativePath)
	assert.Equal(t, "package main", codeChanges[0].Code)
}

func TestExtractCodeChangesWithAdditionalsCharacters(t *testing.T) {
	setup(t)
	text := "\n\n#####File: test.go#####\n```go\npackage main\n```\nFile: test2.go\n```go\npackage main\n```"

	codeChanges, err := analyzer.ExtractCodeChanges(text)

	assert.NoError(t, err)
	assert.Len(t, codeChanges, 2)
	assert.Equal(t, "test.go", codeChanges[0].RelativePath)
	assert.Equal(t, "package main", codeChanges[0].Code)
}

func TestExtractCodeChangesWithRemoveCharacters(t *testing.T) {
	setup(t)
	text := "file:test.go\n```go\npackage main\n```\nFile: test2.go\n```go\npackage main\n```"

	codeChanges, err := analyzer.ExtractCodeChanges(text)

	assert.NoError(t, err)
	assert.Len(t, codeChanges, 2)
	assert.Equal(t, "test.go", codeChanges[0].RelativePath)
	assert.Equal(t, "package main", codeChanges[0].Code)
}

// Test for TryGetInCompletedCodeBlock
func TestTryGetInCompletedCodeBlock(t *testing.T) {
	setup(t) // setup before the first test runs

	// Create relative paths for test files within the temporary directory
	file1Path := strings.ReplaceAll(filepath.Join(relativePathTestDir, "test.go"), `\`, `\\`)
	file2Path := strings.ReplaceAll(filepath.Join(relativePathTestDir, "test2.go"), `\`, `\\`)

	_ = os.WriteFile(file1Path, []byte("package main\nfunc main() {}"), 0644)
	_ = os.WriteFile(file2Path, []byte("package test\nfunc test() {}"), 0644)

	// Prepare JSON-encoded relativePaths string with escaped backslashes
	relativePaths := fmt.Sprintf(`["%s", "%s"]`, file1Path, file2Path)

	requestedContext, err := analyzer.TryGetInCompletedCodeBlocK(relativePaths)

	// Assertions
	assert.NoError(t, err)
	assert.NotEmpty(t, requestedContext)
	assert.Contains(t, requestedContext, "package main\nfunc main() {}")
	assert.Contains(t, requestedContext, "package test\nfunc test() {}")
}

// Test for TryGetInCompletedCodeBlock
func TestTryGetInCompletedCodeBlockWithAdditionalsCharacters(t *testing.T) {
	setup(t) // setup before the first test runs

	// Create relative paths for test files within the temporary directory
	file1Path := strings.ReplaceAll(filepath.Join(relativePathTestDir, "test.go"), `\`, `\\`)
	file2Path := strings.ReplaceAll(filepath.Join(relativePathTestDir, "test2.go"), `\`, `\\`)

	_ = os.WriteFile(file1Path, []byte("package main\nfunc main() {}"), 0644)
	_ = os.WriteFile(file2Path, []byte("package test\nfunc test() {}"), 0644)

	// Prepare JSON-encoded relativePaths string with escaped backslashes
	relativePaths := fmt.Sprintf(`{"###file":["%s", "%s"]\n\n}`, file1Path, file2Path)

	requestedContext, err := analyzer.TryGetInCompletedCodeBlocK(relativePaths)

	// Assertions
	assert.NoError(t, err)
	assert.NotEmpty(t, requestedContext)
	assert.Contains(t, requestedContext, "package main\nfunc main() {}")
	assert.Contains(t, requestedContext, "package test\nfunc test() {}")
}
