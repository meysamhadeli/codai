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
	ctx, cancel := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
	defer cancel()

	var requestedContext string
	var fullContext *models.FullContextData

	spinner := pterm.DefaultSpinner.WithStyle(pterm.NewStyle(pterm.FgLightBlue)).WithSequence("⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏").WithDelay(100).WithRemoveWhenDone(true)

	go utils.GracefulShutdown(ctx, cancel, func() {

		rootDependencies.ChatHistory.ClearHistory()
		rootDependencies.TokenManagement.ClearToken()
	})

	reader := bufio.NewReader(os.Stdin)

	codeOptionsBox := lipgloss.BoxStyle.Render(":help  Help for code subcommand")
	fmt.Println(codeOptionsBox)

	spinnerLoadContext, _ := spinner.Start("Loading Context...")

	// Get all data files from the root directory
	fullContext, err := rootDependencies.Analyzer.GetProjectFiles(rootDependencies.Cwd)

	if err != nil {
		spinnerLoadContext.Stop()
		fmt.Print("\r")
		fmt.Println(lipgloss.Red.Render(fmt.Sprintf("%v", err)))
	}

	spinnerLoadContext.Stop()
	fmt.Print("\r")

	// Launch the user input handler in a goroutine
startLoop: // Label for the start loop
	for {
		select {
		case <-ctx.Done():
			// Wait for GracefulShutdown to complete
			return

		default:
			displayTokens := func() {
				rootDependencies.TokenManagement.DisplayTokens(rootDependencies.Config.AIProviderConfig.Provider, rootDependencies.Config.AIProviderConfig.Model)
			}

			// Get user input
			userInput, err := utils.InputPrompt(reader)

			if err != nil {
				fmt.Println(lipgloss.Red.Render(fmt.Sprintf("%v", err)))
				continue
			}

			if userInput == "" {
				fmt.Print("\r")
				continue
			}

			// Configure help code subcommand
			isHelpSubcommands, exit := findCodeSubCommand(userInput, rootDependencies)

			if isHelpSubcommands {
				continue
			}

			if exit {
				return
			}

			var aiResponseBuilder strings.Builder

			chatRequestOperation := func() error {

				finalPrompt, userInputPrompt := rootDependencies.Analyzer.GeneratePrompt(fullContext.RawCodes, rootDependencies.ChatHistory.GetHistory(), userInput, requestedContext)

				// Step 7: Send the relevant code and user input to the AI API
				responseChan := rootDependencies.CurrentChatProvider.ChatCompletionRequest(ctx, userInputPrompt, finalPrompt)

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
						return fmt.Errorf("Error rendering markdown: %v", err)
					}
				}

				return nil
			}

			// Try to get full block code if block codes is summarized and incomplete
			requestedContext, err = rootDependencies.Analyzer.TryGetInCompletedCodeBlocK(aiResponseBuilder.String())

			if requestedContext != "" && err == nil {
				aiResponseBuilder.Reset()

				fmt.Print("\n")

				contextAccepted, err := utils.ConfirmAdditinalContext(reader)
				if err != nil {
					fmt.Println(lipgloss.Red.Render(fmt.Sprintf("error getting user prompt: %v", err)))
					continue
				}

				if contextAccepted {
					fmt.Println(lipgloss.Green.Render("✔️ Context accepted!"))

					if err := chatRequestOperation(); err != nil {
						fmt.Println(lipgloss.Red.Render(fmt.Sprintf("%v", err)))
						displayTokens()
						continue
					}

				} else {
					fmt.Println(lipgloss.Red.Render("❌ Context rejected."))
				}
			}

			if err := chatRequestOperation(); err != nil {
				fmt.Println(lipgloss.Red.Render(fmt.Sprintf("%v", err)))
				displayTokens()
				continue startLoop
			}

			// Extract code from AI response and structure this code to apply to git
			changes := rootDependencies.Analyzer.ExtractCodeChanges(aiResponseBuilder.String())

			if changes == nil {
				fmt.Println()
				displayTokens()
				continue
			}

			fmt.Print("\n")

			// Try to apply changes
			for _, change := range changes {

				// Prompt the user to accept or reject the changes
				promptAccepted, err := utils.ConfirmPrompt(change.RelativePath, reader)
				if err != nil {
					fmt.Println(lipgloss.Red.Render(fmt.Sprintf("Error getting user prompt: %v", err)))
					continue
				}

				if promptAccepted {
					err := rootDependencies.Analyzer.ApplyChanges(change.RelativePath, change.Code)
					if err != nil {
						fmt.Println(lipgloss.Red.Render(fmt.Sprintf("Error applying changes: %v", err)))
						continue
					}
					fmt.Println(lipgloss.Green.Render("✔️ Changes accepted!"))

					fmt.Print("\r")

				} else {
					fmt.Println(lipgloss.Red.Render("❌ Changes rejected."))
				}
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
		return false, true
	case ":token":
		rootDependencies.TokenManagement.DisplayTokens(
			rootDependencies.Config.AIProviderConfig.Provider,
			rootDependencies.Config.AIProviderConfig.Model,
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
