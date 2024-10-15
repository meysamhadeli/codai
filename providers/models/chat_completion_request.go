package models

// ChatCompletionRequest Define the request body structure
type ChatCompletionRequest struct {
	Model               string        `json:"model"`
	Messages            []Message     `json:"messages"`
	Temperature         float32       `json:"temperature"`
	MaxCompletionTokens int           `json:"maxCompletionTokens"`
	StreamOptions       StreamOptions `json:"streamOptions"`
}

// StreamOptions Define the request body structure
type StreamOptions struct {
	Stream bool `json:"stream"`
}
