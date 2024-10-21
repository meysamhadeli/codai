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
	"io"
	"os"
	"strings"
	"sync"
)

// CodeCmd: codai code
var codeCmd = &cobra.Command{
	Use:   "code",
	Short: "Run the code subcommand",
	Long:  `The 'code' subcommand executes coding-related operations using Codai.`,
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
	})

	// Launch the user input handler in a goroutine
	go func() {
		reader := bufio.NewReader(os.Stdin)
		for {
			select {
			case <-ctx.Done(): // Stop input loop when the context is canceled
				return
			default:
				// Get user input
				fmt.Println(lipgloss_color.BlueSky.Render("Please enter your request for code assis with ai:"))
				userInput, err := reader.ReadString('\n')

				if err != nil {
					if err == io.EOF {
						return // Exit gracefully on EOF
					}
					fmt.Println("Error reading input:", err)
					return
				}

				spinner, _ := pterm.DefaultSpinner.Start("Loading context...")

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
							rootDependencies.Store.Save(dataFile.Path, dataFile.Code, fileEmbedding)
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
					relevantCode = rootDependencies.Store.FindRelevantChunks(queryEmbedding, topN, rootDependencies.Config.AIProviderConfig.EmbeddingModel)
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
				prompt := fmt.Sprintf("%s\n\n%s\n\n", fmt.Sprintf("Here is the context: \n%s", code), string(embed_data.CodeResultTemplate))
				userInputPrompt := fmt.Sprintf("Here is my request:\n%s", userInput)

				var aiResponse string

				chatRequestOperation := func() error {

					// Step 7: Send the relevant code and user input to the AI API
					aiResponse, err = rootDependencies.CurrentProvider.ChatCompletionRequest(ctx, userInputPrompt, prompt)
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

				changes, err := rootDependencies.Markdown.ExtractCodeChanges(aiResponse)
				if err != nil {
					fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
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
					err = rootDependencies.Markdown.GenerateDiff(change)
					if err != nil {
						fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Error running file diff: %v", err)))
						continue
					}

					// Prompt the user to accept or reject the changes
					promptAccepted, err := utils.PromptUser(change.RelativePath)
					if err != nil {
						fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Error getting user prompt: %v", err)))
						continue
					}

					if promptAccepted {

						err := rootDependencies.Analyzer.ApplyChanges(change.RelativePath)
						if err != nil {
							fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Error applying changes: %v", err)))
							continue
						}

						fmt.Println(lipgloss_color.Green.Render(fmt.Sprintf("Changes applied and saved for `%s`.\n", change.RelativePath)))

					} else {
						fmt.Println(lipgloss_color.Orange.Render(fmt.Sprintf("Changes for `%s` is discarded. No files were modified.\n", change.RelativePath)))
					}
				}
			}
		}
	}()

	// Wait for the shutdown signal
	select {
	case <-done:
		fmt.Println(lipgloss_color.Red.Render("Application shutting down"))
		return
	}
}
