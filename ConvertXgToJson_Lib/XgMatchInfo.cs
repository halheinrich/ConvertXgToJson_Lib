using System.Text.Json;
using System.Text.Json.Serialization;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Match-level metadata read from a file's <see cref="MatchHeaderRecord"/>:
/// the players and the terms the session is played on. Populated on
/// <see cref="XgIteratorState.MatchInfo"/> before any decision is yielded
/// from the match, so a caller can skip the match whole. Satisfies
/// <see cref="IMatchInfo"/>, so filter layers consume it without naming this
/// type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Money or a match is the terms' kind</b> (halheinrich/backgammon#273,
/// Hal's ruling of 2026-09-26): <see cref="Terms"/> is a
/// <see cref="MoneyTerms"/> or a <see cref="MatchTerms"/>. XG spells an
/// unlimited session as a match length of 99999
/// (<see cref="MatchHeaderRecord.MoneyMatchLengthSentinel"/>); this type's
/// projection reads that sentinel as money terms, and it is never surfaced as
/// a length — <see cref="IMatchInfo"/>'s producer contract.
/// </para>
/// <para>
/// <b>The Max Cube field.</b> XG's header states a maximum cube value as an
/// exponent (<see cref="MatchHeaderRecord.CubeLimit"/>): the value is 2 to
/// that power. A money session's terms carry the limit itself, the power of
/// two the exponent spells (10 is XG's default, a limit of 1024). A match's
/// terms carry no limit (<see cref="MatchTerms"/> holds its length alone), so
/// a match header's field is read with the header — it stays on the parsed
/// record, and the writers copy it through — and states nothing a match's
/// terms hold. XG typically writes 10 for a match, but its files hold other
/// values too; this reader refuses none of them and relates none to the
/// match's length (Hal's ruling on halheinrich/backgammon#273, 2026-09-27).
/// Whether a match's Max Cube is a rule of its own is an open question
/// (halheinrich/backgammon#289).
/// </para>
/// <para>
/// <b>Built by the reader, read back from JSON.</b> The one projection from a
/// header is <see cref="From"/>; the members have internal setters, so code
/// outside this library never assembles one. The type is serialized through
/// this library's JSON options, and every member follows
/// BgDataTypes_Lib's absence rule: each is required on the wire, a document
/// missing one or stating <see langword="null"/> for one is a
/// <see cref="JsonException"/>, and <see cref="Terms"/> is a kinded document
/// read through BgDataTypes_Lib's one dispatch
/// (<see cref="SessionTermsJsonConverter"/>).
/// </para>
/// </remarks>
public sealed class XgMatchInfo : IMatchInfo
{
    /// <summary>
    /// XG's default Max Cube exponent: 10, a maximum cube value of 1024 — the
    /// value XG typically writes. It is what this library writes in a header
    /// it synthesizes for a match, whose terms state no limit to write, and
    /// the limit of a money session it synthesizes without another chosen.
    /// </summary>
    internal const int DefaultCubeLimitExponent = 10;

    /// <summary>The largest exponent whose power of two an <see cref="int"/> holds.</summary>
    private const int LargestCubeLimitExponent = 30;

    // True while the instance is read from a document (see the serializer's
    // constructor below): a missing value is then refused as a JsonException.
    private readonly bool _read;

    // Null only while construction is still stating them.
    private readonly string? _player1;
    private readonly string? _player2;
    private readonly SessionTerms? _terms;

    /// <summary>Creates the metadata; its members are set by the initializer (<see cref="From"/>).</summary>
    internal XgMatchInfo()
    {
    }

    /// <summary>
    /// The serializer's constructor. It binds <paramref name="player1"/>, the
    /// first member, only because a serializer constructor must bind one; it
    /// marks the instance as read, so a member stated as <see langword="null"/>
    /// is refused as a <see cref="JsonException"/> — BgDataTypes_Lib's
    /// pattern for the same rule.
    /// </summary>
    [JsonConstructor]
    internal XgMatchInfo(string player1)
    {
        _read = true;
        Player1 = player1;
    }

    /// <summary>Name of player 1 (bottom player in XG), as the header records it.</summary>
    [JsonInclude, JsonRequired]
    public string Player1
    {
        get => _player1!;
        internal init => _player1 = Stated(value, nameof(Player1));
    }

    /// <summary>Name of player 2 (top player in XG), as the header records it.</summary>
    [JsonInclude, JsonRequired]
    public string Player2
    {
        get => _player2!;
        internal init => _player2 = Stated(value, nameof(Player2));
    }

    /// <summary>
    /// The terms the session is played on: a money session's rules and cube
    /// limit (<see cref="MoneyTerms"/>) or a match's length
    /// (<see cref="MatchTerms"/>).
    /// </summary>
    [JsonInclude, JsonRequired]
    public SessionTerms Terms
    {
        get => _terms!;
        internal init => _terms = Stated(value, nameof(Terms));
    }

    /// <summary>
    /// Projects a parsed <see cref="MatchHeaderRecord"/> to match-level
    /// metadata. The single header-to-<see cref="XgMatchInfo"/> projection,
    /// shared by the file reader, the decision iterator and the match context.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the header states terms this reader will not read: a money
    /// session whose Max Cube exponent spells no power of two an
    /// <see cref="int"/> holds.
    /// </exception>
    internal static XgMatchInfo From(MatchHeaderRecord hm) => new()
    {
        Player1 = hm.Player1,
        Player2 = hm.Player2,
        Terms = TermsOf(hm),
    };

    /// <summary>
    /// The terms <paramref name="hm"/> states: money terms for XG's money
    /// sentinel length, with the limit its Max Cube exponent spells; the
    /// match's length otherwise, whatever its Max Cube field states. The
    /// match's own rules (a length of at least 1) are the terms' to refuse.
    /// </summary>
    private static SessionTerms TermsOf(MatchHeaderRecord hm)
    {
        if (hm.MatchLength >= MatchHeaderRecord.MoneyMatchLengthSentinel)
        {
            return new MoneyTerms
            {
                IsJacoby = hm.Jacoby,
                IsBeaver = hm.Beaver,
                CubeLimit = MoneyCubeLimit(hm.CubeLimit),
            };
        }

        return new MatchTerms { Length = hm.MatchLength };
    }

    /// <summary>The money session's cube limit: the power of two the header's exponent spells.</summary>
    private static int MoneyCubeLimit(int exponent) =>
        exponent is >= 0 and <= LargestCubeLimitExponent
            ? 1 << exponent
            : throw new InvalidDataException(
                $"The money session's header states a Max Cube exponent of {exponent}, which spells no cube value (an exponent from 0 to {LargestCubeLimitExponent} does).");

    /// <summary>
    /// <paramref name="value"/>, which is never <see langword="null"/>: code
    /// passing one gets an <see cref="ArgumentNullException"/>, a document
    /// stating one a <see cref="JsonException"/> carrying it.
    /// </summary>
    private T Stated<T>(T? value, string member) where T : class
    {
        if (value is not null)
            return value;
        var fault = new ArgumentNullException(member, $"{nameof(XgMatchInfo)}.{member} is never null.");
        throw _read ? new JsonException(fault.Message, fault) : fault;
    }
}
