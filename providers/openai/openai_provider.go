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
	"os"
)

// OpenAPIProvider implements the Provider interface for OpenAPI.
type OpenAPIProvider struct {
	embeddingURL        string
	chatURL             string
	embeddingModel      string
	chatModel           string
	stream              bool
	maxCompletionTokens int
	temperature         float32
	encodingFormat      string
	apiKey              string
}

// NewOpenAPIProvider initializes a new OpenAPIProvider.
func NewOpenAPIProvider() contracts.IAIProvider {
	return &OpenAPIProvider{
		embeddingURL:        "https://api.openai.com/v1/embeddings",
		chatURL:             "https://api.openai.com/v1/chat/completions",
		maxCompletionTokens: 4096,
		chatModel:           "gpt-4o",
		embeddingModel:      "text-embedding-ada-002",
		stream:              false,
		encodingFormat:      "float",
	}
}

func (openAIProvider *OpenAPIProvider) EmbeddingRequest(ctx context.Context, prompt string) (*models.EmbeddingResponse, error) {
	// Create the request payload
	requestBody := models.EmbeddingRequest{
		Input:          prompt,
		Model:          openAIProvider.embeddingModel,
		EncodingFormat: openAIProvider.encodingFormat,
	}

	// Convert the request payload to JSON
	jsonData, err := json.Marshal(requestBody)
	if err != nil {
		return nil, fmt.Errorf("error encoding JSON: %v", err)
	}

	// Create a new HTTP POST request
	req, err := http.NewRequestWithContext(ctx, "POST", openAIProvider.embeddingURL, bytes.NewBuffer(jsonData))
	if err != nil {
		return nil, fmt.Errorf("error creating request: %v", err)
	}

	apiKey := os.Getenv("OPENAI_API_KEY")

	// Set required headers
	req.Header.Set("Authorization", "Bearer "+apiKey)
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

func (openAIProvider *OpenAPIProvider) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) (*models.ChatCompletionResponse, error) {

	// Prepare the request body
	reqBody := models.ChatCompletionRequest{
		Model: openAIProvider.chatModel,
		Messages: []models.Message{
			{Role: "system", Content: prompt},
			{Role: "user", Content: userInput},
		},
		StreamOptions: models.StreamOptions{
			Stream: openAIProvider.stream,
		},
		MaxCompletionTokens: openAIProvider.maxCompletionTokens,
		Temperature:         openAIProvider.temperature,
	}

	jsonData, err := json.Marshal(reqBody)
	if err != nil {
		return nil, err
	}

	// Create a new HTTP request
	req, err := http.NewRequestWithContext(ctx, "POST", openAIProvider.chatURL, bytes.NewBuffer(jsonData))
	if err != nil {
		return nil, fmt.Errorf("error creating request: %v", err)
	}

	// Set headers
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", fmt.Sprintf("Bearer %s", openAIProvider.apiKey))

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
