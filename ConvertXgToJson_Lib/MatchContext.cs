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

    public MatchContext(List<SaveRecord> records, string? sourceFile)
    {
        SourceFile = sourceFile;
        if (records.Count == 0 || records[0] is not MatchHeaderRecord hm)
            throw new InvalidDataException("XG file must begin with a MatchHeaderRecord.");
        _player1 = hm.Player1;
        _player2 = hm.Player2;
        MatchLength = hm.MatchLength >= 99999 ? 0 : hm.MatchLength;
        IsJacoby = MatchLength == 0 && hm.Jacoby;
        IsBeaver = MatchLength == 0 && hm.Beaver;
        MaxCubeLimit = hm.CubeLimit > 0 ? hm.CubeLimit : 6;
    }

    /// <summary>
    /// XGID field 8: match play encodes Crawford as 1/0; money games encode
    /// Jacoby + 2×Beaver. Collocated here so the wire-format knowledge stays
    /// with the XG binary semantics; XgidEncoder consumes this unchanged.
    /// </summary>
    public int XgidCrawfordJacobyField =>
        MatchLength > 0
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
                IsCrawford = MatchLength > 0 && gh.CrawfordApplies;
                IsStandardStart = BackgammonConstants.IsStandardOpeningPosition(gh.InitialPosition);
                CubeValue = 1;
                CubePosition = 0;
                break;

            case MoveRecord mv:
                MoveNumber++;
                CubeValue = XgDecisionIterator.CubeValueActual(mv.CubeValue);
                CubePosition = mv.CubeValue == 0 ? 0 : (mv.CubeValue > 0 ? 1 : -1);
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
    /// Returns the "away" score (points needed to win) from the perspective of
    /// <paramref name="activePlayer"/>. Returns 0 for money games.
    /// </summary>
    public int NeedsFor(int activePlayer) =>
        MatchLength == 0 ? 0 : MatchLength - (activePlayer >= 0 ? Score1 : Score2);
}