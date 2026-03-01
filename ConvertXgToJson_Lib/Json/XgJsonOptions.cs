using System.Text.Json;
using System.Text.Json.Serialization;
using ConvertXgToJson_Lib.Models;
using System.Linq;
namespace ConvertXgToJson_Lib.Json;

/// <summary>
/// Provides pre-configured JsonSerializerOptions for XG model serialization.
/// </summary>
public static class XgJsonOptions
{
    private static readonly JsonSerializerOptions _options = BuildOptions();

    public static JsonSerializerOptions Default => _options;

    private static JsonSerializerOptions BuildOptions()
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters =
    {
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        new PositionEngineConverter(),
        new SaveRecordConverter(),
    }
        }; return opts;
    }
}

/// <summary>
/// Serializes PositionEngine as a compact JSON array of 26 signed integers
/// rather than a nested object, which is much more readable for backgammon data.
/// e.g. [0, 2, 0, 0, 0, -5, ...]
/// </summary>
internal sealed class PositionEngineConverter : JsonConverter<PositionEngine>
{
    public override PositionEngine Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var points = JsonSerializer.Deserialize<sbyte[]>(ref reader, options) ?? new sbyte[26];
        return new PositionEngine { Points = points };
    }

    public override void Write(Utf8JsonWriter writer, PositionEngine value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (sbyte p in value.Points)
            writer.WriteNumberValue(p);
        writer.WriteEndArray();
    }
}

/// <summary>
/// Polymorphic converter for SaveRecord: serialises a "$type" discriminator
/// so that the JSON consumer can identify each record variant.
/// </summary>
internal sealed class SaveRecordConverter : JsonConverter<SaveRecord>
{
    public override bool CanConvert(Type typeToConvert)
        => typeof(SaveRecord).IsAssignableFrom(typeToConvert);

    public override SaveRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Buffer the entire object so we can read $type then redeserialize
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("$type", out var typeProp))
            throw new JsonException("SaveRecord missing '$type' discriminator.");

        string typeName = typeProp.GetString() ?? "";

        // Build inner options without this converter to avoid recursion
        var innerOptions = new JsonSerializerOptions(options);
        innerOptions.Converters.Remove(
            innerOptions.Converters.First(c => c is SaveRecordConverter));

        string json = root.GetRawText();

        return typeName switch
        {
            "HeaderMatch" => JsonSerializer.Deserialize<MatchHeaderRecord>(json, innerOptions)!,
            "HeaderGame" => JsonSerializer.Deserialize<GameHeaderRecord>(json, innerOptions)!,
            "Move" => JsonSerializer.Deserialize<MoveRecord>(json, innerOptions)!,
            "Cube" => JsonSerializer.Deserialize<CubeRecord>(json, innerOptions)!,
            "FooterGame" => JsonSerializer.Deserialize<GameFooterRecord>(json, innerOptions)!,
            "FooterMatch" => JsonSerializer.Deserialize<MatchFooterRecord>(json, innerOptions)!,
            _ => throw new JsonException($"Unknown SaveRecord type: '{typeName}'")
        };
    }
    public override void Write(Utf8JsonWriter writer, SaveRecord value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // Write discriminator first
        writer.WriteString("$type", value.EntryType.ToString());

        // Serialize the concrete type using options WITHOUT this converter
        // to avoid infinite recursion.
        var innerOptions = new JsonSerializerOptions(options);
        innerOptions.Converters.Remove(
            innerOptions.Converters.First(c => c is SaveRecordConverter));

        using var doc = JsonSerializer.SerializeToDocument(value, value.GetType(), innerOptions);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name == "$type") continue;
            prop.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
