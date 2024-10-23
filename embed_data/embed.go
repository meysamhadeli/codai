package embed_data

import _ "embed"

//go:embed prompts/code_block_prompt.tmpl
var CodeBlockTemplate []byte

//go:embed tree-sitter/queries/csharp.scm
var CSharpQuery []byte

//go:embed tree-sitter/queries/go.scm
var GolangQuery []byte
