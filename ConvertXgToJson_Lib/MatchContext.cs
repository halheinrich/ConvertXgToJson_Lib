using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

internal sealed class MatchContext
{
    public string? SourceFile { get; }
    public int MatchLength { get; private set; }
    public int Score1 { get; private set; }
    public int Score2 { get; private set; }
    public int CrawfordJacoby { get; private set; }
    public int CubeValue { get; private set; } = 1;
    public int CubePosition { get; private set; }
    public int GameNumber { get; private set; }
    public int MoveNumber { get; private set; }
    public int MaxCubeLimit { get; private set; } = 6;

    private string _player1 = "Player 1";
    private string _player2 = "Player 2";
//    private bool _lastWasDoubleTake;

    public MatchContext(List<SaveRecord> records, string? sourceFile)
    {
        SourceFile = sourceFile;
        if (records.Count == 0 || records[0] is not MatchHeaderRecord hm)
            throw new InvalidDataException("XG file must begin with a MatchHeaderRecord.");
        _player1 = hm.Player1;
        _player2 = hm.Player2;
        MatchLength = hm.MatchLength >= 99999 ? 0 : hm.MatchLength;
        CrawfordJacoby = MatchLength == 0
            ? (hm.Jacoby ? 1 : 0) + (hm.Beaver ? 2 : 0)
            : 0;
        MaxCubeLimit = hm.CubeLimit > 0 ? hm.CubeLimit : 6;
    }

    public void Update(SaveRecord record)
    {
        switch (record)
        {
            case GameHeaderRecord gh:
                GameNumber++;
                MoveNumber = 0;
                Score1 = gh.Score1;
                Score2 = gh.Score2;
                if (MatchLength > 0 && gh.CrawfordApplies)
                    CrawfordJacoby = 1;
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

    public string MatchScoreFor(int activePlayer)
    {
        if (MatchLength == 0) return "money";
        int onRollScore = activePlayer >= 0 ? Score1 : Score2;
        int opponentScore = activePlayer >= 0 ? Score2 : Score1;
        int away1 = MatchLength - onRollScore;
        int away2 = MatchLength - opponentScore;
        string crawford = CrawfordJacoby == 1 ? "C" : "";
        return $"{away1}a{away2}a{crawford}";
    }
}