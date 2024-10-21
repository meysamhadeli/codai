package embed_data

import _ "embed"

//go:embed prompts/code_result.tmpl
var CodeResultTemplate []byte

//go:embed tree-sitter/queries/csharp.scm
var CSharpQuery []byte

//go:embed tree-sitter/queries/go.scm
var GolangQuery []byte
