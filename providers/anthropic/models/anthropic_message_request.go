package models

// AnthropicMessageRequest represents the request body for Anthropic message.
type AnthropicMessageRequest struct {
	Model       string    `json:"model"`                 // Model ID, e.g., "claude-3-5-sonnet-latest"
	Messages    []Message `json:"messages"`              // Array of message history
	Temperature *float32  `json:"temperature,omitempty"` // Sampling temperature (0.0-1.0)
	Stream      bool      `json:"stream,omitempty"`      // Enable/disable streaming
}

// Message Define the request body structure
type Message struct {
	Role    string `json:"role"`    // Valid roles: "system", "user", "assistant"
	Content string `json:"content"` // The text content for this message
}
