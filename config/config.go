package config

import (
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss_color"
	"github.com/meysamhadeli/codai/providers"
	"github.com/spf13/cobra"
	"github.com/spf13/viper"
	"os"
)

// Config represents the structure of the configuration file
type Config struct {
	Version          string                      `mapstructure:"version"`
	AIProviderConfig *providers.AIProviderConfig `mapstructure:"ai_provider_config"`
}

// Default configuration values
var defaultConfig = Config{
	Version: "1.0",
	AIProviderConfig: &providers.AIProviderConfig{
		ProviderName:        "ollama",
		EmbeddingURL:        "http://localhost:11434/v1/embeddings",
		ChatCompletionURL:   "http://localhost:11434/v1/chat/completions",
		ChatCompletionModel: "deepseek-coder-v2",
		EmbeddingModel:      "nomic-embed-text",
		Stream:              true,
		EncodingFormat:      "float",
		Temperature:         0.2,
		Threshold:           0,
		BufferingTheme:      "dracula",
		ApiKey:              "",
	},
}

// cfgFile holds the path to the configuration file (set via CLI)
var cfgFile string

// LoadConfigs initializes the configuration from file, flags, and environment variables, and returns the final config.
func LoadConfigs(rootCmd *cobra.Command, cwd string) *Config {
	var config *Config

	// Set default values using Viper
	viper.SetDefault("version", defaultConfig.Version)
	viper.SetDefault("ai_provider_config.provider_name", defaultConfig.AIProviderConfig.ProviderName)
	viper.SetDefault("ai_provider_config.embedding_url", defaultConfig.AIProviderConfig.EmbeddingURL)
	viper.SetDefault("ai_provider_config.chat_completion_url", defaultConfig.AIProviderConfig.ChatCompletionURL)
	viper.SetDefault("ai_provider_config.chat_completion_model", defaultConfig.AIProviderConfig.ChatCompletionModel)
	viper.SetDefault("ai_provider_config.embedding_model", defaultConfig.AIProviderConfig.EmbeddingModel)
	viper.SetDefault("ai_provider_config.stream", defaultConfig.AIProviderConfig.Stream)
	viper.SetDefault("ai_provider_config.encoding_format", defaultConfig.AIProviderConfig.EncodingFormat)
	viper.SetDefault("ai_provider_config.temperature", defaultConfig.AIProviderConfig.Temperature)
	viper.SetDefault("ai_provider_config.threshold", defaultConfig.AIProviderConfig.Threshold)
	viper.SetDefault("ai_provider_config.buffering_theme", defaultConfig.AIProviderConfig.BufferingTheme)
	viper.SetDefault("ai_provider_config.api_key", defaultConfig.AIProviderConfig.ApiKey)

	// Automatically read environment variables
	viper.AutomaticEnv() // This will look for variables that match config keys directly

	// Explicitly bind environment variables to config keys
	bindEnv()

	// Check if the user provided a config file
	if cfgFile != "" {
		viper.SetConfigFile(cfgFile)
		if err := viper.ReadInConfig(); err != nil {
			fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Error reading config file: %v", err)))
			os.Exit(1)
		}
	}

	// Check if the user provided a config file via CLI
	if cfgFile != "" {
		viper.SetConfigFile(cfgFile)
	} else {
		// Automatically look for 'config.yml' in the working directory if no CLI file is provided
		viper.SetConfigName("config") // name of config file (without extension)
		viper.SetConfigType("yml")    // Required if file extension is not yaml/yml
		viper.AddConfigPath(cwd)      // Look for config in the current working directory
	}

	// Read the configuration file if available
	err := viper.ReadInConfig()
	if err != nil {
		fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Error reading config file: %v", err)))
		os.Exit(1)
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

// bindEnv explicitly binds environment variables to configuration keys
func bindEnv() {
	viper.BindEnv("ai_provider_config.provider_name")
	viper.BindEnv("ai_provider_config.embedding_url")
	viper.BindEnv("ai_provider_config.chat_completion_url")
	viper.BindEnv("ai_provider_config.chat_completion_model")
	viper.BindEnv("ai_provider_config.embedding_model")
	viper.BindEnv("ai_provider_config.stream")
	viper.BindEnv("ai_provider_config.encoding_format")
	viper.BindEnv("ai_provider_config.temperature")
	viper.BindEnv("ai_provider_config.threshold")
	viper.BindEnv("ai_provider_config.buffering_theme")
	viper.BindEnv("ai_provider_config.api_key")
}

// bindFlags binds the CLI flags to configuration values.
func bindFlags(rootCmd *cobra.Command) {
	_ = viper.BindPFlag("ai_provider_config.provider_name", rootCmd.Flags().Lookup("provider_name"))
	_ = viper.BindPFlag("ai_provider_config.embedding_url", rootCmd.Flags().Lookup("embedding_url"))
	_ = viper.BindPFlag("ai_provider_config.chat_completion_url", rootCmd.Flags().Lookup("chat_completion_url"))
	_ = viper.BindPFlag("ai_provider_config.chat_completion_model", rootCmd.Flags().Lookup("chat_completion_model"))
	_ = viper.BindPFlag("ai_provider_config.embedding_model", rootCmd.Flags().Lookup("embedding_model"))
	_ = viper.BindPFlag("ai_provider_config.stream", rootCmd.Flags().Lookup("stream"))
	_ = viper.BindPFlag("ai_provider_config.encoding_format", rootCmd.Flags().Lookup("encoding_format"))
	_ = viper.BindPFlag("ai_provider_config.temperature", rootCmd.Flags().Lookup("temperature"))
	_ = viper.BindPFlag("ai_provider_config.threshold", rootCmd.Flags().Lookup("threshold"))
	_ = viper.BindPFlag("ai_provider_config.buffering_theme", rootCmd.Flags().Lookup("buffering_theme"))
	_ = viper.BindPFlag("ai_provider_config.api_key", rootCmd.Flags().Lookup("api_key"))
}

// InitFlags initializes the flags for the root command.
func InitFlags(rootCmd *cobra.Command) {
	// Use PersistentFlags so that these flags are available in all subcommands
	rootCmd.PersistentFlags().StringVarP(&cfgFile, "config", "c", "", "Specifies the path to a configuration file that contains all the settings for the application. This file can be used to override defaults.")
	rootCmd.PersistentFlags().StringP("version", "v", defaultConfig.Version, "Specifies the version of the application or service. This helps to track the release or update of the software.")
	rootCmd.PersistentFlags().StringP("provider_name", "p", defaultConfig.AIProviderConfig.ProviderName, "Specifies the name of the AI service provider (e.g., 'ollama'). This determines which service or API will be used for AI-related functions.")
	rootCmd.PersistentFlags().String("embedding_url", defaultConfig.AIProviderConfig.EmbeddingURL, "The API endpoint used for text embedding requests. This URL points to the server that processes and returns text embeddings.")
	rootCmd.PersistentFlags().String("chat_completion_url", defaultConfig.AIProviderConfig.ChatCompletionURL, "The API endpoint for chat completion requests. This URL is where chat messages are sent to receive AI-generated responses.")
	rootCmd.PersistentFlags().String("chat_completion_model", defaultConfig.AIProviderConfig.ChatCompletionModel, "The name of the model used for chat completions, such as 'deepseek-coder-v2'. Different models offer varying levels of performance and capabilities.")
	rootCmd.PersistentFlags().String("embedding_model", defaultConfig.AIProviderConfig.EmbeddingModel, "Specifies the AI model used for generating text embeddings (e.g., 'nomic-embed-text'). This model converts text into vector representations for similarity comparisons.")
	rootCmd.PersistentFlags().Bool("stream", defaultConfig.AIProviderConfig.Stream, "Enables or disables streaming of responses. If set to true, AI responses are streamed incrementally.")
	rootCmd.PersistentFlags().String("encoding_format", defaultConfig.AIProviderConfig.EncodingFormat, "Specifies the format in which the AI embeddings or outputs are encoded (e.g., 'float' for floating-point numbers).")
	rootCmd.PersistentFlags().Float32("temperature", defaultConfig.AIProviderConfig.Temperature, "Adjusts the AI model’s creativity by setting a temperature value. Higher values result in more creative or varied responses, while lower values make them more focused.")
	rootCmd.PersistentFlags().Float64("threshold", defaultConfig.AIProviderConfig.Threshold, "Sets the threshold for similarity calculations in AI systems (e.g., for retrieving related data in a RAG system). Higher values will require closer matches.")
	rootCmd.PersistentFlags().String("buffering_theme", defaultConfig.AIProviderConfig.BufferingTheme, "Set customize theme for buffering response from ai. (e.g., 'dracula', 'light', 'dark')")
	rootCmd.PersistentFlags().String("api_key", defaultConfig.AIProviderConfig.ApiKey, "The API key used to authenticate with the AI service provider.")
}
