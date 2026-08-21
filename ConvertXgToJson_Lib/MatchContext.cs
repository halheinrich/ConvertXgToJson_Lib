using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

internal sealed class MatchContext
{
    public string? SourceFile { get; }
    public int MatchLength { get; private set; }
    public int Score1 { get; private set; }
    public int Score2 { get; private set; }

    // Three semantic bools replacing the overloaded CrawfordJacoby int.
    // IsCrawford applies in match play only; IsJacoby / IsBeaver in money
    // games only. The XGID wire-format int that folds them back together
    // lives in XgidCrawfordJacobyField below.
    public bool IsCrawford { get; private set; }
    public bool IsJacoby { get; }
    public bool IsBeaver { get; }

    /// <summary>
    /// True for an unlimited (money) session. Deliberately a local property
    /// rather than an inherited <see cref="BgDataTypes_Lib.IMatchInfo"/>
    /// default: this type keeps the player names private behind
    /// <see cref="PlayerName"/>, so implementing that contract would widen the
    /// surface purely to inherit this one member. The derivation is kept
    /// identical to <see cref="BgDataTypes_Lib.IMatchInfo.IsMoneyGame"/>, which
    /// remains the rule's definition — valid here for the same reason it is
    /// there: the 99999 sentinel is normalized away in the constructor.
    /// </summary>
    public bool IsMoneyGame => MatchLength == 0;

    public int CubeValue { get; private set; } = 1;
    public int CubePosition { get; private set; }
    public int GameNumber { get; private set; }
    public int MoveNumber { get; private set; }
    public int MaxCubeLimit { get; private set; } = 6;

    /// <summary>
    /// True when the current game began from the canonical opening position.
    /// Set per-game from <see cref="GameHeaderRecord.InitialPosition"/>; consumers
    /// downstream gate move-number filtering on this so non-standard starts
    /// (problem positions, Bg960, etc.) don't get filtered by 1-based move
    /// numbers that no longer correspond to a real game opening.
    /// </summary>
    public bool IsStandardStart { get; private set; }

    private string _player1 = "Player 1";
    private string _player2 = "Player 2";

    // File-level comment table (temp.xgc), keyed by each record's CommentIndex.
    // Held here alongside the other per-file metadata (player names, match
    // length) so the comment join — and its bounds guard — lives in one place.
    private readonly List<string> _comments;

    public MatchContext(List<SaveRecord> records, string? sourceFile, List<string> comments)
    {
        SourceFile = sourceFile;
        _comments = comments;
        if (records.Count == 0 || records[0] is not MatchHeaderRecord hm)
            throw new InvalidDataException("XG file must begin with a MatchHeaderRecord.");
        _player1 = hm.Player1;
        _player2 = hm.Player2;
        MatchLength = hm.MatchLength >= MatchHeaderRecord.MoneyMatchLengthSentinel ? 0 : hm.MatchLength;
        IsJacoby = IsMoneyGame && hm.Jacoby;
        IsBeaver = IsMoneyGame && hm.Beaver;
        MaxCubeLimit = hm.CubeLimit > 0 ? hm.CubeLimit : 6;
    }

    /// <summary>
    /// The Jacoby fact as it is stamped onto a decision record's
    /// <see cref="BgDataTypes_Lib.PositionData.IsJacoby"/>: the fact itself
    /// for a money session, <see langword="null"/> for match play.
    ///
    /// <para>
    /// The <see langword="null"/> is deliberate, not a shrug. Jacoby is
    /// meaningless off money, so a match record does not pose the question —
    /// and a stamped <c>false</c> would answer it anyway, asserting "the
    /// Jacoby rule was not in force" about a game the rule cannot apply to.
    /// Which kind of record it is stays the away-scores pair's job, the
    /// single source of that truth; this member only carries the fact where
    /// the fact exists. Consumers ignore it on match records either way
    /// (SPEC-stats-identity.md §1, amended 2026-08-20;
    /// halheinrich/backgammon#120).
    /// </para>
    ///
    /// <para>
    /// Derived here rather than at each stamping site so the money-only rule
    /// keeps one home, beside <see cref="IsJacoby"/> and
    /// <see cref="XgidCrawfordJacobyField"/>.
    /// </para>
    /// </summary>
    public bool? JacobyStamp => IsMoneyGame ? IsJacoby : null;

    /// <summary>
    /// XGID field 8: match play encodes Crawford as 1/0; money games encode
    /// Jacoby + 2×Beaver. Collocated here so the wire-format knowledge stays
    /// with the XG binary semantics; XgidEncoder consumes this unchanged.
    /// </summary>
    public int XgidCrawfordJacobyField =>
        !IsMoneyGame
            ? (IsCrawford ? 1 : 0)
            : (IsJacoby ? 1 : 0) + (IsBeaver ? 2 : 0);

    public void Update(SaveRecord record)
    {
        switch (record)
        {
            case GameHeaderRecord gh:
                GameNumber++;
                MoveNumber = 0;
                Score1 = gh.Score1;
                Score2 = gh.Score2;
                IsCrawford = !IsMoneyGame && gh.CrawfordApplies;
                IsStandardStart = BackgammonConstants.IsStandardOpeningPosition(gh.InitialPosition);
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

    public string PlayerName(int activePlayer) =>
        activePlayer >= 0 ? _player1 : _player2;

    /// <summary>
    /// Resolves a record's <c>CommentIndex</c> against the file's comment table.
    /// XG's "no comment" sentinel is <c>-1</c> (the same convention the match
    /// header's comment indices use); that, and any out-of-range index, map to
    /// <see cref="string.Empty"/> rather than indexing the table — so a missing
    /// comment never aliases <c>Comments[0]</c>.
    /// </summary>
    public string CommentAt(int commentIndex) =>
        commentIndex >= 0 && commentIndex < _comments.Count
            ? _comments[commentIndex]
            : string.Empty;

    /// <summary>
    /// Returns the "away" score (points needed to win) from the perspective of
    /// <paramref name="activePlayer"/>. Returns 0 for money games.
    /// </summary>
    public int NeedsFor(int activePlayer) =>
        BackgammonConstants.AwayScore(MatchLength, activePlayer >= 0 ? Score1 : Score2);
}