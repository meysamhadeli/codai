package openai

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/providers/models"
	"github.com/meysamhadeli/codai/utils"
	"io"
	"io/ioutil"
	"net/http"
	"strings"
)

// OpenAIConfig implements the Provider interface for OpenAPI.
type OpenAIConfig struct {
	EmbeddingURL        string
	ChatCompletionURL   string
	EmbeddingModel      string
	ChatCompletionModel string
	Temperature         float32
	EncodingFormat      string
	ApiKey              string
	MaxTokens           int
	Threshold           float64
	BufferingTheme      string
}

// NewOpenAIProvider initializes a new OpenAPIProvider.
func NewOpenAIProvider(config *OpenAIConfig) contracts.IAIProvider {
	return &OpenAIConfig{
		EmbeddingURL:        config.EmbeddingURL,
		ChatCompletionURL:   config.ChatCompletionURL,
		EmbeddingModel:      config.EmbeddingModel,
		ChatCompletionModel: config.ChatCompletionModel,
		Temperature:         config.Temperature,
		EncodingFormat:      config.EncodingFormat,
		MaxTokens:           config.MaxTokens,
		Threshold:           config.Threshold,
		ApiKey:              config.ApiKey,
		BufferingTheme:      config.BufferingTheme,
	}
}

func (openAIProvider *OpenAIConfig) EmbeddingRequest(ctx context.Context, prompt string) (*models.EmbeddingResponse, error) {
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

func (openAIProvider *OpenAIConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string, history string) (string, error) {

	finalPrompt := fmt.Sprintf("%s\n\n%s\n\n", history, prompt)

	// Prepare the request body
	reqBody := models.ChatCompletionRequest{
		Model: openAIProvider.ChatCompletionModel,
		Messages: []models.Message{
			{
				Role:    "system",
				Content: finalPrompt,
			},
			{
				Role:    "user",
				Content: userInput,
			},
		},
		Stream:      true, // Enable streaming
		Temperature: &openAIProvider.Temperature,
	}

	jsonData, err := json.Marshal(reqBody)
	if err != nil {
		return "", fmt.Errorf("error marshalling request body: %v", err)
	}

	// Create a new HTTP request
	req, err := http.NewRequestWithContext(ctx, "POST", openAIProvider.ChatCompletionURL, bytes.NewBuffer(jsonData))
	if err != nil {
		return "", fmt.Errorf("error creating request: %v", err)
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
			return "", fmt.Errorf("request canceled: %v", err)
		}
		return "", fmt.Errorf("error sending request: %v", err)
	}
	defer resp.Body.Close()

	var buffer strings.Builder
	inCodeBlock := false

	// Create a buffered reader for reading the response stream
	reader := bufio.NewReader(resp.Body)

	var resultBuilder strings.Builder

	for {
		// Read the response line by line
		line, err := reader.ReadString('\n')
		if err != nil {
			if err == io.EOF {
				break
			}
			return "", fmt.Errorf("error reading stream: %v", err)
		}

		// Check for the end of the stream
		if line == "data: [DONE]\n" {
			break // Stop processing if we hit the end signal
		}

		if strings.HasPrefix(line, "data: ") {
			// Trim the "data: " prefix to get the actual JSON part
			jsonPart := strings.TrimPrefix(line, "data: ")

			// Parse the JSON to extract the response structure
			var response models.ChatCompletionResponse
			if err := json.Unmarshal([]byte(jsonPart), &response); err != nil {
				return "", fmt.Errorf("error unmarshalling chunk: %v", err)
			}

			// Safely extract the content from the response
			if len(response.Choices) > 0 {
				content := response.Choices[0].Delta.Content
				resultBuilder.WriteString(content)
				utils.RenderAndPrintMarkdown(content, &inCodeBlock, &buffer, openAIProvider.BufferingTheme)
			}
		}
	}

	fmt.Println()

	// Return the final result or suggestions
	return resultBuilder.String(), nil
}
