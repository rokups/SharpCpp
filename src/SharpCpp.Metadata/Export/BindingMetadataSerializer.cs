using System.Diagnostics.CodeAnalysis;
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

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Only the binding generator and the generator tests call this method. They are build tools that are never trimmed. The document types are plain public records.")]
    public static string Serialize(BindingMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        return json + "\n";
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Only the binding generator and the generator tests call this method. They are build tools that are never trimmed. The document types are plain public records.")]
    public static BindingMetadataDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<BindingMetadataDocument>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Metadata JSON deserialized to null.");
    }
}
