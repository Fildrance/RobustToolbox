using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Robust.MetadataGenerator.Models;

public class MetadataCollection
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("components")]
    public List<TypeMetadata> Components { get; set; } = new();

    [JsonPropertyName("commands")]
    public List<TypeMetadata> Commands { get; set; } = new();
}
