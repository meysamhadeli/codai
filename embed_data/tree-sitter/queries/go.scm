{
    "package": "(package_clause (package_identifier) @name) @definition.package",
	"function": "(function_declaration name: (identifier) @name) @definition.function",
	"method": "(method_declaration name: (field_identifier) @name) @definition.method",
    "interface": "(type_declaration (type_spec name: (type_identifier) @name type: (interface_type))) @definition.interface",
    "struct": "(type_declaration (type_spec name: name: (type_identifier) @name type: (struct_type))) @definition.struct"
}

