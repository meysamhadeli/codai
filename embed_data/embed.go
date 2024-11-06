package embed_data

import _ "embed"

//go:embed prompts/code_block_prompt.tmpl
var CodeBlockTemplate []byte

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
