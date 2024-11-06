package models

// OpenAIChatCompletionRequest Define the request body structure
type OpenAIChatCompletionRequest struct {
	Model         string        `json:"model"`
	Messages      []Message     `json:"messages"`
	Temperature   *float32      `json:"temperature,omitempty"` // Optional field (pointer to float32)
	Stream        bool          `json:"stream"`
	StreamOptions StreamOptions `json:"stream_options"`
}

// Message Define the request body structure
type Message struct {
	Role    string `json:"role"`
	Content string `json:"content"`
}

// StreamOptions includes configurations for streaming behavior
type StreamOptions struct {
	IncludeUsage bool `json:"include_usage"` // Requests token usage data in the response
}
