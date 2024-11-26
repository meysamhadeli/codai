package config

import (
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss"
	"github.com/meysamhadeli/codai/providers"
	"github.com/spf13/cobra"
	"github.com/spf13/viper"
	"os"
)

// Config represents the structure of the configuration file
type Config struct {
	Version          string                      `mapstructure:"version"`
	Theme            string                      `mapstructure:"theme"`
	RAG              bool                        `mapstructure:"rag"`
	AIProviderConfig *providers.AIProviderConfig `mapstructure:"ai_provider_config"`
}

// Default configuration values
var defaultConfig = Config{
	Version: "1.7.4",
	Theme:   "dracula",
	RAG:     true,
	AIProviderConfig: &providers.AIProviderConfig{
		ChatProviderName:       "openai",
		EmbeddingsProviderName: "openai",
		ChatBaseURL:            "https://api.openai.com",
		EmbeddingsBaseURL:      "https://api.openai.com",
		ChatModel:              "gpt-4o",
		EmbeddingsModel:        "text-embedding-3-small",
		Stream:                 true,
		EncodingFormat:         "float",
		Temperature:            0.2,
		Threshold:              0,
		ChatApiVersion:         "",
		EmbeddingsApiVersion:   "",
		ChatApiKey:             "",
		EmbeddingsApiKey:       "",
	},
}

// cfgFile holds the path to the configuration file (set via CLI)
var cfgFile string

// LoadConfigs initializes the configuration from file, flags, and environment variables, and returns the final config.
func LoadConfigs(rootCmd *cobra.Command, cwd string) *Config {
	var config *Config

	// Set default values using Viper
	viper.SetDefault("version", defaultConfig.Version)
	viper.SetDefault("theme", defaultConfig.Theme)
	viper.SetDefault("rag", defaultConfig.RAG)
	viper.SetDefault("ai_provider_config.chat_provider_name", defaultConfig.AIProviderConfig.ChatProviderName)
	viper.SetDefault("ai_provider_config.embeddings_provider_name", defaultConfig.AIProviderConfig.EmbeddingsProviderName)
	viper.SetDefault("ai_provider_config.chat_base_url", defaultConfig.AIProviderConfig.ChatBaseURL)
	viper.SetDefault("ai_provider_config.embeddings_base_url", defaultConfig.AIProviderConfig.EmbeddingsBaseURL)
	viper.SetDefault("ai_provider_config.chat_model", defaultConfig.AIProviderConfig.ChatModel)
	viper.SetDefault("ai_provider_config.embeddings_model", defaultConfig.AIProviderConfig.EmbeddingsModel)
	viper.SetDefault("ai_provider_config.encoding_format", defaultConfig.AIProviderConfig.EncodingFormat)
	viper.SetDefault("ai_provider_config.temperature", defaultConfig.AIProviderConfig.Temperature)
	viper.SetDefault("ai_provider_config.threshold", defaultConfig.AIProviderConfig.Threshold)
	viper.SetDefault("ai_provider_config.stream", defaultConfig.AIProviderConfig.Stream)
	viper.SetDefault("ai_provider_config.chat_api_key", defaultConfig.AIProviderConfig.ChatApiKey)
	viper.SetDefault("ai_provider_config.embeddings_api_key", defaultConfig.AIProviderConfig.EmbeddingsApiKey)
	viper.SetDefault("ai_provider_config.chat_api_version", defaultConfig.AIProviderConfig.ChatApiVersion)
	viper.SetDefault("ai_provider_config.embeddings_api_version", defaultConfig.AIProviderConfig.EmbeddingsApiVersion)

	// Automatically read environment variables
	viper.AutomaticEnv() // This will look for variables that match config keys directly

	// Explicitly bind environment variables to config keys
	bindEnv()

	// Check if the user provided a config file
	if cfgFile != "" {
		viper.SetConfigFile(cfgFile)
		if err := viper.ReadInConfig(); err != nil {
			fmt.Println(lipgloss.Red.Render(fmt.Sprintf("Error reading config file: %v", err)))
			os.Exit(1)
		}
	} else {
		// Automatically look for 'config.yml' in the working directory if no CLI file is provided
		viper.SetConfigName("codai-config") // name of config file (without extension)
		viper.SetConfigType("yml")          // Required if file extension is not yaml/yml
		viper.AddConfigPath(cwd)            // Look for config in the current working directory
	}

	// Read the configuration file if available
	if err := viper.ReadInConfig(); err == nil {

	} else if cfgFile != "" {
		// If a specific config file was set but not found, show error and exit
		fmt.Println(lipgloss.Red.Render(fmt.Sprintf("Error reading config file: %v", err)))
		os.Exit(1)
	}

	// Bind CLI flags to override config values
	bindFlags(rootCmd)

	// Unmarshal the configuration into the Config struct
	if err := viper.Unmarshal(&config); err != nil {
		fmt.Println(lipgloss.Red.Render(fmt.Sprintf("Unable to decode into struct: %v", err)))
		os.Exit(1)
	}

	return config
}

// bindEnv explicitly binds environment variables to configuration keys
func bindEnv() {
	_ = viper.BindEnv("theme", "THEME")
	_ = viper.BindEnv("rag", "RAG")
	_ = viper.BindEnv("ai_provider_config.chat_provider_name", "CHAT_PROVIDER_NAME")
	_ = viper.BindEnv("ai_provider_config.embeddings_provider_name", "EMBEDDINGS_PROVIDER_NAME")
	_ = viper.BindEnv("ai_provider_config.chat_base_url", "CHAT_BASE_URL")
	_ = viper.BindEnv("ai_provider_config.embeddings_base_url", "EMBEDDINGS_BASE_URL")
	_ = viper.BindEnv("ai_provider_config.chat_model", "CHAT_MODEL")
	_ = viper.BindEnv("ai_provider_config.embeddings_model", "EMBEDDINGS_MODEL")
	_ = viper.BindEnv("ai_provider_config.temperature", "TEMPERATURE")
	_ = viper.BindEnv("ai_provider_config.threshold", "THRESHOLD")
	_ = viper.BindEnv("ai_provider_config.chat_api_key", "CHAT_API_KEY")
	_ = viper.BindEnv("ai_provider_config.embeddings_api_key", "EMBEDDINGS_API_KEY")
	_ = viper.BindEnv("ai_provider_config.chat_api_version", "CHAT_API_VERSION")
	_ = viper.BindEnv("ai_provider_config.embeddings_api_version", "EMBEDDINGS_API_VERSION")
}

// bindFlags binds the CLI flags to configuration values.
func bindFlags(rootCmd *cobra.Command) {
	_ = viper.BindPFlag("theme", rootCmd.Flags().Lookup("theme"))
	_ = viper.BindPFlag("rag", rootCmd.Flags().Lookup("rag"))
	_ = viper.BindPFlag("ai_provider_config.chat_provider_name", rootCmd.Flags().Lookup("chat_provider_name"))
	_ = viper.BindPFlag("ai_provider_config.embeddings_provider_name", rootCmd.Flags().Lookup("embeddings_provider_name"))
	_ = viper.BindPFlag("ai_provider_config.chat_base_url", rootCmd.Flags().Lookup("chat_base_url"))
	_ = viper.BindPFlag("ai_provider_config.embeddings_base_url", rootCmd.Flags().Lookup("embeddings_base_url"))
	_ = viper.BindPFlag("ai_provider_config.chat_model", rootCmd.Flags().Lookup("chat_model"))
	_ = viper.BindPFlag("ai_provider_config.embeddings_model", rootCmd.Flags().Lookup("embeddings_model"))
	_ = viper.BindPFlag("ai_provider_config.temperature", rootCmd.Flags().Lookup("temperature"))
	_ = viper.BindPFlag("ai_provider_config.threshold", rootCmd.Flags().Lookup("threshold"))
	_ = viper.BindPFlag("ai_provider_config.chat_api_key", rootCmd.Flags().Lookup("chat_api_key"))
	_ = viper.BindPFlag("ai_provider_config.embeddings_api_key", rootCmd.Flags().Lookup("embeddings_api_key"))
	_ = viper.BindPFlag("ai_provider_config.chat_api_version", rootCmd.Flags().Lookup("chat_api_version"))
	_ = viper.BindPFlag("ai_provider_config.embeddings_api_version", rootCmd.Flags().Lookup("embeddings_api_version"))
}

// InitFlags initializes the flags for the root command.
func InitFlags(rootCmd *cobra.Command) {
	// Use PersistentFlags so that these flags are available in all subcommands
	rootCmd.PersistentFlags().StringVarP(&cfgFile, "config", "c", "", "Specifies the path to a configuration file that contains all the settings for the application. This file can be used to override defaults.")
	rootCmd.PersistentFlags().String("theme", defaultConfig.Theme, "Set customize theme for buffering response from ai. (e.g., 'dracula', 'light', 'dark')")
	rootCmd.PersistentFlags().Bool("rag", defaultConfig.RAG, "Enable Retrieval-Augmented Generation (RAG) for enhanced responses using relevant data retrieval (e.g., default is 'enabled' and just retrieve related context base on user request).")
	rootCmd.PersistentFlags().StringP("version", "v", defaultConfig.Version, "Specifies the version of the application or service. This helps to track the release or update of the software.")
	rootCmd.PersistentFlags().String("chat_provider_name", defaultConfig.AIProviderConfig.ChatProviderName, "Specifies the name of the chat AI service provider (e.g., 'openai' or 'ollama'). This determines which service or API will be used for AI-related functions.")
	rootCmd.PersistentFlags().String("embeddings_provider_name", defaultConfig.AIProviderConfig.EmbeddingsProviderName, "Specifies the name of the embeddings AI service provider (e.g., 'openai' or 'ollama'). This determines which service or API will be used for AI-related functions.")
	rootCmd.PersistentFlags().String("chat_base_url", defaultConfig.AIProviderConfig.ChatBaseURL, "The chat base URL of AI Provider (e.g., default is 'https://api.openai.com'.")
	rootCmd.PersistentFlags().String("embeddings_base_url", defaultConfig.AIProviderConfig.EmbeddingsBaseURL, "The embeddings base URL of AI Provider (e.g., default is 'https://api.openai.com'.")
	rootCmd.PersistentFlags().String("chat_model", defaultConfig.AIProviderConfig.ChatModel, "The name of the model used for chat completions, such as 'gpt-4o'. Different models offer varying levels of performance and capabilities.")
	rootCmd.PersistentFlags().String("embeddings_model", defaultConfig.AIProviderConfig.EmbeddingsModel, "Specifies the AI model used for generating text embeddings (e.g., 'text-embedding-ada-002'). This model converts text into vector representations for similarity comparisons.")
	rootCmd.PersistentFlags().Float32("temperature", defaultConfig.AIProviderConfig.Temperature, "Adjusts the AI model’s creativity by setting a temperature value. Higher values result in more creative or varied responses, while lower values make them more focused (e.g., value should be between '0 - 1' and default is '0.2').")
	rootCmd.PersistentFlags().Float64("threshold", defaultConfig.AIProviderConfig.Threshold, "Sets the threshold for similarity calculations in AI systems. Higher values will require closer matches and should be careful not to lose matches, while lower values provide a wider range of results to prevent losing any matches. (e.g., value should be between '0.2 - 1' and default is '0.3').")
	rootCmd.PersistentFlags().String("chat_api_key", defaultConfig.AIProviderConfig.ChatApiKey, "The chat API key used to authenticate with the AI service provider.")
	rootCmd.PersistentFlags().String("embeddings_api_key", defaultConfig.AIProviderConfig.EmbeddingsApiKey, "The embeddings API key used to authenticate with the AI service provider.")
	rootCmd.PersistentFlags().String("chat_api_version", defaultConfig.AIProviderConfig.ChatApiVersion, "The API version used to authenticate with the chat AI service provider.")
	rootCmd.PersistentFlags().String("embeddings_api_version", defaultConfig.AIProviderConfig.EmbeddingsApiVersion, "The API version used to authenticate with the embeddings AI service provider.")
}
