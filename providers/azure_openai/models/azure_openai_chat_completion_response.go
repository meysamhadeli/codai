package models

// OpenAIChatCompletionResponse represents the entire response structure from OpenAI's chat completion API.
type OpenAIChatCompletionResponse struct {
	Choices []Choice `json:"choices"` // Array of choice completions
	Usage   Usage    `json:"usage"`   // Token usage details
}

// Choice represents an individual choice in the response.
type Choice struct {
	Delta Delta `json:"delta"`
}

// Delta represents the delta object in each choice containing the content.
type Delta struct {
	Content string `json:"content"`
}

// Usage defines the token usage information for the response.
type Usage struct {
	PromptTokens     int `json:"prompt_tokens"`     // Number of tokens in the prompt
	CompletionTokens int `json:"completion_tokens"` // Number of tokens in the completion
	TotalTokens      int `json:"total_tokens"`      // Total tokens used
}
