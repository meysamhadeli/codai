package models

// FileData holds the path and content of a file
type FileData struct {
	RelativePath   string
	Code           string
	TreeSitterCode string
}

type FullContextData struct {
	FileData []FileData
	RawCodes []string
}
