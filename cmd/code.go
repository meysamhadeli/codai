package cmd

import (
	"bufio"
	"context"
	"fmt"
	"github.com/meysamhadeli/codai/code_analyzer/models"
	"github.com/meysamhadeli/codai/constants/lipgloss"
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
		handleCodeCommand(rootDependencies)
	},
}

func handleCodeCommand(rootDependencies *RootDependencies) {

	// Create a context with cancel function
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	//Channel to signal when the application should shut down
	done := make(chan bool)

	sigs := make(chan os.Signal, 1)

	signal.Notify(sigs, syscall.SIGINT, syscall.SIGTERM)

	go utils.GracefulShutdown(ctx, done, sigs, func() {
		rootDependencies.ChatHistory.ClearHistory()
	})

	reader := bufio.NewReader(os.Stdin)

	var requestedContext string

	var fullContextFiles []models.FileData

	var fullContextCodes []string

	codeOptionsBox := lipgloss.BoxStyle.Render(":help  Help for code subcommand")
	fmt.Println(codeOptionsBox)

	spinnerLoadContext, err := pterm.DefaultSpinner.WithStyle(pterm.NewStyle(pterm.FgLightBlue)).WithSequence("⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏").WithDelay(100).Start("Loading Context...")

	// Get all data files from the root directory
	fullContextFiles, fullContextCodes, err = rootDependencies.Analyzer.GetProjectFiles(rootDependencies.Cwd)

	if err != nil {
		spinnerLoadContext.Stop()
		fmt.Println(lipgloss.Red.Render(fmt.Sprintf("%v", err)))
	}

	spinnerLoadContext.Stop()

	// Launch the user input handler in a goroutine
startLoop: // Label for the start loop
	for {
		select {
		case <-ctx.Done():
			<-done // Wait for gracefulShutdown to complete
			return

		default:

			displayTokens := func() {
				rootDependencies.TokenManagement.DisplayTokens(rootDependencies.Config.AIProviderConfig.ProviderName, rootDependencies.Config.AIProviderConfig.ChatCompletionModel, rootDependencies.Config.AIProviderConfig.EmbeddingModel, rootDependencies.Config.RAG)
			}

			// Get user input
			userInput, err := utils.InputPrompt(reader)
			if err != nil {
				fmt.Println(lipgloss.Red.Render(fmt.Sprintf("%v", err)))
				continue
			}

			// Configure help code subcommand
			isHelpSubcommands, exit := findCodeSubCommand(userInput, rootDependencies)

			if isHelpSubcommands {
				continue
			}

			if exit {
				cancel() // Initiate shutdown for the app's own ":exit" command
				<-done   // Wait for gracefulShutdown to complete
				return
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
					fmt.Println(lipgloss.Red.Render(fmt.Sprintf("%v", err)))
					displayTokens()
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
						return fmt.Errorf(lipgloss.Red.Render("no embeddings returned for user query"))
					}

					// Find relevant chunks with a similarity threshold of 0.3, no topN limit (-1 means all results and positive number only return this relevant results number)
					topN := -1

					// Step 6: Find relevant code chunks based on the user query embedding
					fullContextCodes = rootDependencies.Store.FindRelevantChunks(queryEmbedding[0], topN, rootDependencies.Config.AIProviderConfig.Threshold)
					return nil
				}

				// Call the retryWithBackoff function with the operation and a 3 time retry
				err = utils.RetryWithBackoff(queryEmbeddingOperation, 3)

				if err != nil {
					spinnerLoadContextEmbedding.Stop()
					fmt.Println(lipgloss.Red.Render(fmt.Sprintf("%v", err)))
					displayTokens()
					continue startLoop
				}

				spinnerLoadContextEmbedding.Stop()
			}

			var aiResponseBuilder strings.Builder

			chatRequestOperation := func() error {
				finalPrompt, userInputPrompt := rootDependencies.Analyzer.GeneratePrompt(fullContextCodes, rootDependencies.ChatHistory.GetHistory(), userInput, requestedContext)

				// Step 7: Send the relevant code and user input to the AI API
				var b = finalPrompt + userInputPrompt
				fmt.Println(b)
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
				fmt.Println(lipgloss.Red.Render(fmt.Sprintf("%v", err)))
				displayTokens()
				continue startLoop
			}

			if !rootDependencies.Config.RAG {
				// Try to get full block code if block codes is summarized and incomplete
				requestedContext, err = rootDependencies.Analyzer.TryGetInCompletedCodeBlocK(aiResponseBuilder.String())

				if requestedContext != "" && err == nil {
					aiResponseBuilder.Reset()

					fmt.Println(lipgloss.BlueSky.Render("\nThese files need to changes...\n"))

					err = chatRequestOperation()

					if err != nil {
						fmt.Println(lipgloss.Red.Render(fmt.Sprintf("%v", err)))
						displayTokens()
						continue
					}
				}
			}

			// Extract code from AI response and structure this code to apply to git
			changes := rootDependencies.Analyzer.ExtractCodeChanges(aiResponseBuilder.String())

			if changes == nil {
				fmt.Println(lipgloss.BlueSky.Render("\nno code blocks with a valid path detected to apply."))
				displayTokens()
				continue
			}

			var updateContextNeeded = false

			fmt.Print("\n\n")

			// Try to apply changes
			for _, change := range changes {

				// Prompt the user to accept or reject the changes
				promptAccepted, err := utils.ConfirmPrompt(change.RelativePath)
				if err != nil {
					fmt.Println(lipgloss.Red.Render(fmt.Sprintf("error getting user prompt: %v", err)))
					continue
				}

				if promptAccepted {
					err := rootDependencies.Analyzer.ApplyChanges(change.RelativePath, change.Code)
					if err != nil {
						fmt.Println(lipgloss.Red.Render(fmt.Sprintf("error applying changes: %v", err)))
						continue
					}
					fmt.Println(lipgloss.Green.Render("✔️ Changes accepted!"))

					updateContextNeeded = true

				} else {
					fmt.Println(lipgloss.Red.Render("❌ Changes rejected."))
				}
			}

			// If we need Update the context after apply changes
			if updateContextNeeded {

				spinnerUpdateContext, err := pterm.DefaultSpinner.WithStyle(pterm.NewStyle(pterm.FgLightBlue)).WithSequence("⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏").WithDelay(100).Start("Updating Context...")

				fullContextFiles, fullContextCodes, err = rootDependencies.Analyzer.GetProjectFiles(rootDependencies.Cwd)
				if err != nil {
					spinnerUpdateContext.Stop()
					fmt.Println(lipgloss.Red.Render(fmt.Sprintf("%v", err)))
				}
				spinnerUpdateContext.Stop()
			}
			displayTokens()
		}
	}
}

func findCodeSubCommand(command string, rootDependencies *RootDependencies) (bool, bool) {
	switch command {
	case ":help":
		helps := ":clear  Clear screen\n:exit  Exit from codai\n:token  Token information\n:clear-token  Clear token from session\n:clear-history  Clear history of chat from session"
		styledHelps := lipgloss.BoxStyle.Render(helps)
		fmt.Println(styledHelps)
		return true, false
	case ":clear":
		fmt.Print("\033[2J\033[H")
		return true, false
	case ":exit":
		return false, false
	case ":token":
		rootDependencies.TokenManagement.DisplayTokens(
			rootDependencies.Config.AIProviderConfig.ProviderName,
			rootDependencies.Config.AIProviderConfig.ChatCompletionModel,
			rootDependencies.Config.AIProviderConfig.EmbeddingModel,
			rootDependencies.Config.RAG,
		)
		return true, false
	case ":clear-token":
		rootDependencies.TokenManagement.ClearToken()
		return true, false
	case ":clear-history":
		rootDependencies.ChatHistory.ClearHistory()
		return true, false
	default:
		return false, false
	}
}
