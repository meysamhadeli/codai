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

	analyzer = NewCodeAnalyzer(relativePathTestDir, true)

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

// / Test for ExtractCodeChanges with standard input
func TestExtractCodeChangesComplexText(t *testing.T) {
	setup(t)
	text := "Sure, I can help you create a simple Pacman game using Python. We'll use the `pygame` library for this purpose. If you don't have `pygame` installed, you can install it using `pip install pygame`.\n\nHere is a basic implementation of a Pacman game:\n\nFile: pacman_game.py\n```python\nimport pygame\nimport random\n\n# Initialize pygame\npygame.init()\n\n# Screen dimensions\nSCREEN_WIDTH = 800\nSCREEN_HEIGHT = 600\n\n# Colors\nBLACK = (0, 0, 0)\nWHITE = (255, 255, 255)\nYELLOW = (255, 255, 0)\nRED = (255, 0, 0)\n\n# Pacman settings\nPACMAN_SIZE = 50\nPACMAN_SPEED = 5\n\n# Ghost settings\nGHOST_SIZE = 50\nGHOST_SPEED = 3\n\n# Create the screen\nscreen = pygame.display.set_mode((SCREEN_WIDTH, SCREEN_HEIGHT))\npygame.display.set_caption(\"Pacman Game\")\n\n# Load images\npacman_image = pygame.image.load(\"pacman.png\")\npacman_image = pygame.transform.scale(pacman_image, (PACMAN_SIZE, PACMAN_SIZE))\n\nghost_image = pygame.image.load(\"ghost.png\")\nghost_image = pygame.transform.scale(ghost_image, (GHOST_SIZE, GHOST_SIZE))\n\n# Pacman class\nclass Pacman:\n    def __init__(self):\n        self.x = SCREEN_WIDTH // 2\n        self.y = SCREEN_HEIGHT // 2\n        self.speed = PACMAN_SPEED\n        self.image = pacman_image\n\n    def move(self, dx, dy):\n        self.x += dx * self.speed\n        self.y += dy * self.speed\n\n        # Boundary check\n        if self.x < 0:\n            self.x = 0\n        elif self.x > SCREEN_WIDTH - PACMAN_SIZE:\n            self.x = SCREEN_WIDTH - PACMAN_SIZE\n\n        if self.y < 0:\n            self.y = 0\n        elif self.y > SCREEN_HEIGHT - PACMAN_SIZE:\n            self.y = SCREEN_HEIGHT - PACMAN_SIZE\n\n    def draw(self):\n        screen.blit(self.image, (self.x, self.y))\n\n# Ghost class\nclass Ghost:\n    def __init__(self):\n        self.x = random.randint(0, SCREEN_WIDTH - GHOST_SIZE)\n        self.y = random.randint(0, SCREEN_HEIGHT - GHOST_SIZE)\n        self.speed = GHOST_SPEED\n        self.image = ghost_image\n\n    def move(self):\n        self.x += random.choice([-1, 1]) * self.speed\n        self.y += random.choice([-1, 1]) * self.speed\n\n        # Boundary check\n        if self.x < 0:\n            self.x = 0\n        elif self.x > SCREEN_WIDTH - GHOST_SIZE:\n            self.x = SCREEN_WIDTH - GHOST_SIZE\n\n        if self.y < 0:\n            self.y = 0\n        elif self.y > SCREEN_HEIGHT - GHOST_SIZE:\n            self.y = SCREEN_HEIGHT - GHOST_SIZE\n\n    def draw(self):\n        screen.blit(self.image, (self.x, self.y))\n\n# Main game loop\ndef main():\n    clock = pygame.time.Clock()\n    pacman = Pacman()\n    ghosts = [Ghost() for _ in range(4)]\n\n    running = True\n    while running:\n        for event in pygame.event.get():\n            if event.type == pygame.QUIT:\n                running = False\n\n        keys = pygame.key.get_pressed()\n        dx = dy = 0\n        if keys[pygame.K_LEFT]:\n            dx = -1\n        if keys[pygame.K_RIGHT]:\n            dx = 1\n        if keys[pygame.K_UP]:\n            dy = -1\n        if keys[pygame.K_DOWN]:\n            dy = 1\n\n        pacman.move(dx, dy)\n\n        for ghost in ghosts:\n            ghost.move()\n\n        screen.fill(BLACK)\n        pacman.draw()\n        for ghost in ghosts:\n            ghost.draw()\n\n        pygame.display.flip()\n        clock.tick(30)\n\n    pygame.quit()\n\nif __name__ == \"__main__\":\n    main()\n```\n\nThis code creates a simple Pacman game where Pacman can move around the screen using the arrow keys, and there are ghosts that move randomly. You need to have `pacman.png` and `ghost.png` images in the same directory as the script for it to work.\n\nTo run the game, save the code in a file named `pacman_game.py` and execute it with Python:\n\n```sh\npython pacman_game.py\n```\n\nEnjoy your game!"

	codeChanges := analyzer.ExtractCodeChanges(text)

	assert.Len(t, codeChanges, 1)
	assert.Equal(t, "pacman_game.py", codeChanges[0].RelativePath)
	assert.Equal(t, "import pygame\nimport random\n\n# Initialize pygame\npygame.init()\n\n# Screen dimensions\nSCREEN_WIDTH = 800\nSCREEN_HEIGHT = 600\n\n# Colors\nBLACK = (0, 0, 0)\nWHITE = (255, 255, 255)\nYELLOW = (255, 255, 0)\nRED = (255, 0, 0)\n\n# Pacman settings\nPACMAN_SIZE = 50\nPACMAN_SPEED = 5\n\n# Ghost settings\nGHOST_SIZE = 50\nGHOST_SPEED = 3\n\n# Create the screen\nscreen = pygame.display.set_mode((SCREEN_WIDTH, SCREEN_HEIGHT))\npygame.display.set_caption(\"Pacman Game\")\n\n# Load images\npacman_image = pygame.image.load(\"pacman.png\")\npacman_image = pygame.transform.scale(pacman_image, (PACMAN_SIZE, PACMAN_SIZE))\n\nghost_image = pygame.image.load(\"ghost.png\")\nghost_image = pygame.transform.scale(ghost_image, (GHOST_SIZE, GHOST_SIZE))\n\n# Pacman class\nclass Pacman:\n    def __init__(self):\n        self.x = SCREEN_WIDTH // 2\n        self.y = SCREEN_HEIGHT // 2\n        self.speed = PACMAN_SPEED\n        self.image = pacman_image\n\n    def move(self, dx, dy):\n        self.x += dx * self.speed\n        self.y += dy * self.speed\n\n        # Boundary check\n        if self.x < 0:\n            self.x = 0\n        elif self.x > SCREEN_WIDTH - PACMAN_SIZE:\n            self.x = SCREEN_WIDTH - PACMAN_SIZE\n\n        if self.y < 0:\n            self.y = 0\n        elif self.y > SCREEN_HEIGHT - PACMAN_SIZE:\n            self.y = SCREEN_HEIGHT - PACMAN_SIZE\n\n    def draw(self):\n        screen.blit(self.image, (self.x, self.y))\n\n# Ghost class\nclass Ghost:\n    def __init__(self):\n        self.x = random.randint(0, SCREEN_WIDTH - GHOST_SIZE)\n        self.y = random.randint(0, SCREEN_HEIGHT - GHOST_SIZE)\n        self.speed = GHOST_SPEED\n        self.image = ghost_image\n\n    def move(self):\n        self.x += random.choice([-1, 1]) * self.speed\n        self.y += random.choice([-1, 1]) * self.speed\n\n        # Boundary check\n        if self.x < 0:\n            self.x = 0\n        elif self.x > SCREEN_WIDTH - GHOST_SIZE:\n            self.x = SCREEN_WIDTH - GHOST_SIZE\n\n        if self.y < 0:\n            self.y = 0\n        elif self.y > SCREEN_HEIGHT - GHOST_SIZE:\n            self.y = SCREEN_HEIGHT - GHOST_SIZE\n\n    def draw(self):\n        screen.blit(self.image, (self.x, self.y))\n\n# Main game loop\ndef main():\n    clock = pygame.time.Clock()\n    pacman = Pacman()\n    ghosts = [Ghost() for _ in range(4)]\n\n    running = True\n    while running:\n        for event in pygame.event.get():\n            if event.type == pygame.QUIT:\n                running = False\n\n        keys = pygame.key.get_pressed()\n        dx = dy = 0\n        if keys[pygame.K_LEFT]:\n            dx = -1\n        if keys[pygame.K_RIGHT]:\n            dx = 1\n        if keys[pygame.K_UP]:\n            dy = -1\n        if keys[pygame.K_DOWN]:\n            dy = 1\n\n        pacman.move(dx, dy)\n\n        for ghost in ghosts:\n            ghost.move()\n\n        screen.fill(BLACK)\n        pacman.draw()\n        for ghost in ghosts:\n            ghost.draw()\n\n        pygame.display.flip()\n        clock.tick(30)\n\n    pygame.quit()\n\nif __name__ == \"__main__\":\n    main()", codeChanges[0].Code)
}

// / Test for ExtractCodeChanges with standard input
func TestExtractCodeChangesComplexText2(t *testing.T) {
	setup(t)
	text := "Sure, I can help you create a simple \"Dino Game\" using Python and the Pygame library. This game will be similar to the Chrome Dino game where a dinosaur jumps over obstacles.\n\nFirst, ensure you have Pygame installed. You can install it using pip:\n```sh\npip install pygame\n```\n\nNow, let's create the game. We will need a few files:\n1. `main.py` - The main game logic.\n2. `dino.py` - The Dino character logic.\n3. `obstacle.py` - The obstacle logic.\n4. `settings.py` - Game settings and configurations.\n\n### File: `settings.py`\n```python\n# Game settings and configurations\n\nSCREEN_WIDTH = 800\nSCREEN_HEIGHT = 400\nFPS = 30\n\n# Colors\nWHITE = (255, 255, 255)\nBLACK = (0, 0, 0)\n\n# Dino settings\nDINO_WIDTH = 40\nDINO_HEIGHT = 60\nDINO_COLOR = (0, 255, 0)\nDINO_JUMP_HEIGHT = 10\n\n# Obstacle settings\nOBSTACLE_WIDTH = 20\nOBSTACLE_HEIGHT = 40\nOBSTACLE_COLOR = (255, 0, 0)\nOBSTACLE_SPEED = 5\n```\n\n### File: `dino.py`\n```python\nimport pygame\nfrom settings import DINO_WIDTH, DINO_HEIGHT, DINO_COLOR, DINO_JUMP_HEIGHT\n\nclass Dino:\n    def __init__(self, x, y):\n        self.rect = pygame.Rect(x, y, DINO_WIDTH, DINO_HEIGHT)\n        self.color = DINO_COLOR\n        self.is_jumping = False\n        self.jump_speed = DINO_JUMP_HEIGHT\n        self.gravity = 1\n\n    def update(self):\n        if self.is_jumping:\n            self.rect.y -= self.jump_speed\n            self.jump_speed -= self.gravity\n            if self.jump_speed < -DINO_JUMP_HEIGHT:\n                self.is_jumping = False\n                self.jump_speed = DINO_JUMP_HEIGHT\n\n    def draw(self, screen):\n        pygame.draw.rect(screen, self.color, self.rect)\n\n    def jump(self):\n        if not self.is_jumping:\n            self.is_jumping = True\n```\n\n### File: `obstacle.py`\n```python\nimport pygame\nfrom settings import OBSTACLE_WIDTH, OBSTACLE_HEIGHT, OBSTACLE_COLOR, OBSTACLE_SPEED\n\nclass Obstacle:\n    def __init__(self, x, y):\n        self.rect = pygame.Rect(x, y, OBSTACLE_WIDTH, OBSTACLE_HEIGHT)\n        self.color = OBSTACLE_COLOR\n        self.speed = OBSTACLE_SPEED\n\n    def update(self):\n        self.rect.x -= self.speed\n\n    def draw(self, screen):\n        pygame.draw.rect(screen, self.color, self.rect)\n```\n\n### File: `main.py`\n```python\nimport pygame\nimport random\nfrom settings import SCREEN_WIDTH, SCREEN_HEIGHT, FPS, WHITE\nfrom dino.py import Dino\nfrom obstacle.py import Obstacle\n\ndef main():\n    pygame.init()\n    screen = pygame.display.set_mode((SCREEN_WIDTH, SCREEN_HEIGHT))\n    pygame.display.set_caption(\"Dino Game\")\n    clock = pygame.time.Clock()\n\n    dino = Dino(50, SCREEN_HEIGHT - 60)\n    obstacles = []\n\n    running = True\n    while running:\n        for event in pygame.event.get():\n            if event.type == pygame.QUIT:\n                running = False\n            if event.type == pygame.KEYDOWN:\n                if event.key == pygame.K_SPACE:\n                    dino.jump()\n\n        screen.fill(WHITE)\n\n        dino.update()\n        dino.draw(screen)\n\n        if random.randint(1, 100) < 2:\n            obstacles.append(Obstacle(SCREEN_WIDTH, SCREEN_HEIGHT - 40))\n\n        for obstacle in obstacles[:]:\n            obstacle.update()\n            obstacle.draw(screen)\n            if obstacle.rect.x < 0:\n                obstacles.remove(obstacle)\n            if dino.rect.colliderect(obstacle.rect):\n                running = False\n\n        pygame.display.flip()\n        clock.tick(FPS)\n\n    pygame.quit()\n\nif __name__ == \"__main__\":\n    main()\n```\n\nThis code sets up a basic Dino game where the dinosaur can jump over obstacles. The game will end if the dinosaur collides with an obstacle. You can expand and improve this game by adding more features, such as scoring, different types of obstacles, and animations."

	codeChanges := analyzer.ExtractCodeChanges(text)

	assert.Len(t, codeChanges, 4)
}

// / Test for ExtractCodeChanges with standard input
func TestExtractCodeChanges(t *testing.T) {
	setup(t)
	text := "File: test.go\n```go\npackage main\n```\nFile: test2.go\n```go\npackage main\n```"

	codeChanges := analyzer.ExtractCodeChanges(text)

	assert.Len(t, codeChanges, 2)
	assert.Equal(t, "test.go", codeChanges[0].RelativePath)
	assert.Equal(t, "package main", codeChanges[0].Code)
	assert.Equal(t, "test2.go", codeChanges[1].RelativePath)
	assert.Equal(t, "package main", codeChanges[1].Code)
}

// Test for ExtractCodeChanges with standard input (file path start with File: )
func TestExtractCodeChangesWithStartPathWithFileColon(t *testing.T) {
	setup(t)
	text := "File: tests/fakes/Foo1.cs\n```go\npackage main\n```"

	codeChanges := analyzer.ExtractCodeChanges(text)

	// Expect 1 change since there's only one code block and file path
	assert.Len(t, codeChanges, 1)

	// Check the file path and code
	assert.Equal(t, "tests/fakes/Foo1.cs", codeChanges[0].RelativePath)
	assert.Equal(t, "package main", codeChanges[0].Code)
}

// Test for ExtractCodeChanges with standard input (file path start with 1. )
func TestExtractCodeChangesWithStartPathWithNumberAndDot(t *testing.T) {
	setup(t)
	text := "1. tests/fakes/Foo1.cs\n```go\npackage main\n```"

	codeChanges := analyzer.ExtractCodeChanges(text)

	// Expect 1 change since there's only one code block and file path
	assert.Len(t, codeChanges, 1)

	// Check the file path and code
	assert.Equal(t, "tests/fakes/Foo1.cs", codeChanges[0].RelativePath)
	assert.Equal(t, "package main", codeChanges[0].Code)
}

// TestApplyChanges_AddLines tests if ApplyChanges correctly adds new lines prefixed with "+".
func TestApplyChanges_AddLines(t *testing.T) {
	setup(t)

	// Define the relative path and initial content for the file
	filePath := filepath.Join(relativePathTestDir, "addlines.go")
	initialContent := "package main\nfunc main() {}"
	addedLinesDiff := "+func newFunc() {}\nfunc main() {}"

	// Create the file with initial content
	err := os.WriteFile(filePath, []byte(initialContent), 0644)
	assert.NoError(t, err)

	// Use ApplyChanges to add new lines
	err = analyzer.ApplyChanges(filePath, addedLinesDiff)
	assert.NoError(t, err)

	// Verify the new lines were added correctly
	expectedContent := " func newFunc() {}\nfunc main() {}"
	savedContent, err := os.ReadFile(filePath)
	assert.NoError(t, err)
	assert.Equal(t, expectedContent, string(savedContent))
}

// TestApplyChanges_RemoveLines tests if ApplyChanges correctly removes lines prefixed with "-".
func TestApplyChanges_RemoveLines(t *testing.T) {
	setup(t)

	// Define the relative path and initial content for the file
	filePath := filepath.Join(relativePathTestDir, "removelines.go")
	initialContent := "package main\nfunc toBeRemoved() {}\nfunc main() {}"
	removedLinesDiff := "-func toBeRemoved() {}\nfunc main() {}"

	// Create the file with initial content
	err := os.WriteFile(filePath, []byte(initialContent), 0644)
	assert.NoError(t, err)

	// Use ApplyChanges to remove specific lines
	err = analyzer.ApplyChanges(filePath, removedLinesDiff)
	assert.NoError(t, err)

	// Verify the specified lines were removed
	expectedContent := "func main() {}"
	savedContent, err := os.ReadFile(filePath)
	assert.NoError(t, err)
	assert.Equal(t, expectedContent, string(savedContent))
}

// TestApplyChanges_AddAndRemoveLines tests if ApplyChanges handles both "+" and "-" prefixed lines.
func TestApplyChanges_AddAndRemoveLines(t *testing.T) {
	setup(t)

	// Define the relative path and initial content for the file
	filePath := filepath.Join(relativePathTestDir, "addremovelines.go")
	initialContent := "package main\nfunc oldFunc() {}\nfunc main() {}"
	addRemoveLinesDiff := "-func oldFunc() {}\n+func newFunc() {}\nfunc main() {}"

	// Create the file with initial content
	err := os.WriteFile(filePath, []byte(initialContent), 0644)
	assert.NoError(t, err)

	// Use ApplyChanges to add and remove specific lines
	err = analyzer.ApplyChanges(filePath, addRemoveLinesDiff)
	assert.NoError(t, err)

	// Verify the lines were added and removed correctly
	expectedContent := " func newFunc() {}\nfunc main() {}"
	savedContent, err := os.ReadFile(filePath)
	assert.NoError(t, err)
	assert.Equal(t, expectedContent, string(savedContent))
}

// Test for ExtractCodeChanges with additional characters around the file path
func TestExtractCodeChangesWithAdditionalCharacters(t *testing.T) {
	setup(t)
	text := "\n\n#####File: test.go#####\n```go\npackage main\n```\nFile: test2.go\n```go\npackage main\n```"

	codeChanges := analyzer.ExtractCodeChanges(text)

	assert.Len(t, codeChanges, 2)
	assert.Equal(t, "test.go", codeChanges[0].RelativePath)
	assert.Equal(t, "package main", codeChanges[0].Code)
	assert.Equal(t, "test2.go", codeChanges[1].RelativePath)
	assert.Equal(t, "package main", codeChanges[1].Code)
}

// Test for ExtractCodeChanges with missing whitespace around "file:"
func TestExtractCodeChangesWithDifferentFileLabelFormat(t *testing.T) {
	setup(t)
	text := "file:test.go\n```go\npackage main\n```\nFile: test2.go\n```go\npackage main\n```"

	codeChanges := analyzer.ExtractCodeChanges(text)

	assert.Len(t, codeChanges, 2)
	assert.Equal(t, "test.go", codeChanges[0].RelativePath)
	assert.Equal(t, "package main", codeChanges[0].Code)
	assert.Equal(t, "test2.go", codeChanges[1].RelativePath)
	assert.Equal(t, "package main", codeChanges[1].Code)
}

// Test for ExtractCodeChanges with prefixes like "### 5."
func TestExtractCodeChangesWithSpecialFilePathFormat(t *testing.T) {
	setup(t)
	text := "### 5. Direction.cs/nssss\n```csharp\npublic class Direction {}\n```"

	codeChanges := analyzer.ExtractCodeChanges(text)

	assert.Len(t, codeChanges, 1)
	assert.Equal(t, "Direction.cs", codeChanges[0].RelativePath)
	assert.Equal(t, "public class Direction {}", codeChanges[0].Code)
}

// TestExtractCodeChangesWithUnsupportedColonPrefixExpectNil checks that an unsupported colon prefix returns nil
func TestExtractCodeChangesWithUnsupportedColonPrefixExpectNil(t *testing.T) {
	setup(t)
	text := "## test: Direction.cs/nssss\n```csharp\npublic class Direction {}\n```"

	// Run ExtractCodeChanges with the unsupported prefix in the file path
	codeChanges := analyzer.ExtractCodeChanges(text)

	// Expect an empty result because the prefix "## test:" is not supported
	assert.Len(t, codeChanges, 0) // Expect no code changes
	assert.Nil(t, codeChanges)    // Expect nil for codeChanges since no valid paths were found
}

// TestExtractCodeChangesWithUnsupportedDotPrefixExpectNil checks that an unsupported dot prefix returns nil
func TestExtractCodeChangesWithUnsupportedDotPrefixExpectNil(t *testing.T) {
	setup(t)
	text := "someCharacter. Direction.cs/nssss\n```csharp\npublic class Direction {}\n```"

	// Run ExtractCodeChanges with the unsupported prefix in the file path
	codeChanges := analyzer.ExtractCodeChanges(text)

	// Expect an empty result because the prefix "someCharacter." is not supported
	assert.Len(t, codeChanges, 0) // Expect no code changes
	assert.Nil(t, codeChanges)    // Expect nil for codeChanges since no valid paths were found
}

// Test for ExtractCodeChanges with "## File:" prefix before file path
func TestExtractCodeChangesWithFilePrefixAndSlashInFilePath(t *testing.T) {
	setup(t)
	text := "## File: Direction.cs/nssss\n```csharp\npublic class Direction {}\n```"

	codeChanges := analyzer.ExtractCodeChanges(text)

	assert.Len(t, codeChanges, 1)
	assert.Equal(t, "Direction.cs", codeChanges[0].RelativePath)
	assert.Equal(t, "public class Direction {}", codeChanges[0].Code)
}

// Test for ExtractCodeChanges with no code blocks
func TestExtractCodeChangesWithNoCodeBlocks(t *testing.T) {
	setup(t)
	text := "File: test.go\nFile: test2.go\n"

	codeChanges := analyzer.ExtractCodeChanges(text)

	assert.Len(t, codeChanges, 0)
}

// Test for ExtractCodeChanges with empty text input
func TestExtractCodeChangesWithEmptyText(t *testing.T) {
	setup(t)
	text := ""

	codeChanges := analyzer.ExtractCodeChanges(text)

	assert.Len(t, codeChanges, 0)
}

// Test for ExtractCodeChanges with non-matching patterns
func TestExtractCodeChangesWithNonMatchingPatterns(t *testing.T) {
	setup(t)
	text := "Random text without any file paths or code blocks."

	codeChanges := analyzer.ExtractCodeChanges(text)

	assert.Len(t, codeChanges, 0)
}

// Test for ExtractCodeChanges with multiple code blocks for the same file
func TestExtractCodeChangesWithMultipleCodeBlocksSameFile(t *testing.T) {
	setup(t)
	text := "File: test.go\n```go\npackage main\n```\n```go\nfunc main() {}\n```"

	codeChanges := analyzer.ExtractCodeChanges(text)

	assert.Len(t, codeChanges, 1) // Only the first code block associated with each file path
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
