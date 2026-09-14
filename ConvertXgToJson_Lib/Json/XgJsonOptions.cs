using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;

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
        // an unknown code means — a different question from halheinrich/backgammon#164's.
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
/// Polymorphic converter for <see cref="SaveRecord"/>: writes a
/// <c>$type</c> discriminator first so a consumer can tell the record
/// variants apart, and reads it back to the concrete class.
///
/// <para>
/// <b>The mapping lives in one table.</b> <see cref="Variants"/> pairs
/// every <see cref="RecordType"/> member with the class that carries it;
/// the discriminator string is the member's name, and the read-side lookup
/// is derived from the same table, so <see cref="Write"/> and
/// <see cref="Read"/> cannot disagree (halheinrich/backgammon#177). Two
/// tags share one class: <see cref="RecordType.Comment"/> and
/// <see cref="RecordType.Missing"/> are both <see cref="UnknownRecord"/> —
/// XG declares the codes but never writes them, and
/// <see cref="SaveRecordParser"/> tags whatever it does not recognise with
/// the code it read — and each reads back as an <see cref="UnknownRecord"/>
/// tagged as it was written. That is also why the mapping is a converter
/// and not <c>[JsonPolymorphic]</c> / <c>[JsonDerivedType]</c>: the
/// built-in mechanism binds one discriminator per derived type and rejects
/// a second registration of the same type, so it cannot spell both
/// <c>"Comment"</c> and <c>"Missing"</c> from one class (measured
/// 2026-09-14 on .NET 10: <c>InvalidOperationException: The polymorphic
/// type 'SaveRecord' has already specified derived type
/// 'UnknownRecord'</c>).
/// </para>
///
/// <para>
/// <b>Both directions check the table, so the wire's two copies of the tag
/// agree.</b> A document carries the tag twice — as <c>$type</c> and as
/// the <c>entryType</c> member every record serializes — and a mapping is
/// only a mapping if they cannot drift. A write refuses a record whose
/// runtime type is not the table's class for its
/// <see cref="SaveRecord.EntryType"/> (a <see cref="CubeRecord"/> tagged
/// <c>Move</c>) or whose tag has no discriminator (an unnamed code the
/// parser read from a file); a read refuses a discriminator the table does
/// not name and one that disagrees with the record's own <c>entryType</c>.
/// All four throw <see cref="JsonException"/>.
/// </para>
///
/// <para>
/// <b>Claims <see cref="SaveRecord"/> alone.</b> The converter matches the
/// abstract declared type — the element type of <see cref="XgFile.Records"/>
/// — and no derived type, so the concrete record it serializes or
/// deserializes within resolves to the ordinary object contract of the
/// same options rather than back into this converter. That is what
/// removed the per-record options clone the earlier shape needed to keep
/// from recursing (halheinrich/backgammon#178): there is no derived
/// options object at all.
/// </para>
/// </summary>
internal sealed class SaveRecordConverter : JsonConverter<SaveRecord>
{
    private const string Discriminator = "$type";

    /// <summary>
    /// The one table: every <see cref="RecordType"/> member and the class
    /// that carries it. Everything else the converter knows — the
    /// discriminator strings, the read-side lookup — derives from here.
    /// </summary>
    private static readonly FrozenDictionary<RecordType, Type> Variants =
        new Dictionary<RecordType, Type>
        {
            [RecordType.HeaderMatch] = typeof(MatchHeaderRecord),
            [RecordType.HeaderGame] = typeof(GameHeaderRecord),
            [RecordType.Cube] = typeof(CubeRecord),
            [RecordType.Move] = typeof(MoveRecord),
            [RecordType.FooterGame] = typeof(GameFooterRecord),
            [RecordType.FooterMatch] = typeof(MatchFooterRecord),
            [RecordType.Comment] = typeof(UnknownRecord),
            [RecordType.Missing] = typeof(UnknownRecord),
        }.ToFrozenDictionary();

    /// <summary>The read-side lookup, derived: a member's name is its discriminator.</summary>
    private static readonly FrozenDictionary<string, RecordType> TagsByDiscriminator =
        Variants.Keys.ToFrozenDictionary(DiscriminatorOf, tag => tag, StringComparer.Ordinal);

    /// <summary>Callers pass a table key, which is named by construction.</summary>
    private static string DiscriminatorOf(RecordType tag) =>
        Enum.GetName(tag)
        ?? throw new InvalidOperationException($"RecordType {(byte)tag} is unnamed and cannot be a variant.");

    public override SaveRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Buffer the object: the discriminator is read first, then the whole
        // object is deserialized as the class it names.
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty(Discriminator, out var typeProperty))
            throw new JsonException("SaveRecord missing '$type' discriminator.");
        string discriminator = typeProperty.GetString() ?? "";
        if (!TagsByDiscriminator.TryGetValue(discriminator, out var tag))
            throw new JsonException($"Unknown SaveRecord type: '{discriminator}'");

        var record = (SaveRecord)root.Deserialize(options.GetTypeInfo(Variants[tag]))!;
        if (record.EntryType != tag)
            throw new JsonException(
                $"SaveRecord '$type' {discriminator} disagrees with its entryType {record.EntryType}.");
        return record;
    }

    public override void Write(Utf8JsonWriter writer, SaveRecord value, JsonSerializerOptions options)
    {
        if (!Variants.TryGetValue(value.EntryType, out var variant))
            throw new JsonException(
                $"SaveRecord tagged {value.EntryType} has no '$type' discriminator: the tag is not a RecordType member.");
        if (value.GetType() != variant)
            throw new JsonException(
                $"SaveRecord tagged {value.EntryType} must be a {variant.Name}, not a {value.GetType().Name}.");

        writer.WriteStartObject();
        writer.WriteString(Discriminator, DiscriminatorOf(value.EntryType));

        using var doc = JsonSerializer.SerializeToDocument(value, options.GetTypeInfo(variant));
        foreach (var property in doc.RootElement.EnumerateObject())
            property.WriteTo(writer);

        writer.WriteEndObject();
    }
}
