package models

type StreamResponse struct {
	Content string // Holds content chunks
	Err     error  // Holds error details
	Done    bool   // Signals end of stream
}

type Error struct {
	Message string `json:"message"`
	Code    int    `json:"code"`
}

// AIError represents an error response from OpenAI.
type AIError struct {
	Error Error `json:"error"`
}
