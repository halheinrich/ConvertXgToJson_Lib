using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Game-level metadata read from a <see cref="GameHeaderRecord"/>, against
/// its match's terms: where the players stand as the game begins, and whether
/// it starts from the standard position. Populated on
/// <see cref="XgIteratorState.GameInfo"/> before any decision is yielded from
/// the game, so a caller can skip the game whole. Satisfies
/// <see cref="IGameInfo"/>, so filter layers consume it without naming this
/// type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Money or a match is the standing's kind</b> (halheinrich/backgammon#273,
/// Hal's ruling of 2026-09-26), the kind of the match's terms:
/// <see cref="Standing"/> is a <see cref="MoneyStanding"/> (the points each
/// player has won in the session) or a <see cref="MatchStanding"/> (what each
/// still needs, and whether this is the Crawford game). The money convention
/// of away scores 0 and a Crawford flag always false is gone with the members
/// that held it. The standing is seat-anchored, player 1's first: a record's
/// session is the standing turned to the player on roll, which
/// <see cref="Session.Create"/> does, never this library.
/// </para>
/// <para>
/// <b>Built by the reader, read back from JSON</b>, as
/// <see cref="XgMatchInfo"/> is, and under the same absence rule: every
/// member is required on the wire, a <see langword="null"/> is refused as a
/// <see cref="JsonException"/>, and <see cref="Standing"/> is a kinded
/// document read through BgDataTypes_Lib's one dispatch
/// (<see cref="GameStandingJsonConverter"/>).
/// </para>
/// </remarks>
public sealed class XgGameInfo : IGameInfo
{
    // True while the instance is read from a document (see the serializer's
    // constructor below): a missing value is then refused as a JsonException.
    private readonly bool _read;

    // Null only while construction is still stating it.
    private readonly GameStanding? _standing;

    /// <summary>Creates the metadata; its members are set by the initializer (<see cref="From"/>).</summary>
    internal XgGameInfo()
    {
    }

    /// <summary>
    /// The serializer's constructor. It binds <paramref name="isStandardStart"/>,
    /// the first member, only because a serializer constructor must bind one;
    /// it marks the instance as read, so a <see langword="null"/>
    /// <see cref="Standing"/> is refused as a <see cref="JsonException"/>.
    /// </summary>
    [JsonConstructor]
    internal XgGameInfo(bool isStandardStart)
    {
        _read = true;
        IsStandardStart = isStandardStart;
    }

    /// <summary>
    /// True if the game starts from the standard backgammon opening position
    /// (<see cref="BoardPosition.Standard"/>); false if it starts from a saved
    /// or custom position. Used to filter for opening move decisions.
    /// </summary>
    [JsonInclude, JsonRequired]
    public bool IsStandardStart { get; internal init; }

    /// <summary>
    /// Where the players stand as the game begins, player 1 and player 2: in a
    /// money session the points each has won (<see cref="MoneyStanding"/>), in
    /// a match what each still needs and whether this is the Crawford game
    /// (<see cref="MatchStanding"/>).
    /// </summary>
    [JsonInclude, JsonRequired]
    public GameStanding Standing
    {
        get => _standing!;
        internal init
        {
            if (value is null)
            {
                var fault = new ArgumentNullException(nameof(Standing), $"{nameof(XgGameInfo)}.{nameof(Standing)} is never null.");
                throw _read ? new JsonException(fault.Message, fault) : fault;
            }
            _standing = value;
        }
    }

    /// <summary>
    /// Projects a parsed <see cref="GameHeaderRecord"/> to game-level metadata,
    /// against the match's <paramref name="terms"/>, throwing when the
    /// standing's rules refuse the header's values — the file reader's door.
    /// The decision walk asks the same projection without throwing
    /// (<see cref="TryFrom"/>).
    /// </summary>
    /// <remarks>
    /// A money game's standing is the header's two scores, whatever their
    /// sign: a money score is a running tally that does not bear on a
    /// decision, so a negative one is accepted and never reported (Hal's
    /// ruling of 2026-11-01, halheinrich/backgammon#514). A match game's is
    /// each player's away score — the terms' length less the header's score —
    /// and the header's Crawford flag, stated once, in <see cref="Project"/>.
    /// The standing's own rules (an away score at least 1; in the Crawford
    /// game exactly one player 1-away) are the standing's to refuse, here
    /// through its initializer, which throws; the away scores' bound, the
    /// match's length, is <see cref="GameSession"/>'s, and this does not ask
    /// it (halheinrich/backgammon#308).
    /// </remarks>
    /// <exception cref="ArgumentException">The header's match standing breaks the standing's rules: the exception <see cref="MatchStanding"/>'s initializer throws.</exception>
    internal static XgGameInfo From(GameHeaderRecord gh, SessionTerms terms) =>
        Project(gh, terms, (away1, away2, isCrawford) => new MatchStanding { Away1 = away1, Away2 = away2, IsCrawford = isCrawford })
            ?? throw new UnreachableException("The initializer builds the standing or throws.");

    /// <summary>
    /// <see cref="From"/>'s projection asked without throwing: the metadata
    /// when the header's standing holds the standing's rules, or the
    /// refusal <see cref="MatchStanding.TryCreate"/> gives for its values —
    /// the rules asked in its order, player 1's away score, player 2's, the
    /// Crawford standing. A money standing refuses nothing. The pairing with
    /// the terms' length is not asked here; the decision walk asks it next
    /// (<see cref="MatchContext"/>).
    /// </summary>
    /// <param name="gh">The game's header.</param>
    /// <param name="terms">The match's terms.</param>
    /// <param name="info">The metadata when the standing holds; otherwise <see langword="null"/>.</param>
    /// <param name="refusal">The standing's refusal, as <see cref="MatchStanding.TryCreate"/> gave it; <see langword="null"/> when it holds.</param>
    /// <returns><see langword="true"/> when the header's standing holds the standing's rules.</returns>
    internal static bool TryFrom(
        GameHeaderRecord gh,
        SessionTerms terms,
        [NotNullWhen(true)] out XgGameInfo? info,
        [NotNullWhen(false)] out string? refusal)
    {
        string? standingRefusal = null;
        info = Project(gh, terms, (away1, away2, isCrawford) =>
            MatchStanding.TryCreate(away1, away2, isCrawford, out var standing, out standingRefusal) ? standing : null);
        refusal = info is null
            ? standingRefusal ?? throw new UnreachableException("Only a refused match standing leaves no metadata.")
            : null;
        return info is not null;
    }

    /// <summary>
    /// The one header-to-<see cref="XgGameInfo"/> projection: the start, a
    /// money game's scores, and a match game's away scores and Crawford flag,
    /// with the match standing built by <paramref name="matchStanding"/> from
    /// player 1's away score, player 2's and the flag — the initializer for
    /// <see cref="From"/>, <see cref="MatchStanding.TryCreate"/> for
    /// <see cref="TryFrom"/>. <see langword="null"/> when that builder
    /// refuses.
    /// </summary>
    private static XgGameInfo? Project(GameHeaderRecord gh, SessionTerms terms, Func<int, int, bool, MatchStanding?> matchStanding)
    {
        GameStanding? standing = terms.Match<GameStanding?>(
            money => new MoneyStanding { Score1 = gh.Score1, Score2 = gh.Score2 },
            match => matchStanding(match.Length - gh.Score1, match.Length - gh.Score2, gh.CrawfordApplies));
        return standing is null
            ? null
            : new XgGameInfo { IsStandardStart = StartsFromStandard(gh.InitialPosition), Standing = standing };
    }

    /// <summary>
    /// Whether <paramref name="initial"/> is the standard starting position —
    /// asked of <see cref="BoardPosition"/>, which defines the layout. A
    /// position that is not even well-formed is not the standard one. The
    /// standard position reads the same from either side, so XG's player-1
    /// frame needs no turning.
    /// </summary>
    private static bool StartsFromStandard(PositionEngine initial) =>
        initial.TryToBoardPosition(out var position) && position == BoardPosition.Standard;
}
