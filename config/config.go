package config

import (
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss_color"
	"github.com/meysamhadeli/codai/markdown_generators"
	"github.com/meysamhadeli/codai/providers"
	"github.com/spf13/cobra"
	"github.com/spf13/viper"
	"os"
)

// Config represents the structure of the configuration file
type Config struct {
	Version          string
	MarkdownConfig   *markdown_generators.MarkdownConfig
	AIProviderConfig *providers.AIProviderConfig
}

// Default configuration values
var defaultConfig = Config{
	Version: "1.0",
	AIProviderConfig: &providers.AIProviderConfig{
		ProviderName:        "ollama",
		EmbeddingURL:        "http://localhost:11434/v1/embeddings",
		ChatCompletionURL:   "http://localhost:11434/v1/chat/completions",
		MaxCompletionTokens: 16000,
		ChatCompletionModel: "llama3.1",
		EmbeddingModel:      "all-minilm:l6-v2",
		Stream:              false,
		EncodingFormat:      "float",
		Temperature:         0.2,
		ApiKey:              "",
	},
	MarkdownConfig: &markdown_generators.MarkdownConfig{
		DiffViewer: "cli",
		Theme:      "dracula",
	},
}

// cfgFile holds the path to the configuration file (set via CLI)
var cfgFile string

// LoadConfigs initializes the configuration from file and flags, and returns the final config.
func LoadConfigs(rootCmd *cobra.Command) *Config {
	var config *Config

	// Set default values using Viper
	viper.SetDefault("Version", defaultConfig.Version)
	viper.SetDefault("MarkdownConfig.DiffViewer", defaultConfig.MarkdownConfig.DiffViewer)
	viper.SetDefault("MarkdownConfig.Theme", defaultConfig.MarkdownConfig.Theme)
	viper.SetDefault("AIProviderConfig.ProviderName", defaultConfig.AIProviderConfig.ProviderName)
	viper.SetDefault("AIProviderConfig.EmbeddingURL", defaultConfig.AIProviderConfig.EmbeddingURL)
	viper.SetDefault("AIProviderConfig.ChatCompletionURL", defaultConfig.AIProviderConfig.ChatCompletionURL)
	viper.SetDefault("AIProviderConfig.MaxCompletionTokens", defaultConfig.AIProviderConfig.MaxCompletionTokens)
	viper.SetDefault("AIProviderConfig.ChatCompletionModel", defaultConfig.AIProviderConfig.ChatCompletionModel)
	viper.SetDefault("AIProviderConfig.EmbeddingModel", defaultConfig.AIProviderConfig.EmbeddingModel)
	viper.SetDefault("AIProviderConfig.Stream", defaultConfig.AIProviderConfig.Stream)
	viper.SetDefault("AIProviderConfig.EncodingFormat", defaultConfig.AIProviderConfig.EncodingFormat)
	viper.SetDefault("AIProviderConfig.Temperature", defaultConfig.AIProviderConfig.Temperature)
	viper.SetDefault("AIProviderConfig.ApiKey", defaultConfig.AIProviderConfig.ApiKey)

	// Check if the user provided a config file
	if cfgFile != "" {
		viper.SetConfigFile(cfgFile)
		if err := viper.ReadInConfig(); err != nil {
			fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Error reading config file: %v", err)))
			os.Exit(1)
		}
	}

	// Bind CLI flags to override config values
	bindFlags(rootCmd)

	// Unmarshal the configuration into the Config struct
	if err := viper.Unmarshal(&config); err != nil {
		fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Unable to decode into struct: %v", err)))
		os.Exit(1)
	}

	return config
}

// bindFlags binds the CLI flags to configuration values.
func bindFlags(rootCmd *cobra.Command) {
	_ = viper.BindPFlag("Version", rootCmd.Flags().Lookup("version"))
	_ = viper.BindPFlag("MarkdownConfig.DiffViewer", rootCmd.Flags().Lookup("diff-viewer"))
	_ = viper.BindPFlag("MarkdownConfig.Theme", rootCmd.Flags().Lookup("theme"))
	_ = viper.BindPFlag("AIProviderConfig.ProviderName", rootCmd.Flags().Lookup("provider-name"))
	_ = viper.BindPFlag("AIProviderConfig.EmbeddingURL", rootCmd.Flags().Lookup("embedding-url"))
	_ = viper.BindPFlag("AIProviderConfig.ChatCompletionURL", rootCmd.Flags().Lookup("chat-completion-url"))
	_ = viper.BindPFlag("AIProviderConfig.MaxCompletionTokens", rootCmd.Flags().Lookup("max-completion-tokens"))
	_ = viper.BindPFlag("AIProviderConfig.ChatCompletionModel", rootCmd.Flags().Lookup("chat-completion-model"))
	_ = viper.BindPFlag("AIProviderConfig.EmbeddingModel", rootCmd.Flags().Lookup("embedding-model"))
	_ = viper.BindPFlag("AIProviderConfig.Stream", rootCmd.Flags().Lookup("stream"))
	_ = viper.BindPFlag("AIProviderConfig.EncodingFormat", rootCmd.Flags().Lookup("encoding-format"))
	_ = viper.BindPFlag("AIProviderConfig.Temperature", rootCmd.Flags().Lookup("temperature"))
	_ = viper.BindPFlag("AIProviderConfig.ApiKey", rootCmd.Flags().Lookup("api-key"))
}

// InitFlags initializes the flags for the root command.
func InitFlags(rootCmd *cobra.Command) {
	// Use PersistentFlags so that these flags are available in all subcommands
	rootCmd.PersistentFlags().StringVarP(&cfgFile, "config", "c", "", "config file (optional)")
	rootCmd.PersistentFlags().StringP("version", "v", defaultConfig.Version, "Version of the service")
	rootCmd.PersistentFlags().String("diff-viewer", defaultConfig.MarkdownConfig.DiffViewer, "Diff viewer for markdown")
	rootCmd.PersistentFlags().StringP("theme", "t", defaultConfig.MarkdownConfig.Theme, "Markdown theme")
	rootCmd.PersistentFlags().StringP("provider-name", "p", defaultConfig.AIProviderConfig.ProviderName, "Provider Name")
	rootCmd.PersistentFlags().String("embedding-url", defaultConfig.AIProviderConfig.EmbeddingURL, "Embedding URL")
	rootCmd.PersistentFlags().String("chat-completion-url", defaultConfig.AIProviderConfig.ChatCompletionURL, "Chat Completion URL")
	rootCmd.PersistentFlags().Int("max-completion-tokens", defaultConfig.AIProviderConfig.MaxCompletionTokens, "Max Completion Tokens")
	rootCmd.PersistentFlags().String("chat-completion-model", defaultConfig.AIProviderConfig.ChatCompletionModel, "Chat Completion Model")
	rootCmd.PersistentFlags().String("embedding-model", defaultConfig.AIProviderConfig.EmbeddingModel, "Embedding Model")
	rootCmd.PersistentFlags().Bool("stream", defaultConfig.AIProviderConfig.Stream, "Stream")
	rootCmd.PersistentFlags().String("encoding-format", defaultConfig.AIProviderConfig.EncodingFormat, "Encoding Format")
	rootCmd.PersistentFlags().Float32("temperature", defaultConfig.AIProviderConfig.Temperature, "Temperature")
	rootCmd.PersistentFlags().String("api-key", defaultConfig.AIProviderConfig.ApiKey, "Api-Key")
}
