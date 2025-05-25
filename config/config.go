package config

import (
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss"
	"github.com/meysamhadeli/codai/providers"
	"github.com/spf13/cobra"
	"github.com/spf13/viper"
	"os"
	"strings"
)

// Config represents the structure of the configuration file
type Config struct {
	Version          string                      `mapstructure:"version"`
	Theme            string                      `mapstructure:"theme"`
	AIProviderConfig *providers.AIProviderConfig `mapstructure:"ai_provider_config"`
}

// DefaultConfig values
var DefaultConfig = Config{
	Version: "1.8.4",
	Theme:   "dracula",
	AIProviderConfig: &providers.AIProviderConfig{
		Provider:        "openai",
		BaseURL:         "https://api.openai.com/v1",
		Model:           "gpt-4o",
		Stream:          true,
		EncodingFormat:  "float",
		Temperature:     nil,
		ReasoningEffort: nil,
		ApiVersion:      "",
		ApiKey:          "",
	},
}

// cfgFile holds the path to the configuration file (set via CLI)
var cfgFile string

// LoadConfigs initializes the configuration from file, flags, and environment variables, and returns the final config.
func LoadConfigs(rootCmd *cobra.Command, cwd string) *Config {
	var config *Config

	// Set default values using Viper
	setDefaults()

	// Automatically read environment variables
	viper.AutomaticEnv()

	// Explicitly bind environment variables to config keys
	bindEnv()

	// Check if the user provided a config file
	if cfgFile != "" {
		// Use the config file from the flag
		viper.SetConfigFile(cfgFile)
	} else {
		// Look for configuration files in the current directory
		viper.SetConfigName("codai-config") // Name of config file (without extension)
		viper.AddConfigPath(cwd)            // Look in the current working directory

		// Support both JSON and YAML formats
		viper.SetConfigType("yaml") // Set default type
		if err := viper.ReadInConfig(); err != nil {
			// If YAML fails, try JSON
			viper.SetConfigType("json")
			if err := viper.ReadInConfig(); err != nil {
				// If both fail, we'll continue with defaults
				fmt.Println(lipgloss.Yellow.Render("No configuration file found, using defaults"))
			}
		}
	}

	// Read the explicitly specified config file (if any)
	if cfgFile != "" {
		if err := viper.ReadInConfig(); err != nil {
			fmt.Println(lipgloss.Red.Render(fmt.Sprintf("Error reading config file: %v", err)))
			os.Exit(1)
		}
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

// setDefaults sets all default configuration values
func setDefaults() {
	viper.SetDefault("version", DefaultConfig.Version)
	viper.SetDefault("theme", DefaultConfig.Theme)
	viper.SetDefault("ai_provider_config.provider", DefaultConfig.AIProviderConfig.Provider)
	viper.SetDefault("ai_provider_config.base_url", DefaultConfig.AIProviderConfig.BaseURL)
	viper.SetDefault("ai_provider_config.model", DefaultConfig.AIProviderConfig.Model)
	viper.SetDefault("ai_provider_config.encoding_format", DefaultConfig.AIProviderConfig.EncodingFormat)
	viper.SetDefault("ai_provider_config.temperature", DefaultConfig.AIProviderConfig.Temperature)
	viper.SetDefault("ai_provider_config.reasoning_effort", DefaultConfig.AIProviderConfig.ReasoningEffort)
	viper.SetDefault("ai_provider_config.stream", DefaultConfig.AIProviderConfig.Stream)
	viper.SetDefault("ai_provider_config.api_key", DefaultConfig.AIProviderConfig.ApiKey)
	viper.SetDefault("ai_provider_config.api_version", DefaultConfig.AIProviderConfig.ApiVersion)
}

// bindEnv explicitly binds environment variables to configuration keys
func bindEnv() {
	_ = viper.BindEnv("theme", "THEME")
	_ = viper.BindEnv("ai_provider_config.provider", "PROVIDER")
	_ = viper.BindEnv("ai_provider_config.base_url", "BASE_URL")
	_ = viper.BindEnv("ai_provider_config.model", "MODEL")
	_ = viper.BindEnv("ai_provider_config.temperature", "TEMPERATURE")
	_ = viper.BindEnv("ai_provider_config.reasoning_effort", "REASONING_EFFORT")
	_ = viper.BindEnv("ai_provider_config.api_key", "API_KEY")
	_ = viper.BindEnv("ai_provider_config.api_version", "API_VERSION")
}

// bindFlags binds the CLI flags to configuration values.
func bindFlags(rootCmd *cobra.Command) {
	_ = viper.BindPFlag("theme", rootCmd.PersistentFlags().Lookup("theme"))
	_ = viper.BindPFlag("ai_provider_config.provider", rootCmd.PersistentFlags().Lookup("provider"))
	_ = viper.BindPFlag("ai_provider_config.base_url", rootCmd.PersistentFlags().Lookup("base_url"))
	_ = viper.BindPFlag("ai_provider_config.model", rootCmd.PersistentFlags().Lookup("model"))
	_ = viper.BindPFlag("ai_provider_config.temperature", rootCmd.PersistentFlags().Lookup("temperature"))
	_ = viper.BindPFlag("ai_provider_config.reasoning_effort", rootCmd.PersistentFlags().Lookup("reasoning_effort"))
	_ = viper.BindPFlag("ai_provider_config.api_key", rootCmd.PersistentFlags().Lookup("api_key"))
	_ = viper.BindPFlag("ai_provider_config.api_version", rootCmd.PersistentFlags().Lookup("api_version"))
}

// InitFlags initializes the flags for the root command.
func InitFlags(rootCmd *cobra.Command) {
	// Use PersistentFlags so that these flags are available in all subcommands
	rootCmd.PersistentFlags().StringVarP(&cfgFile, "config", "c", "", "Specifies the path to a configuration file (JSON or YAML) that contains all the settings for the application.")

	// Theme configuration
	rootCmd.PersistentFlags().String("theme", DefaultConfig.Theme, "Set customize theme for buffering response from ai. (e.g., 'dracula', 'light', 'dark')")

	// Version flag
	rootCmd.Flags().BoolP("version", "v", false, "Specifies the version of the application.")

	// AI Provider configuration
	rootCmd.PersistentFlags().String("provider", DefaultConfig.AIProviderConfig.Provider, "The name of the AI provider (e.g., 'openai', 'azure', 'anthropic')")
	rootCmd.PersistentFlags().String("base_url", DefaultConfig.AIProviderConfig.BaseURL, "The base URL of AI Provider (e.g., default is 'https://api.openai.com/v1').")
	rootCmd.PersistentFlags().String("model", DefaultConfig.AIProviderConfig.Model, "The name of the model used for chat completions, such as 'gpt-4o'.")
	rootCmd.PersistentFlags().Float32("temperature", 0, "Adjusts the AI model's creativity (0-1, default 0.2).")
	rootCmd.PersistentFlags().String("reasoning_effort", "", "Adjusts the AI Reasoning model's effort (e.g., 'low', 'medium', 'high').")
	rootCmd.PersistentFlags().String("api_key", DefaultConfig.AIProviderConfig.ApiKey, "The API key used to authenticate with the AI service provider.")
	rootCmd.PersistentFlags().String("api_version", DefaultConfig.AIProviderConfig.ApiVersion, "The API version used to authenticate with the chat AI service provider.")
}

// GetConfigFileType returns the type of the configuration file based on its extension
func GetConfigFileType(filename string) string {
	if strings.HasSuffix(filename, ".json") {
		return "json"
	} else if strings.HasSuffix(filename, ".yaml") || strings.HasSuffix(filename, ".yml") {
		return "yaml"
	}
	return ""
}
