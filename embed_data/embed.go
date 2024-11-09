package embed_data

import _ "embed"

//go:embed prompts/rag_context_prompt.tmpl
var RagContextPrompt []byte

//go:embed prompts/summarize_full_context_prompt.tmpl
var SummarizeFullContextPrompt []byte

//go:embed models_details/model_details.tmpl
var ModelDetails []byte

//go:embed tree-sitter/queries/csharp.scm
var CSharpQuery []byte

//go:embed tree-sitter/queries/go.scm
var GoQuery []byte

//go:embed tree-sitter/queries/python.scm
var PythonQuery []byte

//go:embed tree-sitter/queries/java.scm
var JavaQuery []byte

//go:embed tree-sitter/queries/javascript.scm
var JavascriptQuery []byte

//go:embed tree-sitter/queries/typescript.scm
var TypescriptQuery []byte
