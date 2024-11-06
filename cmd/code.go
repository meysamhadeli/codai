package cmd

import (
	"bufio"
	"context"
	"fmt"
	"github.com/meysamhadeli/codai/code_analyzer/models"
	"github.com/meysamhadeli/codai/constants/lipgloss_color"
	"github.com/meysamhadeli/codai/utils"
	"github.com/pterm/pterm"
	"github.com/spf13/cobra"
	"os"
	"os/signal"
	"strings"
	"sync"
	"syscall"
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
	sigs := make(chan os.Signal, 1)

	signal.Notify(sigs, syscall.SIGINT, syscall.SIGTERM)

	loopNumber := 0

	reader := bufio.NewReader(os.Stdin)

	var requestedContext string

	var fullContextFiles []models.FileData

	var fullContextCodes []string

	spinnerLoadContext, err := pterm.DefaultSpinner.WithStyle(pterm.NewStyle(pterm.FgLightBlue)).WithSequence("⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏").WithDelay(100).Start("Loading Context...")

	// Get all data files from the root directory
	fullContextFiles, fullContextCodes, err = rootDependencies.Analyzer.GetProjectFiles(rootDependencies.Cwd)

	if err != nil {
		spinnerLoadContext.Stop()
		fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
		return
	}

	spinnerLoadContext.Stop()

	// Launch the user input handler in a goroutine
	go func() {
	startLoop: // Label for the start loop
		for {

			if loopNumber > 0 {
				// Display token usage details in a boxed format after each AI request
				rootDependencies.TokenManagement.DisplayTokens(rootDependencies.Config.AIProviderConfig.ProviderName, rootDependencies.Config.AIProviderConfig.ChatCompletionModel, rootDependencies.Config.AIProviderConfig.EmbeddingModel, rootDependencies.Config.RAG)
			}

			loopNumber++

			err := utils.CleanupTempFiles(rootDependencies.Cwd)
			if err != nil {
				fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("failed to cleanup temp files: %v", err)))
			}

			// Get user input
			userInput, err := utils.InputPrompt(reader)
			if err != nil {
				fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
				continue
			}

			// If RAG is enabled, we use RAG system for retrieve most relevant data due user request
			if rootDependencies.Config.RAG {
				var wg sync.WaitGroup
				errorChan := make(chan error, len(fullContextFiles))

				spinnerLoadContextEmbedding, err := pterm.DefaultSpinner.WithStyle(pterm.NewStyle(pterm.FgLightBlue)).WithSequence("⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏").WithDelay(100).Start("Embedding Context...")

				for _, dataFile := range fullContextFiles {
					wg.Add(1) // Increment the WaitGroup counter
					go func(dataFile models.FileData) {
						defer wg.Done() // Decrement the counter when the Goroutine completes
						filesEmbeddingOperation := func() error {
							fileEmbedding, err := rootDependencies.CurrentProvider.EmbeddingRequest(ctx, dataFile.TreeSitterCode)
							if err != nil {
								return err
							}

							// Save embeddings to the embedding store
							rootDependencies.Store.Save(dataFile.RelativePath, dataFile.Code, fileEmbedding[0])
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
					spinnerLoadContextEmbedding.Stop()
					fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
					continue startLoop
				}

				queryEmbeddingOperation := func() error {
					// Step 5: Generate embedding for the user query
					queryEmbedding, err := rootDependencies.CurrentProvider.EmbeddingRequest(ctx, userInput)
					if err != nil {
						return err
					}

					// Ensure there's an embedding for the user query
					if len(queryEmbedding[0]) == 0 {
						return fmt.Errorf(lipgloss_color.Red.Render("no embeddings returned for user query"))
					}

					// Find relevant chunks with a similarity threshold of 0.3, no topN limit (-1 means all results and positive number only return this relevant results number)
					topN := -1

					// Step 6: Find relevant code chunks based on the user query embedding
					fullContextCodes = rootDependencies.Store.FindRelevantChunks(queryEmbedding[0], topN, rootDependencies.Config.AIProviderConfig.EmbeddingModel, rootDependencies.Config.AIProviderConfig.Threshold)
					return nil
				}

				// Call the retryWithBackoff function with the operation and a 3 time retry
				err = utils.RetryWithBackoff(queryEmbeddingOperation, 3)

				if err != nil {
					spinnerLoadContextEmbedding.Stop()
					fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
					continue startLoop
				}

				fmt.Println()
				spinnerLoadContextEmbedding.Stop()
			}

			var aiResponseBuilder strings.Builder

			chatRequestOperation := func() error {
				finalPrompt, userInputPrompt := rootDependencies.Analyzer.GeneratePrompt(fullContextCodes, rootDependencies.ChatHistory.GetHistory(), userInput, requestedContext)

				// Step 7: Send the relevant code and user input to the AI API
				responseChan := rootDependencies.CurrentProvider.ChatCompletionRequest(ctx, userInputPrompt, finalPrompt)

				// Iterate over response channel to handle streamed data or errors.
				for response := range responseChan {
					if response.Err != nil {
						return response.Err
					}

					if response.Done {
						rootDependencies.ChatHistory.AddToHistory(userInput, aiResponseBuilder.String())
						return nil
					}

					aiResponseBuilder.WriteString(response.Content)

					language := utils.DetectLanguageFromCodeBlock(response.Content)
					if err := utils.RenderAndPrintMarkdown(response.Content, language, rootDependencies.Config.Theme); err != nil {
						return fmt.Errorf("error rendering markdown: %v", err)
					}
				}

				return nil
			}

			// Call the retryWithBackoff function with the operation and a 3 time retry
			err = utils.RetryWithBackoff(chatRequestOperation, 3)

			if err != nil {
				fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
				continue startLoop
			}

			if !rootDependencies.Config.RAG {
				// Try to get full block code if block codes is summarized and incomplete
				requestedContext, err = rootDependencies.Analyzer.TryGetInCompletedCodeBlocK(aiResponseBuilder.String())

				if requestedContext != "" && err == nil {
					aiResponseBuilder.Reset()

					fmt.Println(lipgloss_color.BlueSky.Render("Trying to send above context files for getting code suggestion fromm AI...\n"))

					err = chatRequestOperation()

					if err != nil {
						fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
						continue
					}
				}
			}

			// Extract code from AI response and structure this code to apply to git
			changes, err := rootDependencies.Analyzer.ExtractCodeChanges(aiResponseBuilder.String())

			if err != nil || changes == nil {
				fmt.Println(lipgloss_color.BlueSky.Render("\nno code blocks with a valid path detected to apply."))
				continue
			}

			var tempFiles []string

			// Prepare temp files for using comparing in diff
			for _, change := range changes {
				tempPath, err := utils.WriteToTempFile(change.RelativePath, change.Code)
				if err != nil {
					fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("failed to write temp file: %v", err)))
					continue
				}
				tempFiles = append(tempFiles, tempPath)
			}

			var updateContextNeeded = false

			fmt.Print("\n\n")
			// Run diff after applying changes to temp files
			for _, change := range changes {

				// Prompt the user to accept or reject the changes
				promptAccepted, err := utils.ConfirmPrompt(change.RelativePath)
				if err != nil {
					fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("error getting user prompt: %v", err)))
					continue
				}

				if promptAccepted {
					err := rootDependencies.Analyzer.ApplyChanges(change.RelativePath)
					if err != nil {
						fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("error applying changes: %v", err)))
						continue
					}
					fmt.Println(lipgloss_color.Green.Render("✔️ Changes accepted!"))

					updateContextNeeded = true

				} else {
					fmt.Println(lipgloss_color.Red.Render("❌ Changes rejected."))
				}

			}

			// If we need Update the context after apply changes
			if updateContextNeeded {

				spinnerUpdateContext, err := pterm.DefaultSpinner.WithStyle(pterm.NewStyle(pterm.FgLightBlue)).WithSequence("⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏").WithDelay(100).Start("Updating Context...")

				fullContextFiles, fullContextCodes, err = rootDependencies.Analyzer.GetProjectFiles(rootDependencies.Cwd)
				if err != nil {
					spinnerUpdateContext.Stop()
					fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
				}
				spinnerUpdateContext.Stop()
			}
		}
	}()

	go utils.GracefulShutdown(done, sigs, func() {
		err := utils.CleanupTempFiles(rootDependencies.Cwd)
		if err != nil {
			fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("failed to cleanup temp files: %v", err)))
		}
	}, func() {
		rootDependencies.ChatHistory.ClearHistory()
	})

	select {
	case <-done:
		return
	}
}
