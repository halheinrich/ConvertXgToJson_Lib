using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// The decision walk's state as it advances record by record — the match's
/// metadata, the current game's, the cube and the game and move counters —
/// and the reads the record builders share: a decision's session, the
/// players' names and a decision's comment.
/// </summary>
/// <remarks>
/// <para>
/// <b>The session is built, never oriented here.</b> The match's terms come
/// from <see cref="XgMatchInfo.From"/> and each game's standing from
/// <see cref="XgGameInfo.From"/>, the two header projections the file reader
/// shares; a decision's session is those two turned to the seat on roll by
/// <see cref="Session.Create"/>, BgDataTypes_Lib's one orientation rule
/// (<see cref="SessionFor"/>). So no score, away score, Crawford flag or
/// Jacoby rule is restated here, and money has no stand-in: a money session
/// states its rules, a match its length (halheinrich/backgammon#273).
/// </para>
/// <para>
/// <b>A game's header is paired at the header</b> (halheinrich/backgammon#310,
/// site 1; halheinrich/backgammon#514): <see cref="Update"/> builds the
/// standing through its constructors and pairs it with the terms through
/// <see cref="GameSession.Create"/>, the one statement of the rule that
/// binds a match's away scores to its length, before any decision of the
/// game is read. A standing those refuse — a player needing 0 points or
/// more than the length, a Crawford flag at the wrong standing — leaves
/// <see cref="GameInfo"/> <see langword="null"/> and the refusal's complete
/// message in <see cref="GameRefusal"/>, so no decision of that game can borrow the
/// game before it; the game number still advances, since the games around
/// it carry theirs. That recognized refusal is the one thing caught here,
/// and only at that call: a null argument is a programming failure and
/// propagates, as does every other exception in the walk.
/// </para>
/// <para>
/// <b>None recorded is <see langword="null"/>.</b> A name or comment XG
/// records as empty is read as none (<see cref="NameOf"/>,
/// <see cref="CommentAt"/>), the one spelling of "none" a record accepts.
/// </para>
/// </remarks>
internal sealed class MatchContext
{
    // File-level comment table (temp.xgc), keyed by each record's CommentIndex.
    // Held here alongside the other per-file metadata so the comment join —
    // and its bounds guard — lives in one place.
    private readonly List<string> _comments;

    public MatchContext(List<SaveRecord> records, List<string> comments)
    {
        _comments = comments;
        if (records.Count == 0 || records[0] is not MatchHeaderRecord hm)
            throw new InvalidDataException("XG file must begin with a MatchHeaderRecord.");
        MatchInfo = XgMatchInfo.From(hm);
    }

    /// <summary>The match's metadata: the players and the terms (<see cref="XgMatchInfo.From"/>).</summary>
    public XgMatchInfo MatchInfo { get; }

    /// <summary>
    /// The current game's metadata — its standing and whether it starts from
    /// the standard position (<see cref="XgGameInfo.From"/>);
    /// <see langword="null"/> before the first game header, and while the
    /// walk is in a game whose header was refused (<see cref="GameRefusal"/>).
    /// </summary>
    public XgGameInfo? GameInfo { get; private set; }

    /// <summary>
    /// Why the current game's header was refused — the complete message of
    /// the exception the standing's constructors or
    /// <see cref="GameSession.Create"/> threw, the rule, the parameter and
    /// the offending value, which may run to a second line (Hal's ruling of
    /// 2026-11-01: keep the full message); text for a reader, not a stable
    /// format — or <see langword="null"/> when the
    /// current game's standing holds (and before the first game header).
    /// Exactly one of this and <see cref="GameInfo"/> is set once a game
    /// header has been read.
    /// </summary>
    public string? GameRefusal { get; private set; }

    /// <summary>Whether the current game is a match's Crawford game — its standing's fact.</summary>
    public bool IsCrawford => GameInfo?.Standing is MatchStanding { IsCrawford: true };

    /// <summary>The cube's face value (1, 2, 4, …) as the walk has advanced it.</summary>
    public int CubeValue { get; private set; } = 1;

    /// <summary>The cube's owner in XG's raw sign convention: +1 player 1, −1 player 2, 0 centred.</summary>
    public int CubePosition { get; private set; }

    /// <summary>The 1-based number of the current game within the file.</summary>
    public int GameNumber { get; private set; }

    /// <summary>The 1-based number of the last move record within the current game.</summary>
    public int MoveNumber { get; private set; }

    /// <summary>
    /// Advances the walk's state over <paramref name="record"/>: a game
    /// header starts a game (its number, counters, cube and standing — paired
    /// as the class remarks state, refused into <see cref="GameRefusal"/>), a
    /// move record counts and reads the cube, a taken double moves it.
    /// </summary>
    public void Update(SaveRecord record)
    {
        switch (record)
        {
            case GameHeaderRecord gh:
                GameNumber++;
                MoveNumber = 0;
                CubeValue = 1;
                CubePosition = 0;
                (GameInfo, GameRefusal) = PairHeader(gh);
                break;

            case MoveRecord mv:
                MoveNumber++;
                CubeValue = XgDecisionIterator.CubeValueActual(mv.CubeValue);
                CubePosition = Math.Sign(mv.CubeValue);
                break;

            case CubeRecord cb:
                if (cb.Doubled == 1 && cb.Taken == 1)
                {
                    int preCube = XgDecisionIterator.CubeValueActual(cb.CubeValue);
                    CubeValue = preCube * 2;
                    CubePosition = cb.ActivePlayer >= 0 ? 1 : -1;
                }
                break;
        }
    }

    /// <summary>
    /// The game's metadata with its standing paired to the match's terms, or
    /// the refusal's complete message when the standing does not hold. The one
    /// recovery in the walk, and the narrowest: the <see cref="ArgumentException"/>
    /// the standing's constructors (<see cref="XgGameInfo.From"/>) and
    /// <see cref="GameSession.Create"/> refuse a header's values with, at
    /// these two calls alone. An <see cref="ArgumentNullException"/> is not
    /// a value's refusal but a programming failure, and propagates.
    /// </summary>
    private (XgGameInfo? Info, string? Refusal) PairHeader(GameHeaderRecord gh)
    {
        try
        {
            var info = XgGameInfo.From(gh, MatchInfo.Terms);
            GameSession.Create(MatchInfo.Terms, info.Standing);
            return (info, null);
        }
        catch (ArgumentException refusal) when (refusal is not ArgumentNullException)
        {
            return (null, refusal.Message);
        }
    }

    /// <summary>
    /// The seat of the player a record's <c>ActivePlayer</c> names: player 1
    /// for a sign of 0 or more, player 2 otherwise — XG's convention, stated
    /// once here.
    /// </summary>
    public static Seat SeatOf(int activePlayer) => activePlayer >= 0 ? Seat.Player1 : Seat.Player2;

    /// <summary>
    /// The session of a decision taken from <paramref name="onRoll"/>: the
    /// match's terms and the current game's standing, turned to that seat by
    /// <see cref="Session.Create"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">Thrown when no game header precedes the decision, or the game's header was refused (<see cref="GameRefusal"/>) — a decision of an omitted game has no session, and the walk never asks for one.</exception>
    public Session SessionFor(Seat onRoll)
    {
        var game = GameInfo
            ?? throw new InvalidDataException(GameRefusal is null
                ? "A decision record precedes its game's header."
                : $"The game's header was refused, so its decisions have no session: {GameRefusal}");
        return Session.Create(MatchInfo.Terms, game.Standing, onRoll);
    }

    /// <summary>
    /// The name of the player in <paramref name="seat"/>, or
    /// <see langword="null"/> when the header records none (an empty or
    /// white-space name).
    /// </summary>
    public string? NameOf(Seat seat) =>
        Recorded(seat == Seat.Player1 ? MatchInfo.Player1 : MatchInfo.Player2);

    /// <summary>
    /// Resolves a record's <c>CommentIndex</c> against the file's comment table
    /// and returns the decision's comment <b>as plain text</b>, or
    /// <see langword="null"/> when there is none. XG's "no comment" sentinel is
    /// <c>-1</c> (the same convention the match header's comment indices use);
    /// that and any out-of-range index are none rather than an index into the
    /// table — so a missing comment never aliases <c>Comments[0]</c> — and so
    /// is a comment whose text is empty.
    /// </summary>
    /// <remarks>
    /// XG stores its comments as RTF documents, and the stamp path is where
    /// that stops being the consumer's problem: this is the one conversion
    /// site in the library (halheinrich/backgammon#233). It sits beside the
    /// index resolution because the resolution and the rendering answer the
    /// same question, "what note does this decision carry?", and its one
    /// caller is the <c>DescriptiveData.Comment</c> stamp both record kinds
    /// share in <see cref="XgDecisionIterator"/>. The conversion itself belongs to
    /// <see cref="RtfPlainText"/>, whose doc is the one statement of the
    /// contract. The comment <i>table</i> is untouched: <c>XgFile.Comments</c>,
    /// the reader, the writer and the <c>.xgp</c> slice export all still carry
    /// XG's bytes verbatim, so a round trip reproduces them.
    /// </remarks>
    public string? CommentAt(int commentIndex) =>
        commentIndex >= 0 && commentIndex < _comments.Count
            ? Recorded(RtfPlainText.Extract(_comments[commentIndex]))
            : null;

    /// <summary>XG's text as a record states it: <see langword="null"/> for empty or white-space text.</summary>
    private static string? Recorded(string text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
