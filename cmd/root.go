package cmd

import (
	"fmt"
	"github.com/meysamhadeli/codai/chat_history"
	contracts2 "github.com/meysamhadeli/codai/chat_history/contracts"
	"github.com/meysamhadeli/codai/code_analyzer"
	contracts_analyzer "github.com/meysamhadeli/codai/code_analyzer/contracts"
	"github.com/meysamhadeli/codai/config"
	"github.com/meysamhadeli/codai/constants/lipgloss"
	"github.com/meysamhadeli/codai/providers"
	contracts_provider "github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/token_management"
	"github.com/meysamhadeli/codai/token_management/contracts"
	"github.com/spf13/cobra"
	"os"
)

// RootDependencies holds the dependencies for the root command
type RootDependencies struct {
	CurrentChatProvider contracts_provider.IChatAIProvider
	Analyzer            contracts_analyzer.ICodeAnalyzer
	Cwd                 string
	Config              *config.Config
	ChatHistory         contracts2.IChatHistory
	TokenManagement     contracts.ITokenManagement
}

// RootCmd represents the 'context' command
var rootCmd = &cobra.Command{
	Use:   "codai",
	Short: "codai CLI for coding and chatting",
	Long:  `Codai is an AI code assistant that helps developers through a session-based CLI, providing intelligent code suggestions, refactoring, and code reviews based on the full context of the project. It supports multiple LLM providers, such as OpenAI, Azure OpenAI, Ollama, Anthropic, and OpenRouter, to streamline daily development tasks.`,
	Run: func(cmd *cobra.Command, args []string) {
		versionFlag, _ := cmd.Flags().GetBool("version")
		if versionFlag {
			fmt.Printf("version: %s\n", config.DefaultConfig.Version)
			return
		}
		// Check if any arguments or subcommands were provided
		if len(args) == 0 {
			err := cmd.Help() // Display help if no subcommand or argument is provided
			if err != nil {
				return
			}
		} else {
			// Run the handleRootCommand if arguments are provided
			handleRootCommand(cmd)
		}
	},
}

func handleRootCommand(cmd *cobra.Command) *RootDependencies {

	var err error
	var rootDependencies = &RootDependencies{}

	// Get current working directory
	rootDependencies.Cwd, err = os.Getwd()
	if err != nil || rootDependencies.Cwd == "" {
		fmt.Println(lipgloss.Red.Render(fmt.Sprintf("error getting current directory")))
		return nil
	}

	rootDependencies.Config = config.LoadConfigs(cmd, rootDependencies.Cwd)

	rootDependencies.TokenManagement = token_management.NewTokenManager()

	rootDependencies.ChatHistory = chat_history.NewChatHistory()

	rootDependencies.Analyzer = code_analyzer.NewCodeAnalyzer(rootDependencies.Cwd)

	if err != nil {
		fmt.Println(lipgloss.Red.Render(fmt.Sprintf("%v", err)))
	}

	rootDependencies.CurrentChatProvider, err = providers.ChatProviderFactory(rootDependencies.Config.AIProviderConfig, rootDependencies.TokenManagement)

	if err != nil {
		fmt.Println(lipgloss.Red.Render(fmt.Sprintf("%v", err)))
	}

	return rootDependencies
}

// Execute adds all child commands to the root command and sets flags appropriately.
func Execute() {
	if err := rootCmd.Execute(); err != nil {
		fmt.Println(err)
		os.Exit(1)
	}
}

func init() {
	config.InitFlags(rootCmd)

	// Register subcommands
	rootCmd.AddCommand(codeCmd)
}
