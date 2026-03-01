using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Encodes a backgammon position and game state into the XGID string format
/// used by eXtreme Gammon.
///
/// Format:
///   XGID=&lt;pos&gt;:&lt;cubeVal&gt;:&lt;cubePos&gt;:&lt;turn&gt;:&lt;dice&gt;:&lt;score1&gt;:&lt;score2&gt;:&lt;crawfordJacoby&gt;:&lt;matchLen&gt;:&lt;maxCube&gt;
///
/// Position (26 chars):
///   [0]     = top player's bar   (opponent/negative checkers)
///   [1-24]  = points 1-24 from bottom player's perspective
///   [25]    = bottom player's bar (active/positive checkers)
///   '-'     = empty
///   'A'-'P' = 1-16 bottom-player (positive) checkers
///   'a'-'p' = 1-16 top-player (negative) checkers
///
/// cubeVal  = log2 of cube value  (0=1, 1=2, 2=4 ...)
/// cubePos  = 1=bottom owns, 0=centred, -1=top owns
/// turn     = 1=bottom player, -1=top player
/// dice     = "00" to roll, "D" doubled, or two-digit roll e.g. "63"
/// crawfordJacoby = match: 1=crawford game, 0=not; money: Jacoby + 2×Beaver
/// matchLen = 0 for money game
/// maxCube  = log2 of max allowed cube value (typically 6 = 64 max, shown as 2^6)
/// </summary>
public static class XgidEncoder
{
    /// <summary>
    /// Encodes a position and game context into an XGID string.
    /// </summary>
    /// <param name="position">26-element position (positive = bottom/active player).</param>
    /// <param name="cubeValue">Actual cube value (1, 2, 4, 8...).</param>
    /// <param name="cubePos">Cube ownership: 1=bottom, 0=centred, -1=top.</param>
    /// <param name="turn">Whose turn: 1=bottom player, -1=top player.</param>
    /// <param name="dice">Dice roll as two-digit int (e.g. 63), 0=to roll, -1=doubled.</param>
    /// <param name="score1">Bottom player current score.</param>
    /// <param name="score2">Top player current score.</param>
    /// <param name="crawfordJacoby">Match: 1=crawford, 0=not. Money: Jacoby+2×Beaver bitmask.</param>
    /// <param name="matchLength">Match length (0 for money game).</param>
    /// <param name="maxCubeLog2">Log2 of max cube value (default 6 = max cube 64).</param>
    public static string Encode(
        PositionEngine position,
        int cubeValue,
        int cubePos,
        int turn,
        int dice,
        int score1,
        int score2,
        int crawfordJacoby,
        int matchLength,
        int maxCubeLog2 = 6)
    {
        string pos      = EncodePosition(position.Points);
        int    cubeLog  = CubeLog2(cubeValue);
        string diceStr  = EncodeDice(dice);

        return $"XGID={pos}:{cubeLog}:{cubePos}:{turn}:{diceStr}:{score1}:{score2}:{crawfordJacoby}:{matchLength}:{maxCubeLog2}";
    }

    // -----------------------------------------------------------------------

    private static string EncodePosition(sbyte[] points)
    {
        // points[0]    = opponent bar  → XGID char 0 (top player bar)
        // points[1-24] = board points 1-24
        // points[25]   = player bar   → XGID char 25 (bottom player bar)
        var chars = new char[26];
        for (int i = 0; i < 26; i++)
            chars[i] = EncodePoint(points[i]);
        return new string(chars);
    }

    private static char EncodePoint(sbyte v) => v switch
    {
        0 => '-',
        > 0 => (char)('A' + Math.Min((int)v, 16) - 1),
        < 0 => (char)('a' + Math.Min((int)-v, 16) - 1),
    };
    private static string EncodeDice(int dice) => dice switch
    {
        -2   => "R",   // raccoon
        -1   => "B",   // beaver
        0    => "00",  // to roll / cube decision
        > 0  => dice.ToString(),
        _    => "D",   // doubled, waiting for response
    };

    private static int CubeLog2(int cubeValue) =>
        cubeValue <= 1 ? 0 : (int)Math.Round(Math.Log2(Math.Max(1, cubeValue)));
}
