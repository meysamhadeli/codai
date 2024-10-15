{
    "definition.file_scoped_namespace_multiple": "(file_scoped_namespace_declaration name: (qualified_name) @name)",
    "definition.file_scoped_namespace_single": "(file_scoped_namespace_declaration name: (identifier) @name)",
    "definition.namespace_multiple": "(namespace_declaration name: (qualified_name) @name)",
    "definition.namespace_single": "(namespace_declaration name: (identifier) @name)",
    "definition.class": "(class_declaration name: (identifier) @name)",
    "reference.class": "(class_declaration (base_list (_) @name))",
    "definition.interface": "(interface_declaration name: (identifier) @name)",
    "reference.interface": "(interface_declaration (base_list (_) @name))",
    "definition.method": "(method_declaration name: (identifier) @name)",
    "definition.enum": "(enum_declaration name: (identifier) @enum.name)",
    "definition.struct": "(struct_declaration name: (identifier) @struct.name)",
    "definition.record": "(record_declaration name: (identifier) @record.name)",
    "definition.property": "(property_declaration name: (identifier) @property.name)"
}