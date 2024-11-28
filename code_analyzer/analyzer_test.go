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
	t.Run("TestProcessFileWithSupportedLanguageReturnTreeSitterResult", TestProcessFileWithSupportedLanguageReturnTreeSitterResult)
	t.Run("TestApplyChanges_NewFile", TestApplyChanges_NewFile)
	t.Run("TestApplyChanges_ModifyFile", TestApplyChanges_ModifyFile)
	t.Run("TestApplyChanges_DeletedFile", TestApplyChanges_DeletedFile)
	t.Run("TestExtractCodeChanges", TestExtractCodeChanges)
	t.Run("TestExtractCodeChangesForMDFile", TestExtractCodeChangesForMDFile)
	t.Run("TestExtractCodeChangesForAnotherMDFile", TestExtractCodeChangesForAnotherMDFile)
	t.Run("TestExtractCodeChangesComplexText", TestExtractCodeChangesComplexText)
	t.Run("TestExtractCodeChangesWithStartPathWithFileColon", TestExtractCodeChangesWithStartPathWithFileColon)
	t.Run("TestExtractCodeChangesWithStartPathWithNumberAndDot", TestExtractCodeChangesWithStartPathWithNumberAndDot)
	t.Run("TestApplyChanges_AddLines", TestApplyChanges_AddLines)
	t.Run("TestApplyChanges_RemoveLines", TestApplyChanges_RemoveLines)
	t.Run("TestApplyChanges_AddAndRemoveLines", TestApplyChanges_AddAndRemoveLines)
	t.Run("TestExtractCodeChangesWithAdditionalCharacters", TestExtractCodeChangesWithAdditionalCharacters)
	t.Run("TestExtractCodeChangesWithDifferentFileLabelFormat", TestExtractCodeChangesWithDifferentFileLabelFormat)
	t.Run("TestExtractCodeChangesWithSpecialFilePathFormat", TestExtractCodeChangesWithSpecialFilePathFormat)
	t.Run("TestExtractCodeChangesWithUnsupportedColonPrefixExpectNil", TestExtractCodeChangesWithUnsupportedColonPrefixExpectNil)
	t.Run("TestExtractCodeChangesWithUnsupportedDotPrefixExpectNil", TestExtractCodeChangesWithUnsupportedDotPrefixExpectNil)
	t.Run("TestExtractCodeChangesWithFilePrefixAndSlashInFilePath", TestExtractCodeChangesWithFilePrefixAndSlashInFilePath)
	t.Run("TestExtractCodeChangesWithNoCodeBlocks", TestExtractCodeChangesWithNoCodeBlocks)
	t.Run("TestExtractCodeChangesWithEmptyText", TestExtractCodeChangesWithEmptyText)
	t.Run("TestExtractCodeChangesWithNonMatchingPatterns", TestExtractCodeChangesWithNonMatchingPatterns)
	t.Run("TestExtractCodeChangesWithMultipleCodeBlocksSameFile", TestExtractCodeChangesWithMultipleCodeBlocksSameFile)
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
func TestProcessFileWithSupportedLanguageReturnTreeSitterResult(t *testing.T) {
	setup(t)
	content := []byte("class Test {}")

	result := analyzer.ProcessFile("test.cs", content)

	assert.Contains(t, result, "class: Test")
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

func TestExtractCodeChangesForMDFile(t *testing.T) {
	setup(t)
	text := "File: readme.md\n```markdown\n# Pacman Game Documentation\n\n## Overview\n\nThis project is a simple implementation of the classic Pacman game using Python and the Pygame library. The player controls Pacman, who must eat food while avoiding ghosts. The game keeps track of the player's score, which increases each time Pacman eats food.\n\n## Requirements\n\n- Python 3.x\n- Pygame library\n\n## Installation\n\n1. **Install Python**: Ensure you have Python installed on your system. You can download it from [python.org](https://www.python.org/).\n\n2. **Install Pygame**: Install the Pygame library using pip. Open your terminal or command prompt and run:\n   ```sh\n   pip install pygame\n   ```\n\n## Running the Game\n\n1. **Save the Code**: Save the provided code into a Python file, for example, `pacman_game.py`.\n\n2. **Run the Game**: Navigate to the directory where you saved the `pacman_game.py` file using your terminal or command prompt. Then, run the game with the following command:\n   ```sh\n   python pacman_game.py\n   ```\n\n## Game Controls\n\n- **Arrow Keys**: Use the arrow keys on your keyboard to move Pacman.\n  - **Left Arrow**: Move Pacman left.\n  - **Right Arrow**: Move Pacman right.\n  - **Up Arrow**: Move Pacman up.\n  - **Down Arrow**: Move Pacman down.\n\n## Code Explanation\n\n### Imports and Initialization\n\n```python\nimport pygame\nimport random\n\n# Initialize Pygame\npygame.init()\n```\n\n- `pygame` and `random` libraries are imported.\n- Pygame is initialized.\n\n### Screen Setup\n\n```python\n# Screen dimensions\nSCREEN_WIDTH = 800\nSCREEN_HEIGHT = 600\n\n# Colors\nBLACK = (0, 0, 0)\nWHITE = (255, 255, 255)\nYELLOW = (255, 255, 0)\nBLUE = (0, 0, 255)\nRED = (255, 0, 0)\n\n# Set up the display\nscreen = pygame.display.set_mode((SCREEN_WIDTH, SCREEN_HEIGHT))\npygame.display.set_caption(\"Pacman Game\")\n```\n\n- Screen dimensions and colors are defined.\n- The display is set up with the specified dimensions and title.\n\n### Game Entities\n\n#### Pacman\n\n```python\n# Pacman settings\npacman_size = 20\npacman_x = SCREEN_WIDTH // 2\npacman_y = SCREEN_HEIGHT // 2\npacman_speed = 5\n```\n\n- Pacman's size, initial position, and speed are defined.\n\n#### Ghosts\n\n```python\n# Ghost settings\nghost_size = 20\nghost_speed = 3\nghosts = []\n\nfor _ in range(4):\n    ghost_x = random.randint(0, SCREEN_WIDTH - ghost_size)\n    ghost_y = random.randint(0, SCREEN_HEIGHT - ghost_size)\n    ghosts.append([ghost_x, ghost_y])\n```\n\n- Ghosts' size and speed are defined.\n- Four ghosts are created at random positions.\n\n#### Food\n\n```python\n# Food settings\nfood_size = 10\nfood_x = random.randint(0, SCREEN_WIDTH - food_size)\nfood_y = random.randint(0, SCREEN_HEIGHT - food_size)\n```\n\n- Food's size and initial position are defined.\n\n### Game Loop\n\n```python\n# Score\nscore = 0\n\n# Main game loop\nrunning = True\nwhile running:\n    for event in pygame.event.get():\n        if event.type == pygame.QUIT:\n            running = False\n\n    # Get the keys pressed\n    keys = pygame.key.get_pressed()\n\n    # Move Pacman\n    if keys[pygame.K_LEFT]:\n        pacman_x -= pacman_speed\n    if keys[pygame.K_RIGHT]:\n        pacman_x += pacman_speed\n    if keys[pygame.K_UP]:\n        pacman_y -= pacman_speed\n    if keys[pygame.K_DOWN]:\n        pacman_y += pacman_speed\n\n    # Keep Pacman within screen bounds\n    pacman_x = max(0, min(SCREEN_WIDTH - pacman_size, pacman_x))\n    pacman_y = max(0, min(SCREEN_HEIGHT - pacman_size, pacman_y))\n\n    # Check for collision with food\n    if abs(pacman_x - food_x) < pacman_size and abs(pacman_y - food_y) < pacman_size:\n        score += 1\n        food_x = random.randint(0, SCREEN_WIDTH - food_size)\n        food_y = random.randint(0, SCREEN_HEIGHT - food_size)\n\n    # Move ghosts\n    for ghost in ghosts:\n        if ghost[0] < pacman_x:\n            ghost[0] += ghost_speed\n        elif ghost[0] > pacman_x:\n            ghost[0] -= ghost_speed\n\n        if ghost[1] < pacman_y:\n            ghost[1] += ghost_speed\n        elif ghost[1] > pacman_y:\n            ghost[1] -= ghost_speed\n\n        # Check for collision with Pacman\n        if abs(ghost[0] - pacman_x) < pacman_size and abs(ghost[1] - pacman_y) < pacman_size:\n            running = False\n\n    # Clear the screen\n    screen.fill(BLACK)\n\n    # Draw Pacman\n    pygame.draw.circle(screen, YELLOW, (pacman_x, pacman_y), pacman_size // 2)\n\n    # Draw ghosts\n    for ghost in ghosts:\n        pygame.draw.rect(screen, RED, (ghost[0], ghost[1], ghost_size, ghost_size))\n\n    # Draw food\n    pygame.draw.circle(screen, BLUE, (food_x, food_y), food_size // 2)\n\n    # Draw score\n    font = pygame.font.Font(None, 36)\n    text = font.render(f\"Score: {score}\", True, WHITE)\n    screen.blit(text, (10, 10))\n\n    # Update the display\n    pygame.display.flip()\n\n    # Cap the frame rate\n    clock.tick(30)\n\n# Quit Pygame\npygame.quit()\n```\n\n- The main game loop handles events, updates game state, and renders the game.\n- Pacman is moved based on key presses.\n- Pacman's position is kept within screen bounds.\n- Collision detection is implemented for food and ghosts.\n- The screen is cleared and game entities are drawn each frame.\n- The display is updated and the frame rate is capped.\n\n## Conclusion\n\nThis documentation provides an overview of the Pacman game project, including installation instructions, game controls, and a detailed explanation of the code. The game is a simple yet fun implementation of the classic Pacman game using Python and Pygame.\n```"

	codeChanges := analyzer.ExtractCodeChanges(text)

	assert.Len(t, codeChanges, 1)
	assert.Equal(t, "readme.md", codeChanges[0].RelativePath)
	assert.Equal(t, "# Pacman Game Documentation\n\n## Overview\n\nThis project is a simple implementation of the classic Pacman game using Python and the Pygame library. The player controls Pacman, who must eat food while avoiding ghosts. The game keeps track of the player's score, which increases each time Pacman eats food.\n\n## Requirements\n\n- Python 3.x\n- Pygame library\n\n## Installation\n\n1. **Install Python**: Ensure you have Python installed on your system. You can download it from [python.org](https://www.python.org/).\n\n2. **Install Pygame**: Install the Pygame library using pip. Open your terminal or command prompt and run:\n   ```sh\n   pip install pygame\n   ```\n\n## Running the Game\n\n1. **Save the Code**: Save the provided code into a Python file, for example, `pacman_game.py`.\n\n2. **Run the Game**: Navigate to the directory where you saved the `pacman_game.py` file using your terminal or command prompt. Then, run the game with the following command:\n   ```sh\n   python pacman_game.py\n   ```\n\n## Game Controls\n\n- **Arrow Keys**: Use the arrow keys on your keyboard to move Pacman.\n  - **Left Arrow**: Move Pacman left.\n  - **Right Arrow**: Move Pacman right.\n  - **Up Arrow**: Move Pacman up.\n  - **Down Arrow**: Move Pacman down.\n\n## Code Explanation\n\n### Imports and Initialization\n\n```python\nimport pygame\nimport random\n\n# Initialize Pygame\npygame.init()\n```\n\n- `pygame` and `random` libraries are imported.\n- Pygame is initialized.\n\n### Screen Setup\n\n```python\n# Screen dimensions\nSCREEN_WIDTH = 800\nSCREEN_HEIGHT = 600\n\n# Colors\nBLACK = (0, 0, 0)\nWHITE = (255, 255, 255)\nYELLOW = (255, 255, 0)\nBLUE = (0, 0, 255)\nRED = (255, 0, 0)\n\n# Set up the display\nscreen = pygame.display.set_mode((SCREEN_WIDTH, SCREEN_HEIGHT))\npygame.display.set_caption(\"Pacman Game\")\n```\n\n- Screen dimensions and colors are defined.\n- The display is set up with the specified dimensions and title.\n\n### Game Entities\n\n#### Pacman\n\n```python\n# Pacman settings\npacman_size = 20\npacman_x = SCREEN_WIDTH // 2\npacman_y = SCREEN_HEIGHT // 2\npacman_speed = 5\n```\n\n- Pacman's size, initial position, and speed are defined.\n\n#### Ghosts\n\n```python\n# Ghost settings\nghost_size = 20\nghost_speed = 3\nghosts = []\n\nfor _ in range(4):\n    ghost_x = random.randint(0, SCREEN_WIDTH - ghost_size)\n    ghost_y = random.randint(0, SCREEN_HEIGHT - ghost_size)\n    ghosts.append([ghost_x, ghost_y])\n```\n\n- Ghosts' size and speed are defined.\n- Four ghosts are created at random positions.\n\n#### Food\n\n```python\n# Food settings\nfood_size = 10\nfood_x = random.randint(0, SCREEN_WIDTH - food_size)\nfood_y = random.randint(0, SCREEN_HEIGHT - food_size)\n```\n\n- Food's size and initial position are defined.\n\n### Game Loop\n\n```python\n# Score\nscore = 0\n\n# Main game loop\nrunning = True\nwhile running:\n    for event in pygame.event.get():\n        if event.type == pygame.QUIT:\n            running = False\n\n    # Get the keys pressed\n    keys = pygame.key.get_pressed()\n\n    # Move Pacman\n    if keys[pygame.K_LEFT]:\n        pacman_x -= pacman_speed\n    if keys[pygame.K_RIGHT]:\n        pacman_x += pacman_speed\n    if keys[pygame.K_UP]:\n        pacman_y -= pacman_speed\n    if keys[pygame.K_DOWN]:\n        pacman_y += pacman_speed\n\n    # Keep Pacman within screen bounds\n    pacman_x = max(0, min(SCREEN_WIDTH - pacman_size, pacman_x))\n    pacman_y = max(0, min(SCREEN_HEIGHT - pacman_size, pacman_y))\n\n    # Check for collision with food\n    if abs(pacman_x - food_x) < pacman_size and abs(pacman_y - food_y) < pacman_size:\n        score += 1\n        food_x = random.randint(0, SCREEN_WIDTH - food_size)\n        food_y = random.randint(0, SCREEN_HEIGHT - food_size)\n\n    # Move ghosts\n    for ghost in ghosts:\n        if ghost[0] < pacman_x:\n            ghost[0] += ghost_speed\n        elif ghost[0] > pacman_x:\n            ghost[0] -= ghost_speed\n\n        if ghost[1] < pacman_y:\n            ghost[1] += ghost_speed\n        elif ghost[1] > pacman_y:\n            ghost[1] -= ghost_speed\n\n        # Check for collision with Pacman\n        if abs(ghost[0] - pacman_x) < pacman_size and abs(ghost[1] - pacman_y) < pacman_size:\n            running = False\n\n    # Clear the screen\n    screen.fill(BLACK)\n\n    # Draw Pacman\n    pygame.draw.circle(screen, YELLOW, (pacman_x, pacman_y), pacman_size // 2)\n\n    # Draw ghosts\n    for ghost in ghosts:\n        pygame.draw.rect(screen, RED, (ghost[0], ghost[1], ghost_size, ghost_size))\n\n    # Draw food\n    pygame.draw.circle(screen, BLUE, (food_x, food_y), food_size // 2)\n\n    # Draw score\n    font = pygame.font.Font(None, 36)\n    text = font.render(f\"Score: {score}\", True, WHITE)\n    screen.blit(text, (10, 10))\n\n    # Update the display\n    pygame.display.flip()\n\n    # Cap the frame rate\n    clock.tick(30)\n\n# Quit Pygame\npygame.quit()\n```\n\n- The main game loop handles events, updates game state, and renders the game.\n- Pacman is moved based on key presses.\n- Pacman's position is kept within screen bounds.\n- Collision detection is implemented for food and ghosts.\n- The screen is cleared and game entities are drawn each frame.\n- The display is updated and the frame rate is capped.\n\n## Conclusion\n\nThis documentation provides an overview of the Pacman game project, including installation instructions, game controls, and a detailed explanation of the code. The game is a simple yet fun implementation of the classic Pacman game using Python and Pygame.", codeChanges[0].Code)
}

func TestExtractCodeChangesForAnotherMDFile(t *testing.T) {
	setup(t)
	text := "File: README.md\n```markdown\n# Pacman Game\n\nThis is a simple implementation of the classic Pacman game using Python and the Pygame library. The game features a Pacman character that the player can control using the arrow keys, and multiple ghosts that move randomly on the screen.\n\n## Requirements\n\n- Python 3.x\n- Pygame library\n\n## Installation\n\n1. Make sure you have Python 3.x installed on your system.\n2. Install the Pygame library using pip:\n\n```bash\npip install pygame\n```\n\n## How to Play\n\n1. Clone this repository or download the `pacman_game.py` file.\n2. Run the `pacman_game.py` file using Python:\n\n```bash\npython pacman_game.py\n```\n\n3. Use the arrow keys to control the Pacman character:\n   - Up arrow key: Move up\n   - Down arrow key: Move down\n   - Left arrow key: Move left\n   - Right arrow key: Move right\n\n4. Avoid the ghosts that move randomly on the screen.\n\n## Game Features\n\n- Pacman character that can be controlled using the arrow keys.\n- Four ghosts that move randomly on the screen.\n- Boundary checks to prevent Pacman and ghosts from moving outside the screen.\n\n## Code Structure\n\n- `Pacman` class: Represents the Pacman character. Handles movement and drawing of the Pacman.\n- `Ghost` class: Represents a ghost. Handles random movement and drawing of the ghost.\n- `main` function: The main game loop that initializes the game, handles events, updates the game state, and renders the game.\n\n## Future Improvements\n\n- Add collision detection between Pacman and ghosts.\n- Implement a scoring system.\n- Add more levels and obstacles.\n- Improve the graphics and animations.\n\n## License\n\nThis project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.\n\n## Acknowledgments\n\n- This project was inspired by the classic Pacman game.\n- Thanks to the Pygame community for providing an excellent library for game development in Python.\n```"

	codeChanges := analyzer.ExtractCodeChanges(text)

	assert.Len(t, codeChanges, 1)
	assert.Equal(t, "README.md", codeChanges[0].RelativePath)
	assert.Equal(t, "# Pacman Game\n\nThis is a simple implementation of the classic Pacman game using Python and the Pygame library. The game features a Pacman character that the player can control using the arrow keys, and multiple ghosts that move randomly on the screen.\n\n## Requirements\n\n- Python 3.x\n- Pygame library\n\n## Installation\n\n1. Make sure you have Python 3.x installed on your system.\n2. Install the Pygame library using pip:\n\n```bash\npip install pygame\n```\n\n## How to Play\n\n1. Clone this repository or download the `pacman_game.py` file.\n2. Run the `pacman_game.py` file using Python:\n\n```bash\npython pacman_game.py\n```\n\n3. Use the arrow keys to control the Pacman character:\n   - Up arrow key: Move up\n   - Down arrow key: Move down\n   - Left arrow key: Move left\n   - Right arrow key: Move right\n\n4. Avoid the ghosts that move randomly on the screen.\n\n## Game Features\n\n- Pacman character that can be controlled using the arrow keys.\n- Four ghosts that move randomly on the screen.\n- Boundary checks to prevent Pacman and ghosts from moving outside the screen.\n\n## Code Structure\n\n- `Pacman` class: Represents the Pacman character. Handles movement and drawing of the Pacman.\n- `Ghost` class: Represents a ghost. Handles random movement and drawing of the ghost.\n- `main` function: The main game loop that initializes the game, handles events, updates the game state, and renders the game.\n\n## Future Improvements\n\n- Add collision detection between Pacman and ghosts.\n- Implement a scoring system.\n- Add more levels and obstacles.\n- Improve the graphics and animations.\n\n## License\n\nThis project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.\n\n## Acknowledgments\n\n- This project was inspired by the classic Pacman game.\n- Thanks to the Pygame community for providing an excellent library for game development in Python.", codeChanges[0].Code)
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
