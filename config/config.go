package config

import (
	"github.com/spf13/viper"
	"log"
)

type Config struct {
	Host    string
	Port    int
	Timeout int
	Version string
}

func LoadConfiguration(cliConfig *Config) *Config {
	// Set the name of the configuration file (without the extension)
	viper.SetConfigName("config")
	// Set the path to look for the configuration file
	viper.AddConfigPath("./config") // Update this line to point to the config directory
	// Enable reading from environment variables
	viper.AutomaticEnv()

	// Read the configuration file
	if err := viper.ReadInConfig(); err != nil {
		log.Fatalf("Error reading config file: %v", err)
	}

	// Load the config from the file
	fileConfig := &Config{
		Host:    viper.GetString("host"),
		Port:    viper.GetInt("port"),
		Timeout: viper.GetInt("timeout"),
		Version: viper.GetString("version"),
	}

	// Merge CLI and file configurations, and validate the result
	return updateConfigs(fileConfig, cliConfig)
}

// mergeAndValidateConfig merges CLI config into fileConfig and validates the resulting configuration
func updateConfigs(fileConfig, cliConfig *Config) *Config {
	// Merge CLI values into fileConfig if provided
	if cliConfig.Host != "" {
		fileConfig.Host = cliConfig.Host
	}
	if cliConfig.Port != 0 {
		fileConfig.Port = cliConfig.Port
	}
	if cliConfig.Timeout != 0 {
		fileConfig.Timeout = cliConfig.Timeout
	}
	if cliConfig.Version != "" {
		fileConfig.Version = cliConfig.Version
	}

	// Validate the final configuration
	if fileConfig.Host == "" {
		log.Fatal("Fatal error: Host is required but not provided.")
	}
	if fileConfig.Port == 0 {
		log.Fatal("Fatal error: Port is required but not provided.")
	}
	if fileConfig.Timeout == 0 {
		log.Fatal("Fatal error: Timeout is required but not provided.")
	}
	if fileConfig.Version == "" {
		log.Fatal("Fatal error: Version is required but not provided.")
	}

	return fileConfig
}
