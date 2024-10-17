package openai

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

// OpenAPIConfig implements the Provider interface for OpenAPI.
type OpenAPIConfig struct {
	EmbeddingURL        string
	ChatCompletionURL   string
	EmbeddingModel      string
	ChatCompletionModel string
	Stream              bool
	MaxCompletionTokens int
	Temperature         float32
	EncodingFormat      string
	ApiKey              string
}

// NewOpenAPIProvider initializes a new OpenAPIProvider.
func NewOpenAPIProvider(config *OpenAPIConfig) contracts.IAIProvider {
	return &OpenAPIConfig{
		EmbeddingURL:        config.EmbeddingURL,
		ChatCompletionURL:   config.ChatCompletionURL,
		EmbeddingModel:      config.EmbeddingModel,
		ChatCompletionModel: config.ChatCompletionModel,
		Stream:              config.Stream,
		MaxCompletionTokens: config.MaxCompletionTokens,
		Temperature:         config.Temperature,
		EncodingFormat:      config.EncodingFormat,
		ApiKey:              config.ApiKey,
	}
}

func (openAIProvider *OpenAPIConfig) EmbeddingRequest(ctx context.Context, prompt string) (*models.EmbeddingResponse, error) {
	// Create the request payload
	requestBody := models.EmbeddingRequest{
		Input:          prompt,
		Model:          openAIProvider.EmbeddingModel,
		EncodingFormat: openAIProvider.EncodingFormat,
	}

	// Convert the request payload to JSON
	jsonData, err := json.Marshal(requestBody)
	if err != nil {
		return nil, fmt.Errorf("error encoding JSON: %v", err)
	}

	// Create a new HTTP POST request
	req, err := http.NewRequestWithContext(ctx, "POST", openAIProvider.EmbeddingURL, bytes.NewBuffer(jsonData))
	if err != nil {
		return nil, fmt.Errorf("error creating request: %v", err)
	}

	// Set required headers
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("api-key", openAIProvider.ApiKey)

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

func (openAIProvider *OpenAPIConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) (*models.ChatCompletionResponse, error) {

	// Prepare the request body
	reqBody := models.ChatCompletionRequest{
		Model: openAIProvider.ChatCompletionModel,
		Messages: []models.Message{
			{Role: "system", Content: prompt},
			{Role: "user", Content: userInput},
		},
		StreamOptions: models.StreamOptions{
			Stream: openAIProvider.Stream,
		},
		MaxCompletionTokens: openAIProvider.MaxCompletionTokens,
		Temperature:         openAIProvider.Temperature,
	}

	jsonData, err := json.Marshal(reqBody)
	if err != nil {
		return nil, err
	}

	// Create a new HTTP request
	req, err := http.NewRequestWithContext(ctx, "POST", openAIProvider.ChatCompletionURL, bytes.NewBuffer(jsonData))
	if err != nil {
		return nil, fmt.Errorf("error creating request: %v", err)
	}

	// Set headers
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("api-key", openAIProvider.ApiKey)

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
