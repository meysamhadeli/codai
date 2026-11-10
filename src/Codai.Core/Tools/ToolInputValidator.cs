using System.Runtime.CompilerServices;
using System.Text.Json;
using Json.Schema;
using CompiledSchema = Json.Schema.JsonSchema;

namespace Codai.Core.Tools;

internal sealed class ToolInputValidator
{
    private readonly ConditionalWeakTable<ITool, CompiledSchema> _schemas = new();

    public static string? ValidateObject(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Object ? null : "Arguments must be a JSON object.";

    public string? Validate(ITool tool, JsonElement arguments)
    {
        var schema = _schemas.GetValue(tool, static entry => CompiledSchema.Build(entry.Schema, new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new SchemaRegistry
            {
                Fetch = static (_, _) => throw new InvalidOperationException(
                    "External schema references are not supported. Bundle referenced definitions into the tool schema."),
            },
        }));
        var results = schema.Evaluate(arguments, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (results.IsValid)
        {
            return null;
        }

        var errors = Errors(results).Take(5);
        return string.Join("; ", errors);
    }

    private static IEnumerable<string> Errors(EvaluationResults results)
    {
        if (results.IsValid)
        {
            yield break;
        }

        if (results.Errors is not null)
        {
            foreach (var error in results.Errors)
            {
                yield return $"{results.InstanceLocation}: {error.Value}";
            }
        }

        if (results.Details is not null)
        {
            foreach (var detail in results.Details)
            {
                foreach (var error in Errors(detail))
                {
                    yield return error;
                }
            }
        }
    }
}