package models

// ChatCompletionResponse represents the entire response structure from OpenAI's chat completion API.
type ChatCompletionResponse struct {
	Choices []Choice `json:"choices"`
}

// Choice represents an individual choice in the response.
type Choice struct {
	Delta Delta `json:"delta"`
}

// Delta represents the delta object in each choice containing the content.
type Delta struct {
	Content string `json:"content"`
}

type StreamResponse struct {
	Content string // Holds content chunks
	Err     error  // Holds error details
	Done    bool   // Signals end of stream
}
