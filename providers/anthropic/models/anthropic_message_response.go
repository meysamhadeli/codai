package models

// AnthropicMessageResponse represents the full response structure for Anthropic's chat completion API (streaming).
type AnthropicMessageResponse struct {
	Type    string   `json:"type"`              // Type of the response chunk, e.g., "message_start", "content_block_delta", etc.
	Choices []Choice `json:"choices,omitempty"` // Array of choices for response content
	Usage   *Usage   `json:"usage,omitempty"`   // Optional token usage details (appears in certain chunks)
	Delta   *Delta   `json:"delta,omitempty"`   // Optional content updates or deltas
}

// Choice represents an individual choice in the response.
type Choice struct {
	Delta Delta `json:"delta"` // Streamed content delta
}

// Delta represents the streamed content or updates.
type Delta struct {
	Type       string `json:"type,omitempty"`        // Type of delta, e.g., "text_delta"
	Text       string `json:"text,omitempty"`        // Text content streamed in chunks
	StopReason string `json:"stop_reason,omitempty"` // Reason for stopping (e.g., "end_turn")
}

// Usage represents token usage details for Anthropic responses.
type Usage struct {
	InputTokens  int `json:"input_tokens"`  // Number of tokens in the input
	OutputTokens int `json:"output_tokens"` // Number of tokens in the output
	TotalTokens  int `json:"total_tokens"`  // Total tokens used
}

// AnthropicError represents the error response structure from Anthropic's API.
type AnthropicError struct {
	Type  string `json:"type"`  // Error type, e.g., "error"
	Error Error  `json:"error"` // Error details
}

// Error represents detailed information about the error.
type Error struct {
	Type    string `json:"type"`    // Error category, e.g., "invalid_request_error"
	Message string `json:"message"` // Human-readable error message
}
