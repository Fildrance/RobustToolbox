using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Robust.MetadataGenerator.Models;

public class TypeMetadata
{
    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = "";
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "";
    [JsonPropertyName("assemblyName")]
    public string AssemblyName { get; set; } = "";

    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; set; }

    [JsonPropertyName("seeAlso")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? SeeAlso { get; set; }

    [JsonPropertyName("accessReferences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AccessReference>? AccessReferences { get; set; }

    [JsonPropertyName("relatedSystems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? RelatedSystems { get; set; }

    [JsonPropertyName("fields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FieldMetadata> Fields { get; set; } = new();

    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PropertyMetadata> Properties { get; set; } = new();

    [JsonPropertyName("nestedTypes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TypeMetadata> NestedTypes { get; set; } = new();
}
