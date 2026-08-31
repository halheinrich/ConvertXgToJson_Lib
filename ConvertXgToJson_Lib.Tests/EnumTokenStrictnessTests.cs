using System.Globalization;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Json;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins <see cref="XgJsonOptions"/>' two-population enum token policy
/// (halheinrich/backgammon#164). The BgDataTypes_Lib wire enums are
/// string-token-exact — names in, names out, ordinals rejected. The XG-native
/// enums are deliberately integer-tolerant, because they mirror fields of a
/// third-party binary format whose value space exceeds the named members. Both
/// halves are pinned, so a future blanket tightening of either fails loudly
/// rather than silently breaking the other.
/// </summary>
public class EnumTokenStrictnessTests
{
    private static readonly JsonSerializerOptions Options = XgJsonOptions.Default;

    // ------------------------------------------------------------------ //
    //  (1) The wire enums: string-token-exact
    // ------------------------------------------------------------------ //

    /// <summary>
    /// The reader is the inverse of its writer: the camelCase token this
    /// document writes deserializes to its member, while the member's own
    /// ordinal — and an undefined one — are rejected.
    /// </summary>
    private static void AssertStringTokenExact<TEnum>(string expectedToken, TEnum member)
        where TEnum : struct, Enum
    {
        JsonSerializer.Serialize(member, Options).Should().Be($"\"{expectedToken}\"");
        JsonSerializer.Deserialize<TEnum>($"\"{expectedToken}\"", Options).Should().Be(member);

        string ordinal = Convert.ToInt32(member, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TEnum>(ordinal, Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TEnum>("99", Options));
    }

    [Fact]
    public void AnalysisLevel_IsStringTokenExact() =>
        AssertStringTokenExact("ply3", AnalysisLevel.Ply3);

    [Fact]
    public void AnalysisMode_IsStringTokenExact() =>
        AssertStringTokenExact("bookRollout", AnalysisMode.BookRollout);

    [Fact]
    public void CubeAction_IsStringTokenExact() =>
        AssertStringTokenExact("take", CubeAction.Take);

    [Fact]
    public void CubeOwner_IsStringTokenExact() =>
        AssertStringTokenExact("onRoll", CubeOwner.OnRoll);

    /// <summary>
    /// The camelCase spelling is the document's pinned wire contract, and it
    /// comes from this options object rather than from the enums' own
    /// type-level attributes — those write PascalCase, and an options-level
    /// converter outranks a type attribute. Every member of every wire enum
    /// round-trips through the tightened registration, so the strictness costs
    /// no legitimate token.
    /// </summary>
    [Fact]
    public void EveryWireEnumMember_RoundTripsAsCamelCase()
    {
        AssertRoundTrips<AnalysisLevel>();
        AssertRoundTrips<AnalysisMode>();
        AssertRoundTrips<CubeAction>();
        AssertRoundTrips<CubeOwner>();

        static void AssertRoundTrips<TEnum>()
            where TEnum : struct, Enum
        {
            foreach (TEnum member in Enum.GetValues<TEnum>())
            {
                string json = JsonSerializer.Serialize(member, Options);
                json.Should().Be($"\"{JsonNamingPolicy.CamelCase.ConvertName(member.ToString()!)}\"");
                JsonSerializer.Deserialize<TEnum>(json, Options).Should().Be(member);
            }
        }
    }

    // ------------------------------------------------------------------ //
    //  (2) The XG-native enums: integer-tolerant, deliberately
    // ------------------------------------------------------------------ //

    /// <summary>
    /// The documented-safe carve-out, pinned so it cannot be "tidied" into
    /// strictness: <see cref="SiteId"/> is legitimately <c>-1</c> — real XG and
    /// <c>XgpExporter</c> both write that for a local (non-site-imported) save,
    /// as <see cref="SiteId"/>'s own remarks state. Integer tolerance is
    /// load-bearing on the WRITE side here: <c>allowIntegerValues: false</c>
    /// throws rather than emitting the value, which is why the
    /// halheinrich/backgammon#164 tightening is scoped to the wire enums and
    /// not applied blanket to this options object.
    /// </summary>
    [Fact]
    public void SiteId_UndefinedValue_RoundTripsAsNumber()
    {
        string json = JsonSerializer.Serialize((SiteId)(-1), Options);

        json.Should().Be("-1");
        JsonSerializer.Deserialize<SiteId>(json, Options).Should().Be((SiteId)(-1));
    }

    /// <summary>
    /// The same tolerance for the enums parsed by unchecked cast straight from
    /// file bytes in <c>SaveRecordParser</c> — an unnamed code is expected
    /// input from a third-party format, not corruption, so round-tripping the
    /// number is the correct behaviour rather than a hazard to close.
    /// </summary>
    [Theory]
    [InlineData(typeof(RecordType))]
    [InlineData(typeof(ClockType))]
    [InlineData(typeof(GameMode))]
    [InlineData(typeof(CurrencyId))]
    public void XgNativeEnums_TolerateUnnamedCodes(Type enumType)
    {
        object value = JsonSerializer.Deserialize("99", enumType, Options)!;

        Convert.ToInt32(value, CultureInfo.InvariantCulture).Should().Be(99);
    }
}
