using System.Text.Json.Serialization;

namespace Codai.Core.Prompting;

/// <summary>Source-generated JSON used by <see cref="RequestedContextParser"/>.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(string[]))]
internal sealed partial class PromptingJsonContext : JsonSerializerContext;
