package cmd

import (
	"fmt"
	"github.com/meysamhadeli/codai/code_analyzer"
	contracts_analyzer "github.com/meysamhadeli/codai/code_analyzer/contracts"
	"github.com/meysamhadeli/codai/config"
	"github.com/meysamhadeli/codai/constants/lipgloss_color"
	"github.com/meysamhadeli/codai/embedding_store"
	contracts_store "github.com/meysamhadeli/codai/embedding_store/contracts"
	"github.com/meysamhadeli/codai/markdown_generators"
	contracts_markdown "github.com/meysamhadeli/codai/markdown_generators/contracts"
	"github.com/meysamhadeli/codai/providers"
	contracts_provider "github.com/meysamhadeli/codai/providers/contracts"
	"github.com/spf13/cobra"
	"os"
)

// RootDependencies holds the dependencies for the root command
type RootDependencies struct {
	CurrentProvider contracts_provider.IAIProvider
	Store           contracts_store.IEmbeddingStore
	Analyzer        contracts_analyzer.ICodeAnalyzer
	Markdown        contracts_markdown.IMarkdownGenerator
	Cwd             string
	Config          *config.Config
}

var rootDependencies = &RootDependencies{}

// RootCmd represents the 'context' command
var rootCmd = &cobra.Command{
	Use:   "codai",
	Short: "Codai CLI for coding and chatting",
	Long: `Codai is a command-line interface (CLI) application that helps with coding and chatting
by providing an AI-powered assistant for development assistance and communication.`,
	Run: func(cmd *cobra.Command, args []string) {
		rootDependencies.Config = config.LoadConfigs(cmd)
		handleRootCommand()
	},
}

func handleRootCommand() *RootDependencies {

	var err error

	// Get current working directory
	rootDependencies.Cwd, err = os.Getwd()
	if err != nil || rootDependencies.Cwd == "" {
		fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Error getting current directory")))
		return nil
	}

	// Initialize Markdown
	rootDependencies.Markdown = markdown_generators.NewMarkdownGenerator("dracula", "git")

	// Initialize Analyzer
	rootDependencies.Analyzer = code_analyzer.NewCodeAnalyzer(rootDependencies.Cwd)

	// Initialize the embedding store model
	rootDependencies.Store = embedding_store.NewEmbeddingStoreModel()

	// Create a provider using the factory
	rootDependencies.CurrentProvider, err = providers.ProviderFactory("ollama")
	if err != nil {
		fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
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
	// Register subcommands
	rootCmd.AddCommand(codeCmd)
	rootCmd.AddCommand(chatCmd)
}
