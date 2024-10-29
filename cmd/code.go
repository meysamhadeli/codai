package cmd

import (
	"bufio"
	"context"
	"fmt"
	"github.com/meysamhadeli/codai/code_analyzer/models"
	"github.com/meysamhadeli/codai/constants/lipgloss_color"
	"github.com/meysamhadeli/codai/embed_data"
	"github.com/meysamhadeli/codai/utils"
	"github.com/pterm/pterm"
	"github.com/spf13/cobra"
	"os"
	"strings"
	"sync"
)

// CodeCmd: codai code
var codeCmd = &cobra.Command{
	Use:   "code",
	Short: "Run the AI-powered code assistant for various coding tasks within a session.",
	Long: `The 'code' subcommand allows users to leverage a session-based AI assistant for a range of coding tasks. 
This assistant can suggest new code, refactor existing code, review code for improvements, and even propose new features 
based on the current project context. Each interaction is part of a session, allowing for continuous context and 
improved responses throughout the user experience.`,
	Run: func(cmd *cobra.Command, args []string) {
		rootDependencies := handleRootCommand(cmd)
		handleCodeCommand(rootDependencies) // Pass standard input by default
	},
}

func handleCodeCommand(rootDependencies *RootDependencies) {

	// Create a context with cancel function
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	// Channel to signal when the application should shut down
	done := make(chan bool)

	go utils.GracefulShutdown(done, func() {
		err := utils.CleanupTempFiles(rootDependencies.Cwd)
		if err != nil {
			fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Failed to cleanup temp files: %v", err)))
		}
	}, func() {
		rootDependencies.ChatHistory.ClearHistory()
	})

	// Launch the user input handler in a goroutine
	go func() {
		reader := bufio.NewReader(os.Stdin)
		for {

			err := utils.CleanupTempFiles(rootDependencies.Cwd)
			if err != nil {
				fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Failed to cleanup temp files: %v", err)))
			}

			// Create a new spinner with custom color and custom shape
			//spinner := pterm.DefaultSpinner.WithStyle(pterm.NewStyle(pterm.FgGray)).WithRemoveWhenDone(true)

			// Get user input
			userInput, err := utils.InputPrompt(reader)
			if err != nil {
				fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
				continue
			}

			spinner, _ := pterm.DefaultSpinner.WithStyle(pterm.NewStyle(pterm.FgGray)).Start("Loading context...")

			// Get all data files from the root directory
			allDataFiles, err := rootDependencies.Analyzer.GetProjectFiles(rootDependencies.Cwd)
			if err != nil {
				spinner.Stop()
				fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
				continue
			}

			var wg sync.WaitGroup
			errorChan := make(chan error, len(allDataFiles))
			var relevantCode []string

			for _, dataFile := range allDataFiles {
				wg.Add(1) // Increment the WaitGroup counter
				go func(dataFile models.FileData) {
					defer wg.Done() // Decrement the counter when the Goroutine completes
					filesEmbeddingOperation := func() error {
						fileEmbeddingResponse, err := rootDependencies.CurrentProvider.EmbeddingRequest(ctx, dataFile.TreeSitterCode)
						if err != nil {
							return err
						}

						fileEmbedding := fileEmbeddingResponse.Data[0].Embedding

						// Save embeddings to the embedding store
						rootDependencies.Store.Save(dataFile.RelativePath, dataFile.Code, fileEmbedding)
						return nil
					}

					// Call the retryWithBackoff function with the operation and a 3-time retry
					if err := utils.RetryWithBackoff(filesEmbeddingOperation, 3); err != nil {
						errorChan <- err // Send the error to the channel
					}
				}(dataFile) // Pass the current dataFile to the Goroutine
			}

			wg.Wait()        // Wait for all Goroutines to finish
			close(errorChan) // Close the error channel
			// Handle any errors that occurred during processing
			for err = range errorChan {
				spinner.Stop()
				fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
				continue
			}

			queryEmbeddingOperation := func() error {
				// Step 5: Generate embedding for the user query
				queryEmbeddingResponse, err := rootDependencies.CurrentProvider.EmbeddingRequest(ctx, userInput)
				if err != nil {
					return err
				}

				queryEmbedding := queryEmbeddingResponse.Data[0].Embedding

				// Ensure there's an embedding for the user query
				if len(queryEmbedding) == 0 {
					return fmt.Errorf(lipgloss_color.Red.Render("no embeddings returned for user query"))
				}

				// Find relevant chunks with a similarity threshold of 0.3, no topN limit (-1 means all results and positive number only return this relevant results number)
				topN := -1

				// Step 6: Find relevant code chunks based on the user query embedding
				relevantCode = rootDependencies.Store.FindRelevantChunks(queryEmbedding, topN, rootDependencies.Config.AIProviderConfig.EmbeddingModel, rootDependencies.Config.AIProviderConfig.Threshold)
				return nil
			}

			// Call the retryWithBackoff function with the operation and a 3 time retry
			err = utils.RetryWithBackoff(queryEmbeddingOperation, 3)

			if err != nil {
				spinner.Stop()
				fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
				continue
			}

			spinner.Stop()

			// Combine the relevant code into a single string
			code := strings.Join(relevantCode, "\n---------\n\n")

			// Create the final prompt for the AI
			prompt := fmt.Sprintf("%s\n\n%s\n\n", fmt.Sprintf("Here is the context: \n\n%s", code), string(embed_data.CodeBlockTemplate))
			userInputPrompt := fmt.Sprintf("Here is my request:\n%s", userInput)
			history := strings.Join(rootDependencies.ChatHistory.GetHistory(), "\n\n")
			finalPrompt := fmt.Sprintf("%s\n\n%s\n\n", history, prompt)

			var aiResponse string

			chatRequestOperation := func() error {

				// Step 7: Send the relevant code and user input to the AI API
				aiResponse, err = rootDependencies.CurrentProvider.ChatCompletionRequest(ctx, userInputPrompt, finalPrompt)
				if err != nil {
					return fmt.Errorf("failed to get response from AI: %v", err)
				}

				return nil
			}

			// Call the retryWithBackoff function with the operation and a 3 time retry
			err = utils.RetryWithBackoff(chatRequestOperation, 3)

			if err != nil {
				fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
				continue
			}

			rootDependencies.ChatHistory.AddToHistory(fmt.Sprintf("%s\n\n%s\n\n%s\n\n", prompt, userInputPrompt, aiResponse))

			changes, err := rootDependencies.Analyzer.ExtractCodeChanges(aiResponse)

			if err != nil || changes == nil {
				fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Problem during fetching data from LLM model `%s`. Please try again with more context and keywords in your request.", rootDependencies.Config.AIProviderConfig.ChatCompletionModel)))
				continue
			}

			var tempFiles []string

			// Prepare temp files for using comparing in diff
			for _, change := range changes {
				tempPath, err := utils.WriteToTempFile(change.RelativePath, change.Code)
				if err != nil {
					fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Failed to write temp file: %v", err)))
					continue
				}
				tempFiles = append(tempFiles, tempPath)
			}

			// Run diff after applying changes to temp files
			for _, change := range changes {

				// Prompt the user to accept or reject the changes
				err := utils.ConfirmPrompt(change.RelativePath)
				if err != nil {
					fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Error getting user prompt: %v", err)))
					continue
				}
			}

			// Display token usage details in a boxed format after each AI request
			rootDependencies.TokenManagement.DisplayTokens(rootDependencies.Config.AIProviderConfig.ChatCompletionModel, rootDependencies.Config.AIProviderConfig.EmbeddingModel)
		}
	}()

	// Wait for the shutdown signal
	select {
	case <-done:
		fmt.Println(lipgloss_color.Red.Render("Application shutting down"))
		return
	}
}
