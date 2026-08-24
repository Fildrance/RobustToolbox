using System.Text.Json.Serialization;

namespace Robust.MetadataGenerator.Models;

public class AccessReference
{
    [JsonPropertyName("targetType")]
    public string TargetType { get; set; } = "";

    [JsonPropertyName("access")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Access { get; set; }
}
