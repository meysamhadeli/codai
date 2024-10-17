package ollama

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/providers/models"
	"io/ioutil"
	"net/http"
)

// OllamaConfig implements the Provider interface for Ollama.
type OllamaConfig struct {
	EmbeddingURL        string
	ChatCompletionURL   string
	MaxCompletionTokens int
	EmbeddingModel      string
	ChatCompletionModel string
	Stream              bool
	Temperature         float32
	EncodingFormat      string
}

// NewOllamaProvider initializes a new OllamaProvider.
func NewOllamaProvider(config *OllamaConfig) contracts.IAIProvider {
	return &OllamaConfig{
		EmbeddingURL:        config.EmbeddingURL,
		ChatCompletionURL:   config.ChatCompletionURL,
		MaxCompletionTokens: config.MaxCompletionTokens,
		ChatCompletionModel: config.ChatCompletionModel,
		EmbeddingModel:      config.EmbeddingModel,
		Stream:              config.Stream,
		EncodingFormat:      config.EncodingFormat,
		Temperature:         config.Temperature,
	}
}

func (ollamaProvider *OllamaConfig) EmbeddingRequest(ctx context.Context, prompt string) (*models.EmbeddingResponse, error) {

	// Create the request payload
	requestBody := models.EmbeddingRequest{
		Input:          prompt,
		Model:          ollamaProvider.EmbeddingModel,
		EncodingFormat: ollamaProvider.EncodingFormat,
	}

	// Convert the request payload to JSON
	jsonData, err := json.Marshal(requestBody)
	if err != nil {
		return nil, fmt.Errorf("error encoding JSON: %v", err)
	}

	// Create a new HTTP POST request
	req, err := http.NewRequestWithContext(ctx, "POST", ollamaProvider.EmbeddingURL, bytes.NewBuffer(jsonData))
	if err != nil {
		return nil, fmt.Errorf("error creating request: %v", err)
	}

	// Set required headers
	req.Header.Set("Content-Type", "application/json")

	// Make the HTTP request
	client := &http.Client{}
	resp, err := client.Do(req)
	if err != nil {
		// Check if the context was canceled
		if errors.Is(ctx.Err(), context.Canceled) {
			return nil, fmt.Errorf("request canceled: %v", err)
		}
		return nil, fmt.Errorf("error sending request: %v", err)
	}
	defer resp.Body.Close()

	// Read the response body
	body, err := ioutil.ReadAll(resp.Body)
	if err != nil {
		return nil, fmt.Errorf("error reading response: %v", err)
	}

	// Check for non-200 status codes
	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("API request failed with status %d: %s", resp.StatusCode, string(body))
	}

	// Unmarshal the response JSON into the struct
	var embeddingResponse models.EmbeddingResponse
	err = json.Unmarshal(body, &embeddingResponse)
	if err != nil {
		return nil, fmt.Errorf("error decoding JSON response: %v", err)
	}

	// Return the parsed response
	return &embeddingResponse, nil
}

func (ollamaProvider *OllamaConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) (*models.ChatCompletionResponse, error) {

	// Prepare the request body
	reqBody := models.ChatCompletionRequest{
		Model: ollamaProvider.ChatCompletionModel,
		Messages: []models.Message{
			{Role: "system", Content: prompt},
			{Role: "user", Content: userInput},
		},
		StreamOptions: models.StreamOptions{
			Stream: ollamaProvider.Stream,
		},
		MaxCompletionTokens: ollamaProvider.MaxCompletionTokens,
		Temperature:         ollamaProvider.Temperature,
	}

	jsonData, err := json.Marshal(reqBody)
	if err != nil {
		return nil, err
	}

	// Create a new HTTP request
	req, err := http.NewRequestWithContext(ctx, "POST", ollamaProvider.ChatCompletionURL, bytes.NewBuffer(jsonData))
	if err != nil {
		return nil, fmt.Errorf("error creating request: %v", err)
	}

	// Set headers
	req.Header.Set("Content-Type", "application/json")

	// Create an HTTP client and send the request
	client := &http.Client{}
	resp, err := client.Do(req)
	if err != nil {
		// Check if the context was canceled
		if errors.Is(ctx.Err(), context.Canceled) {
			return nil, fmt.Errorf("request canceled: %v", err)
		}
		return nil, fmt.Errorf("error sending request: %v", err)
	}
	defer resp.Body.Close()

	// Read the response body
	body, err := ioutil.ReadAll(resp.Body)
	if err != nil {
		return nil, fmt.Errorf("error reading response body: %v", err)
	}

	// Unmarshal the response into the response struct
	var chatResponse models.ChatCompletionResponse
	err = json.Unmarshal(body, &chatResponse)
	if err != nil {
		return nil, fmt.Errorf("error unmarshalling response: %v", err)
	}

	return &chatResponse, nil
}
