package models

// GeminiChatCompletionResponse represents the response from Gemini API
type GeminiChatCompletionResponse struct {
	Candidates    []Candidate    `json:"candidates"`
	UsageMetadata *UsageMetadata `json:"usageMetadata"`
}

type Candidate struct {
	Content Content `json:"content"`
}

type UsageMetadata struct {
	PromptTokenCount     int `json:"promptTokenCount"`
	CandidatesTokenCount int `json:"candidatesTokenCount"`
	TotalTokens          int `json:"totalTokens"`
}
