package models

// Suggestion Define struct for the entire suggestion
type Suggestion struct {
	CodeChanges  CodeChanges `xml:"codeChanges"`
	Explanations string      `xml:"EXPLANATIONS"`
}

// CodeChanges wraps multiple code changes.
type CodeChanges struct {
	CodeChange []CodeChange `xml:"codeChange"`
}

// CodeChange represents an individual code change with a file path and the code content.
type CodeChange struct {
	RelativePath string `xml:"relativePath"`
	Code         string `xml:"code"`
}
