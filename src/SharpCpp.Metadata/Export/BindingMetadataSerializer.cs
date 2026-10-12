using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpCpp.Metadata.Export;

public static class BindingMetadataSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        // Documents are hashed and compared, so every platform writes the same line endings.
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(BindingMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        return json + "\n";
    }

    public static BindingMetadataDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<BindingMetadataDocument>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Metadata JSON deserialized to null.");
    }
}
