using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AwesomeAssertions;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Json;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The source-generation gate (halheinrich/backgammon#129 leg 2): the
/// context changes the mechanism, never the bytes. <c>JsonContractTests</c>
/// is the outer byte gate — it pins the emitted document against goldens
/// captured before any of this — and passes unchanged. This suite pins the
/// mechanism itself: the same document must come out whichever resolver
/// produces the metadata, the options-level converters must be honoured on
/// the source-generated path (both halves of halheinrich/backgammon#164's
/// enum split), and the context must cover the document's full closure.
/// </summary>
[Collection("FileIO")]
public class XgJsonContextTests
{
    // -----------------------------------------------------------------------
    //  The three metadata mechanisms, over one options configuration.
    //
    //  Every path is built by copying XgJsonOptions.Default and swapping only
    //  its resolver, so this suite never restates the document's policy —
    //  indentation, camelCase naming, the null-ignore condition and the
    //  halheinrich/backgammon#164 enum converters stay single-sourced in
    //  XgJsonOptions. What varies between the paths is exactly one thing:
    //  where the JsonTypeInfo comes from.
    // -----------------------------------------------------------------------

    /// <summary>The pre-change mechanism: runtime reflection.</summary>
    private static readonly JsonSerializerOptions ReflectionOptions =
        new(XgJsonOptions.Default) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    /// <summary>What this library ships: this repo's context chained ahead of
    /// BgDataTypes_Lib's, the arc's composition pattern.</summary>
    private static readonly JsonSerializerOptions ChainedOptions = XgJsonOptions.Default;

    /// <summary>
    /// This repo's context alone, unchained — the pin that the
    /// <see cref="XgFile"/> closure is self-sufficient here and does not
    /// silently lean on the link below it. (The chain is still load-bearing
    /// for <see cref="XgJsonOptions"/> as a whole: the four BgDataTypes_Lib
    /// wire enums resolve one link down, which is what
    /// <c>EnumTokenStrictnessTests</c> exercises.)
    /// </summary>
    private static readonly JsonSerializerOptions ContextOnlyOptions =
        new(XgJsonOptions.Default) { TypeInfoResolver = XgJsonContext.Default };

    // -----------------------------------------------------------------------
    //  Fixtures — real XG-authored files, so every collection and record
    //  variant is populated by the format rather than by a test's imagination.
    // -----------------------------------------------------------------------

    /// <summary>Carries a comment table.</summary>
    private static XgFile WithComments() =>
        XgFileReader.ReadFile(Path.Combine(TestPaths.FixtureFilesDir, "DoubleAnalysis.xgp"));

    /// <summary>Carries a rollout context.</summary>
    private static XgFile WithRollouts() =>
        XgFileReader.ReadFile(Path.Combine(TestPaths.FixtureFilesDir, "TooGoodAndTake.xgp"));

    /// <summary>A full tournament match: every <see cref="SaveRecord"/>
    /// variant the format produces, in one document.</summary>
    private static XgFile WholeMatch() =>
        XgFileReader.ReadFile(TestPaths.AchimMuellerSeqXg);

    private static XgFile Synthetic() =>
        XgFileReader.ReadStream(XgBytesBuilder.BuildMinimalXgFile("Alice", "Bob", 7));

    public static TheoryData<string> Documents => new()
    {
        nameof(WithComments), nameof(WithRollouts), nameof(WholeMatch), nameof(Synthetic),
    };

    private static XgFile Document(string name) => name switch
    {
        nameof(WithComments) => WithComments(),
        nameof(WithRollouts) => WithRollouts(),
        nameof(WholeMatch) => WholeMatch(),
        _ => Synthetic(),
    };

    // -----------------------------------------------------------------------
    //  Byte identity — the invariant of the whole halheinrich/backgammon#129
    //  arc: source generation changes the mechanism, never the bytes.
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryResolver_EmitsTheSameDocument(string document)
    {
        var file = Document(document);

        string reflection = JsonSerializer.Serialize(file, TypeInfo(ReflectionOptions));
        string chained = JsonSerializer.Serialize(file, TypeInfo(ChainedOptions));
        string contextOnly = JsonSerializer.Serialize(file, TypeInfo(ContextOnlyOptions));

        chained.Should().Be(reflection,
            "the shipped resolver chain must reproduce the reflection mechanism byte for byte");
        contextOnly.Should().Be(reflection,
            "this repo's context alone must describe the whole XgFile closure");
    }

    /// <summary>
    /// The read half: a document deserialized through the source-generated
    /// metadata re-emits identically, so the mechanism change is invisible in
    /// both directions.
    /// </summary>
    [Theory]
    [MemberData(nameof(Documents))]
    public void SourceGeneratedRoundTrip_IsStable(string document)
    {
        string json = JsonSerializer.Serialize(Document(document), TypeInfo(ChainedOptions));

        var restored = JsonSerializer.Deserialize(json, TypeInfo(ChainedOptions))!;

        JsonSerializer.Serialize(restored, TypeInfo(ChainedOptions)).Should().Be(json);
    }

    /// <summary>
    /// The published <c>options</c> parameter of
    /// <see cref="XgFileReader.ToJson"/> keeps working for a caller who
    /// brings options carrying no resolver of their own: they are given this
    /// library's metadata (<c>XgJsonOptions.Resolver</c>) in place of the
    /// reflection resolver the serializer used to install on them, and the
    /// document is the one their options describe — here, the same document
    /// as the default, unindented.
    /// </summary>
    [Fact]
    public void CallerOptionsWithoutAResolver_StillSerialize()
    {
        var file = WithComments();
        var compact = new JsonSerializerOptions(XgJsonOptions.Default)
        {
            TypeInfoResolver = null,
            WriteIndented = false,
        };

        string json = XgFileReader.ToJson(file, compact);

        json.Should().NotContain("\n");
        json.Should().Be(
            JsonSerializer.Serialize(
                file,
                (JsonTypeInfo<XgFile>)new JsonSerializerOptions(ReflectionOptions)
                {
                    WriteIndented = false,
                }.GetTypeInfo(typeof(XgFile))));
    }

    // -----------------------------------------------------------------------
    //  Converter respect on the source-generated path. The options-level
    //  converters outrank whatever a resolver supplies, and that must stay
    //  true when the resolver is a source-generated context.
    // -----------------------------------------------------------------------

    [Fact]
    public void ContextPath_PositionEngineStaysACompactArray()
    {
        var position = new PositionEngine { Points = new sbyte[26] };
        position.Points[1] = 2;
        position.Points[24] = -2;

        string json = JsonSerializer.Serialize(position, TypeInfo<PositionEngine>(ContextOnlyOptions));

        json.Should().StartWith("[").And.EndWith("]");
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(26);
        doc.RootElement[1].GetSByte().Should().Be(2);
        doc.RootElement[24].GetSByte().Should().Be(-2);
    }

    /// <summary>
    /// <c>SaveRecordConverter</c> resolves each concrete variant through the
    /// active options at runtime — the property walk that built this context
    /// stops at the abstract base and never reaches them, which is why they
    /// are declared explicitly. This is the test that the declarations are
    /// what that resolution finds.
    /// </summary>
    [Fact]
    public void ContextPath_EveryRecordVariantKeepsItsDiscriminator()
    {
        using var doc = JsonDocument.Parse(
            JsonSerializer.Serialize(WholeMatch(), TypeInfo(ContextOnlyOptions)));

        var stamped = doc.RootElement.GetProperty("records")
            .EnumerateArray()
            .Select(record => record.GetProperty("$type").GetString())
            .Distinct()
            .Order()
            .ToList();

        stamped.Should().Contain(["Cube", "FooterGame", "FooterMatch", "HeaderGame", "HeaderMatch", "Move"],
            "a whole tournament match exercises every variant the format produces");

        // The discriminator is written before the variant's own members.
        doc.RootElement.GetProperty("records")[0].EnumerateObject().First().Name
            .Should().Be("$type");
    }

    /// <summary>
    /// The variant no golden contains and the <c>$type</c> switch does not
    /// name. <c>SaveRecordParser</c> yields an <see cref="UnknownRecord"/>
    /// for any record code it does not recognise, and XG's format declares
    /// two it does not (<see cref="RecordType.Comment"/>,
    /// <see cref="RecordType.Missing"/>), so it can reach the wire. On the
    /// reflection path it always serialized; leaving it undeclared would
    /// have made the source-generated path throw on a document the previous
    /// mechanism wrote fine — a mechanism change visible to a caller, which
    /// is precisely what this leg must not do. The completeness test found
    /// it; this is the concrete case.
    /// </summary>
    [Fact]
    public void ContextPath_SerializesTheUnknownRecordVariant()
    {
        var record = new UnknownRecord(RecordType.Comment);

        string json = JsonSerializer.Serialize<SaveRecord>(record, ContextOnlyOptions);

        json.Should().Be(
            JsonSerializer.Serialize<SaveRecord>(record, ReflectionOptions));
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("$type").GetString().Should().Be("Comment");
    }

    /// <summary>
    /// The XG-native half of halheinrich/backgammon#164's split — deliberately
    /// integer-tolerant — survives the mechanism change. <c>SiteId</c> is the
    /// load-bearing case: real XG and <c>XgpExporter</c> both write
    /// <c>(SiteId)(-1)</c> for a local save, so tolerance matters on the write
    /// side and a source-generated resolver must not quietly tighten it.
    /// </summary>
    [Fact]
    public void ContextPath_XgNativeEnumsStayIntegerTolerant()
    {
        JsonSerializer.Serialize((SiteId)(-1), ContextOnlyOptions).Should().Be("-1");
        JsonSerializer.Deserialize<SiteId>("-1", ContextOnlyOptions).Should().Be((SiteId)(-1));

        JsonSerializer.Serialize(SiteId.FIBS, ContextOnlyOptions).Should().Be("\"fibs\"");
        JsonSerializer.Deserialize<RecordType>("99", ContextOnlyOptions)
            .Should().Be((RecordType)99);
    }

    /// <summary>
    /// The strict half — the four BgDataTypes_Lib wire enums — reaches this
    /// options object through the chained context, and keeps rejecting
    /// numeric ordinals. Chained only: this repo's context does not declare
    /// them, which is the point of chaining rather than re-declaring.
    /// </summary>
    [Fact]
    public void ChainedPath_WireEnumsStayStrict()
    {
        JsonSerializer.Serialize(AnalysisLevel.Ply3, ChainedOptions).Should().Be("\"ply3\"");

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<AnalysisLevel>("4", ChainedOptions));
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<CubeAction>("1", ChainedOptions));

        XgJsonContext.Default.GetTypeInfo(typeof(AnalysisLevel)).Should().BeNull(
            "the wire enums are BgDataTypes_Lib's to declare; this context chains rather than shadows");
    }

    // -----------------------------------------------------------------------
    //  Completeness — the halheinrich/backgammon#144 intersection pattern:
    //  two independent enumerations of one fact, kept agreeing by a test.
    //
    //  Side A is the serialized closure, derived from the running serializer's
    //  own metadata graph rather than from the declaration list: what does
    //  this document actually reference, member by member? Side B is what the
    //  shipped options can resolve. A property added to any record type, or a
    //  seventh SaveRecord variant, lands in side A automatically and fails
    //  here until the context declares it.
    // -----------------------------------------------------------------------

    [Fact]
    public void ShippedOptions_ResolveTheFullDocumentClosure()
    {
        var unresolved = SerializedClosure()
            .Where(type => !XgJsonOptions.Default.TryGetTypeInfo(type, out _))
            .Select(type => type.ToString())
            .Order()
            .ToList();

        unresolved.Should().BeEmpty(
            "every type this document references must resolve through the shipped resolver chain");
    }

    /// <summary>
    /// And the same closure resolves through this repo's context alone: the
    /// <see cref="XgFile"/> document owes nothing to the chain. If a
    /// BgDataTypes_Lib type ever enters the record model this fails while
    /// <see cref="ShippedOptions_ResolveTheFullDocumentClosure"/> passes —
    /// the honest signal that the document has grown a cross-repo member and
    /// the chain became load-bearing for it.
    /// </summary>
    [Fact]
    public void ThisReposContextAlone_ResolvesTheFullDocumentClosure()
    {
        var unresolved = SerializedClosure()
            .Where(type => XgJsonContext.Default.GetTypeInfo(type) is null)
            .Select(type => type.ToString())
            .Order()
            .ToList();

        unresolved.Should().BeEmpty();
    }

    /// <summary>
    /// The roots are derived, not copied from the context's
    /// <c>[JsonSerializable]</c> list — otherwise the check would agree with
    /// itself. <see cref="XgFile"/> is the document root; the
    /// <see cref="SaveRecord"/> variants come from the assembly, so a new one
    /// is discovered here the moment it is written; <see cref="PositionEngine"/>
    /// and <c>sbyte[]</c> are the pair its converter exchanges; the two
    /// metadata DTOs are public wire shapes this options object serves.
    /// </summary>
    private static HashSet<Type> SerializedClosure()
    {
        var roots = new List<Type>
        {
            typeof(XgFile), typeof(PositionEngine), typeof(sbyte[]),
            typeof(XgMatchInfo), typeof(XgGameInfo),
        };
        roots.AddRange(typeof(SaveRecord).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(SaveRecord).IsAssignableFrom(t)));

        var closure = new HashSet<Type>();
        var pending = new Queue<Type>(roots);
        while (pending.Count > 0)
        {
            var type = pending.Dequeue();
            if (!closure.Add(type))
                continue;

            // Ask the serializer what this type serializes as, rather than
            // re-deriving it from reflection. Kind None means a converter owns
            // the wire form wholesale — a primitive, an enum, Guid, DateTime,
            // or one of this library's own options-level converters — and the
            // serializer never walks it, so neither does the closure. (What a
            // converter emits internally is invisible from here, which is
            // exactly why PositionEngine's sbyte[] and the SaveRecord variants
            // are roots.)
            if (!XgJsonOptions.Default.TryGetTypeInfo(type, out var info))
                continue;

            switch (info.Kind)
            {
                case JsonTypeInfoKind.Object:
                    foreach (var property in info.Properties)
                        pending.Enqueue(property.PropertyType);
                    break;
                case JsonTypeInfoKind.Enumerable:
                case JsonTypeInfoKind.Dictionary:
                    if (info.ElementType is not null)
                        pending.Enqueue(info.ElementType);
                    break;
            }
        }

        return closure;
    }

    // -----------------------------------------------------------------------
    //  Trim posture — the declarations that make the analyzer a gate rather
    //  than a suggestion. Asserted here so flipping either off in the csproj
    //  fails a test rather than silently reopening the reflection path.
    // -----------------------------------------------------------------------

    [Fact]
    public void TheLibraryAssembly_DeclaresItselfTrimmable()
    {
        typeof(XgFileReader).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Should().Contain(a => a.Key == "IsTrimmable" && a.Value == "True");
    }

    private static JsonTypeInfo<XgFile> TypeInfo(JsonSerializerOptions options)
        => TypeInfo<XgFile>(options);

    private static JsonTypeInfo<T> TypeInfo<T>(JsonSerializerOptions options)
        => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
