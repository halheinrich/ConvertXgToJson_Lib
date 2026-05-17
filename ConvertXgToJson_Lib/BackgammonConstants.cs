using System.Numerics;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Shared backgammon constants and position utilities.
/// </summary>
internal static class BackgammonConstants
{
    /// <summary>
    /// Standard backgammon opening position.
    /// Index 0 = opponent bar, 1–24 = points, 25 = player bar.
    /// Positive = bottom player's checkers, negative = top player's checkers.
    /// </summary>
    internal static readonly sbyte[] StandardOpeningPosition = new sbyte[26]
    {
         0,   // [0]  opponent bar
        -2,   // [1]  point 1  — top player 2 checkers
         0, 0, 0, 0,
         5,   // [6]  point 6  — bottom player 5 checkers
         0,
         3,   // [8]  point 8  — bottom player 3 checkers
         0, 0, 0,
        -5,   // [12] point 12 — top player 5 checkers
         5,   // [13] point 13 — bottom player 5 checkers
         0, 0, 0,
        -3,   // [17] point 17 — top player 3 checkers
         0,
        -5,   // [19] point 19 — top player 5 checkers
         0, 0, 0, 0,
         2,   // [24] point 24 — bottom player 2 checkers
         0,   // [25] player bar
    };

    internal static bool IsStandardOpeningPosition(PositionEngine position)
    {
        for (int i = 0; i < 26; i++)
            if (position.Points[i] != StandardOpeningPosition[i])
                return false;
        return true;
    }

    /// <summary>
    /// Returns a 26-element copy of <paramref name="board"/> flipped to the
    /// opposite player's perspective: index <c>i</c> swapped with <c>25 - i</c>,
    /// every value negated. The single source of the perspective flip, used
    /// for both raw position arrays and on-roll board arrays.
    /// </summary>
    internal static T[] Flip<T>(IReadOnlyList<T> board) where T : INumber<T>
    {
        var flipped = new T[26];
        for (int i = 0; i < 26; i++)
            flipped[i] = -board[25 - i];
        return flipped;
    }

    /// <summary>
    /// A player's "away" score — points still needed to win — for a match of
    /// <paramref name="matchLength"/> with the player at <paramref name="score"/>.
    /// 0 for money games (<paramref name="matchLength"/> 0). The single source
    /// of the away-score rule.
    /// </summary>
    internal static int AwayScore(int matchLength, int score)
        => matchLength == 0 ? 0 : matchLength - score;
}