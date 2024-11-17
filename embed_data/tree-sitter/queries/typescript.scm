{
     "class" : "(class_declaration name: ((type_identifier) @name)) @definition.class",
     "interface": "(interface_declaration name: ((type_identifier) @name)) @definition.interface",
     "enum": "(enum_declaration name: ((identifier) @name)) @definition.enum",
     "method": "(method_definition name: ((property_identifier) @name)) @definition.method",
     "function": "(function_declaration name: ((identifier) @name)) @definition.function",
     "anonymous_function": "(lexical_declaration(variable_declarator name: (identifier) @name)) @definition.anonymous_function"
}