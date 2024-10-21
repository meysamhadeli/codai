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
	Version          string                              `mapstructure:"version"`
	MarkdownConfig   *markdown_generators.MarkdownConfig `mapstructure:"markdown_config"`
	AIProviderConfig *providers.AIProviderConfig         `mapstructure:"ai_provider_config"`
}

// Default configuration values
var defaultConfig = Config{
	Version: "1.0",
	AIProviderConfig: &providers.AIProviderConfig{
		ProviderName:        "ollama",
		EmbeddingURL:        "http://localhost:11434/v1/embeddings",
		ChatCompletionURL:   "http://localhost:11434/v1/chat/completions",
		ChatCompletionModel: "deepseek-coder-v2",
		EmbeddingModel:      "all-minilm:l6-v2",
		Stream:              true,
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
	viper.SetDefault("version", defaultConfig.Version)
	viper.SetDefault("markdown_config.diff_viewer", defaultConfig.MarkdownConfig.DiffViewer)
	viper.SetDefault("markdown_config.Theme", defaultConfig.MarkdownConfig.Theme)
	viper.SetDefault("ai_provider_config.provider_name", defaultConfig.AIProviderConfig.ProviderName)
	viper.SetDefault("ai_provider_config.embedding_url", defaultConfig.AIProviderConfig.EmbeddingURL)
	viper.SetDefault("ai_provider_config.chat_completion_url", defaultConfig.AIProviderConfig.ChatCompletionURL)
	viper.SetDefault("ai_provider_config.chat_completion_model", defaultConfig.AIProviderConfig.ChatCompletionModel)
	viper.SetDefault("ai_provider_config.embedding_model", defaultConfig.AIProviderConfig.EmbeddingModel)
	viper.SetDefault("ai_provider_config.stream", defaultConfig.AIProviderConfig.Stream)
	viper.SetDefault("ai_provider_config.encoding_format", defaultConfig.AIProviderConfig.EncodingFormat)
	viper.SetDefault("ai_provider_config.temperature", defaultConfig.AIProviderConfig.Temperature)
	viper.SetDefault("ai_provider_config.api_key", defaultConfig.AIProviderConfig.ApiKey)

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
	_ = viper.BindPFlag("version", rootCmd.Flags().Lookup("version"))
	_ = viper.BindPFlag("markdown_config.DiffViewer", rootCmd.Flags().Lookup("diff_viewer"))
	_ = viper.BindPFlag("markdown_config.Theme", rootCmd.Flags().Lookup("theme"))
	_ = viper.BindPFlag("ai_provider_config.provider_name", rootCmd.Flags().Lookup("provider_name"))
	_ = viper.BindPFlag("ai_provider_config.embedding_url", rootCmd.Flags().Lookup("embedding_url"))
	_ = viper.BindPFlag("ai_provider_config.chat_completion_url", rootCmd.Flags().Lookup("chat_completion_url"))
	_ = viper.BindPFlag("ai_provider_config.chat_completion_model", rootCmd.Flags().Lookup("chat_completion_model"))
	_ = viper.BindPFlag("ai_provider_config.embedding_model", rootCmd.Flags().Lookup("embedding_model"))
	_ = viper.BindPFlag("ai_provider_config.stream", rootCmd.Flags().Lookup("stream"))
	_ = viper.BindPFlag("ai_provider_config.encoding_format", rootCmd.Flags().Lookup("encoding_format"))
	_ = viper.BindPFlag("ai_provider_config.temperature", rootCmd.Flags().Lookup("temperature"))
	_ = viper.BindPFlag("ai_provider_config.api_key", rootCmd.Flags().Lookup("api_key"))
}

// InitFlags initializes the flags for the root command.
func InitFlags(rootCmd *cobra.Command) {
	// Use PersistentFlags so that these flags are available in all subcommands
	rootCmd.PersistentFlags().StringVarP(&cfgFile, "config", "c", "", "config file (optional)")
	rootCmd.PersistentFlags().StringP("version", "v", defaultConfig.Version, "Version of the service")
	rootCmd.PersistentFlags().String("diff_viewer", defaultConfig.MarkdownConfig.DiffViewer, "Diff viewer for markdown")
	rootCmd.PersistentFlags().StringP("theme", "t", defaultConfig.MarkdownConfig.Theme, "Markdown theme")
	rootCmd.PersistentFlags().StringP("provider_name", "p", defaultConfig.AIProviderConfig.ProviderName, "Provider Name")
	rootCmd.PersistentFlags().String("embedding_url", defaultConfig.AIProviderConfig.EmbeddingURL, "Embedding URL")
	rootCmd.PersistentFlags().String("chat_completion_url", defaultConfig.AIProviderConfig.ChatCompletionURL, "Chat Completion URL")
	rootCmd.PersistentFlags().String("chat_completion_model", defaultConfig.AIProviderConfig.ChatCompletionModel, "Chat Completion Model")
	rootCmd.PersistentFlags().String("embedding_model", defaultConfig.AIProviderConfig.EmbeddingModel, "Embedding Model")
	rootCmd.PersistentFlags().Bool("stream", defaultConfig.AIProviderConfig.Stream, "Stream")
	rootCmd.PersistentFlags().String("encoding_format", defaultConfig.AIProviderConfig.EncodingFormat, "Encoding Format")
	rootCmd.PersistentFlags().Float32("temperature", defaultConfig.AIProviderConfig.Temperature, "Temperature")
	rootCmd.PersistentFlags().String("api_key", defaultConfig.AIProviderConfig.ApiKey, "ApiKey")
}
