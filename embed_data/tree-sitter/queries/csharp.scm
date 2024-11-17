{
    "file_scoped_namespace": "(file_scoped_namespace_declaration name: (identifier)? @name.file_scoped_namespace name: (qualified_name)? @name) @definition.file_scoped_namespace",
    "namespace": "(namespace_declaration name: (qualified_name)? @name.namespace  name: (identifier)? @name) @definition.namespace",
    "class": "(class_declaration name: (identifier) @name) @definition.class",
    "interface": "(interface_declaration name: (identifier) @name) @definition.interface",
    "method": "(method_declaration name: (identifier) @name) @definition.method",
    "enum": "(enum_declaration name: (identifier) @name) @definition.enum",
    "struct": "(struct_declaration name: (identifier) @name) @definition.struct",
    "record": "(record_declaration name: (identifier) @name) @definition.record",
    "property": "(property_declaration name: (identifier) @name) @definition.property"
}