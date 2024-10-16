package config

import (
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss_color"
	"github.com/spf13/cobra"
	"github.com/spf13/viper"
	"os"
)

// Config represents the structure of the configuration file
type Config struct {
	Host    string
	Port    int
	Timeout int
	Version string
}

// Default configuration values
var defaultConfig = Config{
	Host:    "localhost",
	Port:    8080,
	Timeout: 30,
	Version: "1.0",
}

// cfgFile holds the path to the configuration file (set via CLI)
var cfgFile string

// LoadConfigs initializes the configuration from file and flags, and returns the final config.
// It uses default values, config file values, and CLI flags in priority order.
func LoadConfigs(cmd *cobra.Command) *Config {
	var config *Config

	initFlags(cmd)

	// Set default values using Viper
	viper.SetDefault("Host", defaultConfig.Host)
	viper.SetDefault("Port", defaultConfig.Port)
	viper.SetDefault("Timeout", defaultConfig.Timeout)
	viper.SetDefault("Version", defaultConfig.Version)

	// If a config file is specified, load it
	if cfgFile != "" {
		viper.SetConfigFile(cfgFile)
		if err := viper.ReadInConfig(); err != nil {
			fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Error reading config file: %v", err)))
			os.Exit(1)
		}
	}

	// Bind CLI flags to config values, allowing them to override file or default values
	bindFlags(cmd)

	// Unmarshal the final configuration into the Config struct
	if err := viper.Unmarshal(&config); err != nil {
		fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Unable to decode into struct: %v", err)))
		os.Exit(1)
	}

	// Validate the config (we have defaults now, so it won't crash unless explicitly required fields are missing)
	validateConfig(config)

	return config
}

// bindFlags binds the CLI flags to configuration values
func bindFlags(cmd *cobra.Command) {
	_ = viper.BindPFlag("Host", cmd.Flags().Lookup("host"))
	_ = viper.BindPFlag("Port", cmd.Flags().Lookup("port"))
	_ = viper.BindPFlag("Timeout", cmd.Flags().Lookup("timeout"))
	_ = viper.BindPFlag("Version", cmd.Flags().Lookup("version"))
}

// validateConfig checks if all required configuration fields are set
func validateConfig(config *Config) {
	if config.Host == "" || config.Port == 0 || config.Timeout == 0 || config.Version == "" {
		fmt.Println(lipgloss_color.Red.Render("Error: Missing required configuration values."))
		os.Exit(1)
	}
}

// initFlags initializes the flags that can be used to override the config
func initFlags(cmd *cobra.Command) {
	// Define flags with default values (defaults will be used if neither config nor CLI flags provide values)
	cmd.PersistentFlags().StringVar(&cfgFile, "config", "", "config file (default is ./config.yml)")
	cmd.Flags().StringP("host", "H", "", "Server host (default is 'localhost')")
	cmd.Flags().IntP("port", "P", 0, "Server port (default is 8080)")
	cmd.Flags().IntP("timeout", "T", 0, "Timeout in seconds (default is 30)")
	cmd.Flags().StringP("version", "V", "", "Version of the service (default is '1.0')")
}
