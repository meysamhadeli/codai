package openai

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss_color"
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
	TokenManagement     contracts.ITokenManagement
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
		TokenManagement:     config.TokenManagement,
	}
}

func (openAIProvider *OpenAIConfig) EmbeddingRequest(ctx context.Context, prompt string) (*models.EmbeddingResponse, error) {

	// Count tokens for the user input and prompt
	totalChatTokens, err := openAIProvider.TokenManagement.CountTokens(prompt, openAIProvider.ChatCompletionModel)
	if err != nil {
		return nil, fmt.Errorf(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
	}

	// Check if enough tokens are available
	if err := openAIProvider.TokenManagement.UseEmbeddingTokens(totalChatTokens); err != nil {
		return nil, fmt.Errorf(lipgloss_color.Red.Render(fmt.Sprintf("Error: %v", err)))
	}

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

func (openAIProvider *OpenAIConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) (string, error) {
	// Count tokens for the user input and prompt
	totalChatTokens, err := openAIProvider.TokenManagement.CountTokens(fmt.Sprintf("%s%s", prompt, userInput), openAIProvider.ChatCompletionModel)
	if err != nil {
		return "", fmt.Errorf("Error: %v", err)
	}

	// Check if enough tokens are available
	if err := openAIProvider.TokenManagement.UseTokens(totalChatTokens); err != nil {
		return "", fmt.Errorf("Error: %v", err)
	}

	// Prepare the request body
	reqBody := models.ChatCompletionRequest{
		Model: openAIProvider.ChatCompletionModel,
		Messages: []models.Message{
			{Role: "system", Content: prompt},
			{Role: "user", Content: userInput},
		},
		Stream:      true,
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

	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("api-key", openAIProvider.ApiKey)

	client := &http.Client{}
	resp, err := client.Do(req)
	if err != nil {
		if errors.Is(ctx.Err(), context.Canceled) {
			return "", fmt.Errorf("request canceled: %v", err)
		}
		return "", fmt.Errorf("error sending request: %v", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("API request failed with status: %d", resp.StatusCode)
	}

	reader := bufio.NewReader(resp.Body)
	var resultBuilder strings.Builder
	var markdownBuffer strings.Builder // Buffer for accumulating chunks

	// Process each chunk as it arrives
	for {
		line, err := reader.ReadString('\n')
		if err != nil {
			if err == io.EOF {
				break
			}
			return "", fmt.Errorf("error reading stream: %v", err)
		}

		if line == "data: [DONE]\n" {
			break
		}

		if strings.HasPrefix(line, "data: ") {
			// Trim the "data: " prefix and parse JSON
			jsonPart := strings.TrimPrefix(line, "data: ")
			var response models.ChatCompletionResponse
			if err := json.Unmarshal([]byte(jsonPart), &response); err != nil {
				return "", fmt.Errorf("error unmarshalling chunk: %v", err)
			}

			if len(response.Choices) > 0 {
				content := response.Choices[0].Delta.Content
				resultBuilder.WriteString(content) // Gather full result for final return

				// Accumulate content in the markdown buffer
				markdownBuffer.WriteString(content)

				// Process complete Markdown blocks (indicated by double newlines)
				if strings.Contains(content, "\n") {
					blockContent := markdownBuffer.String()
					markdownBuffer.Reset()

					language := utils.DetectLanguageFromCodeBlock(blockContent)
					err := utils.RenderAndPrintMarkdown(blockContent, language, openAIProvider.BufferingTheme)
					if err != nil {
						return "", err
					}
				}
			}
		}
	}

	// Flush remaining content in the buffer
	if markdownBuffer.Len() > 0 {
		blockContent := markdownBuffer.String()
		language := utils.DetectLanguageFromCodeBlock(blockContent)
		err := utils.RenderAndPrintMarkdown(blockContent, language, openAIProvider.BufferingTheme)
		if err != nil {
			return "", err
		}
	}

	return resultBuilder.String(), nil
}
