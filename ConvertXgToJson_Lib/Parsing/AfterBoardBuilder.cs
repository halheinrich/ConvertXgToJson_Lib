namespace ConvertXgToJson_Lib.Parsing;

/// <summary>
/// Applies an XG candidate-play encoding to a prior board and returns the
/// resulting <see cref="BgDataTypes_Lib.PlayOutcomeData"/>-shaped after-board:
/// 26 elements, POV flipped so the decision-maker's checkers are stored as
/// negative values and the (new) on-roll opponent's are positive.
///
/// <para>
/// Input is the same raw encoding as <see cref="Models.BestMoveAnalysis.Moves"/>:
/// adjacent <c>(from, to)</c> pairs in active-player POV, 0-indexed point values.
/// Sentinels:
///   <c>from == -1</c> terminates; <c>(from, to) == (0, 0)</c> is XG's
///   "no legal moves" (dance) encoding — the whole move list is a no-op;
///   <c>from == 24</c> is bar entry;
///   any <c>to &lt; 0</c> is a bear off (XG encodes overshoot bear-offs as
///   <c>to = from - die</c>, which can be <c>-2, -3, …</c>); otherwise the
///   point is <c>value + 1</c>.
/// </para>
///
/// <para>
/// Hit detection reads the on-roll-POV prior board. An opponent blot
/// (<c>-1</c>) at the destination is sent to the opponent bar (index 0)
/// before the moving checker lands.
/// </para>
/// </summary>
internal static class AfterBoardBuilder
{
    /// <summary>
    /// Clones <paramref name="priorBoardOnRollPov"/>, applies
    /// <paramref name="moves"/> in on-roll POV, then flips POV so the result
    /// is expressed from the new on-roll player's perspective (the opponent
    /// of the player who just moved).
    /// </summary>
    public static int[] ComputeAfterBoard(
        IReadOnlyList<int> priorBoardOnRollPov,
        sbyte[] moves)
    {
        var board = new int[26];
        for (int i = 0; i < 26; i++)
            board[i] = priorBoardOnRollPov[i];

        for (int i = 0; i + 1 < moves.Length; i += 2)
        {
            sbyte from = moves[i];
            sbyte to   = moves[i + 1];
            if (from == -1) break;
            if (from == 0 && to == 0) break; // "no legal moves" — dance

            var (fromIdx, toIdx, isBearOff) = XgMoveEncoding.DecodeMovePair(from, to);
            board[fromIdx]--;

            if (isBearOff) continue; // bear off — no destination side-effects

            if (board[toIdx] == -1)
            {
                // Opponent blot at destination: hit sends it to the opponent
                // bar (index 0, negative in on-roll POV).
                board[toIdx] = 0;
                board[0]--;
            }
            board[toIdx]++;
        }

        return FlipBoard(board);
    }

    /// <summary>
    /// Returns the POV-flipped copy of a 26-element on-roll-POV board:
    /// mirrors index order and negates all values.
    /// </summary>
    internal static int[] FlipBoard(int[] board) => BackgammonConstants.Flip(board);
}
