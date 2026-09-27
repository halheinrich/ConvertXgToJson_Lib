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
    /// <see langword="null"/> before the first game header.
    /// </summary>
    public XgGameInfo? GameInfo { get; private set; }

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

    public void Update(SaveRecord record)
    {
        switch (record)
        {
            case GameHeaderRecord gh:
                GameNumber++;
                MoveNumber = 0;
                GameInfo = XgGameInfo.From(gh, MatchInfo.Terms);
                CubeValue = 1;
                CubePosition = 0;
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
    /// <exception cref="InvalidDataException">Thrown when no game header precedes the decision.</exception>
    public Session SessionFor(Seat onRoll)
    {
        var game = GameInfo
            ?? throw new InvalidDataException("A decision record precedes its game's header.");
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
