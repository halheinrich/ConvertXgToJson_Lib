using System.Text.Json.Serialization;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;

namespace ConvertXgToJson_Lib.Json;

/// <summary>
/// The source-generated <see cref="JsonSerializerContext"/> for this
/// library's serialization surface — trim-safe <c>System.Text.Json</c>
/// metadata for every type <see cref="XgJsonOptions.Default"/> is asked to
/// serialize, produced at compile time instead of by runtime reflection
/// (halheinrich/backgammon#129 leg 2). The mechanism changes, the bytes do
/// not: the <c>JsonContractTests</c> goldens are the byte gate and pass
/// unchanged.
///
/// <para>
/// <b>Why this context is <see langword="internal"/>, unlike leg 1's.</b>
/// The arc's standing shape is one <em>public</em> context per producer
/// repo, chained by consumers. That shape is unavailable here, and not by
/// omission: this repo's entire document model below <see cref="XgFile"/>
/// is <see langword="internal"/> by design (halheinrich/backgammon#131 —
/// "consumers say what a match <i>is</i> through the builder and never see
/// the on-disk record structure"). The generator emits a
/// <c>public JsonTypeInfo&lt;T&gt;</c> property for every type in the
/// closure, so a public context over an internal closure is a compile
/// error (CS0053, inconsistent accessibility) — for
/// <see cref="RichGameHeader"/>, <see cref="SaveRecord"/>,
/// <see cref="RolloutContext"/> and their collections alike. Publishing
/// this context would therefore mean publishing the record model, which is
/// the decision halheinrich/backgammon#131 deliberately made the other way.
/// Nothing is lost: no consumer serializes this document — the JSON surface
/// is <see cref="XgFileReader.ToJson"/> /
/// <see cref="XgFileReader.WriteJsonAsync"/> /
/// <see cref="XgFileReader.ReadJson"/>, all of which route through
/// <see cref="XgJsonOptions.Default"/> and so are trim-safe from here
/// without the consumer naming anything.
/// </para>
///
/// <para>
/// <b>What is declared, and why.</b> <see cref="XgFile"/> is the document
/// root; the rest of the record model rides the generator's property-graph
/// walk from it. Three groups cannot be reached by that walk and are
/// declared explicitly:
/// <list type="bullet">
///   <item><description>
///     Every concrete <see cref="SaveRecord"/> subtype. The walk stops at
///     the abstract base, yet <see cref="SaveRecordConverter"/> resolves
///     each concrete type through the active
///     <see cref="System.Text.Json.JsonSerializerOptions"/> at runtime —
///     these declarations are what that resolution finds. (Leg 1's
///     <c>Move</c> is the same situation: a converter emitting types the
///     property walk never sees.) That is seven types, not the six the
///     <c>$type</c> switch names: <see cref="UnknownRecord"/> is what
///     <see cref="SaveRecordParser"/> yields for a record code it does not
///     recognise, and XG's format has codes it does not
///     (<see cref="RecordType.Comment"/>, <see cref="RecordType.Missing"/>),
///     so it reaches the wire like any other variant. The completeness test
///     derives this group from the assembly rather than from this list, and
///     is what caught it.
///   </description></item>
///   <item><description>
///     <see cref="PositionEngine"/> and <see cref="GameHeaderRecord"/> are
///     covered by the walk, but <c>sbyte[]</c> is declared for
///     <see cref="PositionEngineConverter"/>'s read path, which asks the
///     options for it directly.
///   </description></item>
///   <item><description>
///     <see cref="XgMatchInfo"/> and <see cref="XgGameInfo"/> — public
///     metadata DTOs with their own pinned wire shapes
///     (<c>MetadataContractTests</c>) that this options object serves but
///     the document does not contain.
///   </description></item>
/// </list>
/// A completeness test keeps the declarations honest: the serialized
/// closure of the roots must resolve through this context, member by
/// member.
/// </para>
///
/// <para>
/// <b>Chaining.</b> <see cref="XgJsonOptions"/> combines this context with
/// <see cref="BgDataTypes_Lib.BgDataTypesJsonContext"/>, per the arc's
/// composition pattern. The chain is a standing seam rather than a live
/// dependency for this document — the <see cref="XgFile"/> closure contains
/// no BgDataTypes_Lib type — but <see cref="XgJsonOptions.Default"/> is
/// asked for the four wire enums directly (their halheinrich/backgammon#164
/// strictness is pinned by <c>EnumTokenStrictnessTests</c> against exactly
/// this options object), and those resolve one link down the chain.
/// </para>
///
/// <para>
/// <b>Metadata-only generation</b>, per the arc's binding rule. The default
/// generation mode also emits fast-path serialize handlers, and a fast-path
/// handler binds nested type resolution to the declaring context's own
/// private options rather than the runtime options it was invoked with,
/// silently bypassing the resolver chain. Leg 1's chained-consumer test
/// pair demonstrates both the failure and the working shape; this context
/// is exactly the downstream shape it modeled, so it declares the same
/// mode.
/// </para>
///
/// <para>
/// <b>The generation options mirror <see cref="XgJsonOptions"/> only as far
/// as the attribute can express.</b> Indentation, the camelCase property
/// naming policy, the null-ignore condition and the number handling are
/// stated here so that metadata produced by this context stands for the
/// same document however it is reached. The enum token policy is
/// deliberately <em>not</em> mirrored: halheinrich/backgammon#164's split
/// needs two parameterized converter registrations
/// (<c>allowIntegerValues: false</c> for the wire enums, camelCase-tolerant
/// for the XG-native ones), and
/// <see cref="JsonSourceGenerationOptionsAttribute"/> can express neither.
/// It stays where halheinrich/backgammon#164 put it and where its tests
/// pin it — on the options object, where an options-level converter
/// outranks a type attribute.
/// </para>
///
/// <para>
/// <b>So do not serialize through this context's own options.</b>
/// <c>JsonSerializer.Serialize(file, typeof(XgFile),
/// XgJsonContext.Default)</c> compiles and runs, and emits a document that
/// is quietly ruined: the attribute above carries the naming and formatting
/// but no converters, so every record collapses to its abstract base's one
/// member — <c>"records": [{"entryType": 0}, …]</c>, no <c>$type</c>, no
/// payload — and <see cref="PositionEngine"/> reverts to an object. It
/// fails silently rather than loudly, which is the sharpest reason this
/// context is <see langword="internal"/>: the trap is reachable only from
/// inside this assembly, from this declaration. The supported paths are
/// <see cref="XgJsonOptions.Default"/> and any caller options that chain
/// this context as a resolver; both are pinned byte-identical to the
/// reflection mechanism they replace (<c>XgJsonContextTests</c>). Closing
/// the gap halfway — declaring the two document converters in the attribute
/// but leaving the enum split unexpressible — would be worse still: output
/// that looks right with the wrong enum tokens.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(XgFile))]
[JsonSerializable(typeof(MatchHeaderRecord))]
[JsonSerializable(typeof(GameHeaderRecord))]
[JsonSerializable(typeof(CubeRecord))]
[JsonSerializable(typeof(MoveRecord))]
[JsonSerializable(typeof(GameFooterRecord))]
[JsonSerializable(typeof(MatchFooterRecord))]
[JsonSerializable(typeof(UnknownRecord))]
[JsonSerializable(typeof(PositionEngine))]
[JsonSerializable(typeof(sbyte[]))]
[JsonSerializable(typeof(XgMatchInfo))]
[JsonSerializable(typeof(XgGameInfo))]
internal sealed partial class XgJsonContext : JsonSerializerContext
{
}
