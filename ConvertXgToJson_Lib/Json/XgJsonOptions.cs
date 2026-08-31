using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using System.Linq;
namespace ConvertXgToJson_Lib.Json;

/// <summary>
/// Provides pre-configured JsonSerializerOptions for XG model serialization.
/// </summary>
internal static class XgJsonOptions
{
    /// <summary>
    /// The source-generated metadata for everything this library puts on a
    /// wire (halheinrich/backgammon#129 leg 2) — this repo's context first,
    /// <see cref="BgDataTypesJsonContext"/> second, per the arc's
    /// composition pattern (most derived first). Deliberately no
    /// <c>DefaultJsonTypeInfoResolver</c> behind them: a type this library
    /// is asked for but no context declares must fail loudly rather than
    /// fall back to reflection a trimmed consumer would not have.
    ///
    /// <para>
    /// Exposed separately from <see cref="Default"/> because it is the one
    /// thing a <i>caller's</i> options also needs: <c>XgFileReader</c>'s
    /// published <c>options</c> parameter lets a caller override formatting
    /// and converters wholesale, but the metadata describing this library's
    /// own document is this library's to supply. One chain, built once,
    /// used by both.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Declared before <see cref="_options"/>: static field initializers run
    /// in textual order and <see cref="BuildOptions"/> reads this one.
    /// </remarks>
    private static readonly IJsonTypeInfoResolver _resolver =
        JsonTypeInfoResolver.Combine(XgJsonContext.Default, BgDataTypesJsonContext.Default);

    private static readonly JsonSerializerOptions _options = BuildOptions();

    /// <inheritdoc cref="_resolver"/>
    public static IJsonTypeInfoResolver Resolver => _resolver;

    public static JsonSerializerOptions Default => _options;

    private static JsonSerializerOptions BuildOptions()
    {
        var opts = new JsonSerializerOptions
        {
            TypeInfoResolver = _resolver,
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters =
    {
        // Enum token policy (halheinrich/backgammon#164). Two populations, and
        // the split is deliberate — order matters, because the first converter
        // whose CanConvert matches wins, so the strict per-enum registrations
        // must precede the blanket one.
        //
        // (1) The BgDataTypes_Lib wire enums are string-token-exact: they are
        // this converter's own vocabulary, every writer emits a name, and a
        // reader that also took ordinals would re-couple a stored document to
        // member numbering. AnalysisLevel makes that concrete — its declaration
        // order is contractual and its families interleave, so inserting a
        // member renumbers every member above it (Ply3Red, 2026-08-28). These
        // types carry strict type-level attributes of their own, but an
        // options-level converter OUTRANKS a type attribute, so registering
        // them here is what preserves that strictness rather than defeating it.
        new JsonStringEnumConverter<AnalysisLevel>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        new JsonStringEnumConverter<AnalysisMode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        new JsonStringEnumConverter<CubeAction>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        new JsonStringEnumConverter<CubeOwner>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),

        // (2) The XG-native enums stay integer-tolerant, and that is a
        // documented safety rather than an oversight: they mirror fields of a
        // third-party binary format whose value space is larger than the named
        // members. SiteId is the proof — real XG and XgpExporter both write
        // (SiteId)(-1) for a local save, so the tolerance is load-bearing on
        // the WRITE side too; allowIntegerValues: false would throw rather than
        // emit it. Every one of these is populated by an unchecked cast from
        // file bytes (SaveRecordParser), so an unnamed value is expected input,
        // not corruption, and round-tripping the number is the correct
        // behaviour. Tightening these would need a per-enum decision about what
        // an unknown code means — a different question from #164's.
        //
        // camelCase is this document's pinned token spelling for both groups
        // and must not change: these tokens are what every existing reader of
        // the emitted JSON already holds.
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
        // Resolved through the options rather than by reflection, so the
        // read path is trim-safe and honours whatever resolver the caller
        // configured (halheinrich/backgammon#129 leg 2). XgJsonContext
        // declares sbyte[] for exactly this call.
        var typeInfo = (JsonTypeInfo<sbyte[]>)options.GetTypeInfo(typeof(sbyte[]));
        var points = JsonSerializer.Deserialize(ref reader, typeInfo) ?? new sbyte[26];
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

        var innerOptions = WithoutSelf(options);

        string json = root.GetRawText();

        return typeName switch
        {
            "HeaderMatch" => Deserialize<MatchHeaderRecord>(json, innerOptions),
            "HeaderGame" => Deserialize<GameHeaderRecord>(json, innerOptions),
            "Move" => Deserialize<MoveRecord>(json, innerOptions),
            "Cube" => Deserialize<CubeRecord>(json, innerOptions),
            "FooterGame" => Deserialize<GameFooterRecord>(json, innerOptions),
            "FooterMatch" => Deserialize<MatchFooterRecord>(json, innerOptions),
            _ => throw new JsonException($"Unknown SaveRecord type: '{typeName}'")
        };
    }
    public override void Write(Utf8JsonWriter writer, SaveRecord value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // Write discriminator first
        writer.WriteString("$type", value.EntryType.ToString());

        var innerOptions = WithoutSelf(options);

        using var doc = JsonSerializer.SerializeToDocument(
            value, innerOptions.GetTypeInfo(value.GetType()));
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name == "$type") continue;
            prop.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Deserializes one concrete record variant through the options'
    /// resolver rather than by reflection — the trim-safe form of
    /// <c>Deserialize&lt;T&gt;(json, options)</c>
    /// (halheinrich/backgammon#129 leg 2). Every variant named by the
    /// <c>$type</c> switch is declared in <see cref="XgJsonContext"/>,
    /// which is what makes the lookup resolve there.
    /// </summary>
    private static T Deserialize<T>(string json, JsonSerializerOptions options)
        where T : SaveRecord
        => (T)JsonSerializer.Deserialize(json, options.GetTypeInfo(typeof(T)))!;

    /// <summary>
    /// Clones <paramref name="options"/> without this converter, so the inner
    /// (de)serialization of a concrete SaveRecord type does not recurse back
    /// into SaveRecordConverter.
    /// </summary>
    private static JsonSerializerOptions WithoutSelf(JsonSerializerOptions options)
    {
        var inner = new JsonSerializerOptions(options);
        inner.Converters.Remove(inner.Converters.First(c => c is SaveRecordConverter));
        return inner;
    }
}
