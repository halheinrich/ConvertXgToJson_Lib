using ConvertXgToJson_Lib.Parsing;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for <see cref="AfterBoardBuilder.ComputeAfterBoard"/>. The helper
/// applies an XG candidate-play encoding to a prior on-roll-POV board and
/// returns the 26-element after-board with POV flipped (decision-maker's
/// checkers stored as negative values in the result).
/// </summary>
public class AfterBoardBuilderTests
{
    // Helpers --------------------------------------------------------------

    /// <summary>Empty 26-element board.</summary>
    private static int[] EmptyBoard() => new int[26];

    /// <summary>Builds a sentinel-terminated 8-element raw XG move array.</summary>
    private static sbyte[] M(params int[] vals)
    {
        var arr = new sbyte[8];
        for (int i = 0; i < 8; i++) arr[i] = i < vals.Length ? (sbyte)vals[i] : (sbyte)-1;
        return arr;
    }

    /// <summary>Sum of the decision-maker's checkers in the flipped result.</summary>
    /// <remarks>
    /// After a play the opponent is on roll, so the decision-maker's checkers
    /// are *negative* in the flipped POV. This helper sums the absolute value
    /// of the negatives across points 1–24 and the decision-maker's bar
    /// (which is index 0 in the flipped board).
    /// </remarks>
    private static int DecisionMakerCheckerCount(int[] afterBoardFlipped)
    {
        int count = 0;
        for (int i = 0; i <= 24; i++)
            if (afterBoardFlipped[i] < 0) count -= afterBoardFlipped[i];
        return count;
    }

    /// <summary>Sum of the (new) on-roll opponent's checkers in the flipped result.</summary>
    private static int OpponentCheckerCount(int[] afterBoardFlipped)
    {
        int count = 0;
        for (int i = 1; i <= 25; i++)
            if (afterBoardFlipped[i] > 0) count += afterBoardFlipped[i];
        return count;
    }

    /// <summary>Sum of on-roll checkers on a prior (pre-flip) board.</summary>
    private static int OnRollCount(int[] prior)
    {
        int count = 0;
        for (int i = 0; i < 26; i++)
            if (prior[i] > 0) count += prior[i];
        return count;
    }

    /// <summary>Sum of opponent checkers on a prior (pre-flip) board.</summary>
    private static int PriorOpponentCount(int[] prior)
    {
        int count = 0;
        for (int i = 0; i < 26; i++)
            if (prior[i] < 0) count -= prior[i];
        return count;
    }

    // Empty play -----------------------------------------------------------

    [Fact]
    public void ComputeAfterBoard_EmptyMoves_ReturnsPriorFlipped()
    {
        // Opening-style prior (arbitrary small setup): active has 2 on pt 6,
        // opponent has 2 on pt 19 (active-POV pt 19 = opponent's pt 6).
        var prior = EmptyBoard();
        prior[6] = 2;
        prior[19] = -2;

        var result = AfterBoardBuilder.ComputeAfterBoard(prior, M()); // all sentinels

        // Flipped: prior[6]=2 → result[19]=-2; prior[19]=-2 → result[6]=2.
        result.Should().HaveCount(26);
        result[19].Should().Be(-2);
        result[6].Should().Be(2);
        DecisionMakerCheckerCount(result).Should().Be(OnRollCount(prior));
        OpponentCheckerCount(result).Should().Be(PriorOpponentCount(prior));
    }

    // Non-hit plays --------------------------------------------------------

    [Fact]
    public void ComputeAfterBoard_SingleMove_AppliesAndFlips()
    {
        // Active has 2 on pt 24; plays 24/20 (raw 23, 19).
        var prior = EmptyBoard();
        prior[24] = 2;

        var result = AfterBoardBuilder.ComputeAfterBoard(prior, M(23, 19));

        // After the move (on-roll POV pre-flip): pt 24 has 1, pt 20 has 1.
        // Flipped: on-roll pt 24 ↔ flipped index 1; on-roll pt 20 ↔ flipped index 5.
        result[1].Should().Be(-1);
        result[5].Should().Be(-1);
        DecisionMakerCheckerCount(result).Should().Be(2);
    }

    [Fact]
    public void ComputeAfterBoard_ChainedSubMoves_AppliesSequentially()
    {
        // Active has 1 on pt 24; plays 24/18/13 (raw 23, 17, 17, 12).
        var prior = EmptyBoard();
        prior[24] = 1;

        var result = AfterBoardBuilder.ComputeAfterBoard(prior, M(23, 17, 17, 12));

        // After (on-roll POV): pt 13 has 1, everything else empty.
        // Flipped: on-roll pt 13 ↔ flipped index 12.
        result[12].Should().Be(-1);
        DecisionMakerCheckerCount(result).Should().Be(1);
    }

    // Hits -----------------------------------------------------------------

    [Fact]
    public void ComputeAfterBoard_HitOnDestination_SendsBlotToOpponentBar()
    {
        // Active at 24, opponent blot at 18; plays 24/18* (raw 23, 17).
        var prior = EmptyBoard();
        prior[24] = 1;
        prior[18] = -1;

        var result = AfterBoardBuilder.ComputeAfterBoard(prior, M(23, 17));

        // On-roll POV after: pt 18 has 1 (active), opponent bar (idx 0) has -1.
        // Flipped: on-roll idx 18 ↔ flipped idx 7; on-roll idx 0 ↔ flipped idx 25.
        result[7].Should().Be(-1);     // decision-maker's checker now at 18, flipped
        result[25].Should().Be(1);     // opponent bar (positive in flipped POV)
        DecisionMakerCheckerCount(result).Should().Be(1);
        OpponentCheckerCount(result).Should().Be(1);
    }

    [Fact]
    public void ComputeAfterBoard_ChainedHitAtIntermediate_BothHitAndLandingReflected()
    {
        // Active at 24, opponent blot at 18; plays 24/18*/13 (raw 23, 17, 17, 12).
        // Intermediate 18 was hit, checker continues to 13. Final on-roll POV:
        //   pt 13 = 1, opp bar (idx 0) = -1, pt 18 = 0.
        var prior = EmptyBoard();
        prior[24] = 1;
        prior[18] = -1;

        var result = AfterBoardBuilder.ComputeAfterBoard(prior, M(23, 17, 17, 12));

        // Flipped: on-roll 13 ↔ 12; on-roll 0 ↔ 25.
        result[12].Should().Be(-1);
        result[25].Should().Be(1);
        DecisionMakerCheckerCount(result).Should().Be(1);
        OpponentCheckerCount(result).Should().Be(1);
    }

    // Bear off -------------------------------------------------------------

    [Fact]
    public void ComputeAfterBoard_BearOff_RemovesChecker()
    {
        // Active has 1 on pt 2; plays 2/off (raw 1, -1).
        var prior = EmptyBoard();
        prior[2] = 1;

        var result = AfterBoardBuilder.ComputeAfterBoard(prior, M(1, -1));

        // After (on-roll POV): board is empty.
        // Decision-maker's count should be 0 (bore off one).
        DecisionMakerCheckerCount(result).Should().Be(0);
        OnRollCount(prior).Should().Be(1); // sanity: prior had exactly one
    }

    [Fact]
    public void ComputeAfterBoard_DoublesBearOff_RemovesMultiple()
    {
        // Active has 3 on pt 4; plays 4/off 4/off 4/off (raw 3,-1 3,-1 3,-1).
        var prior = EmptyBoard();
        prior[4] = 3;

        var result = AfterBoardBuilder.ComputeAfterBoard(prior, M(3, -1, 3, -1, 3, -1));

        DecisionMakerCheckerCount(result).Should().Be(0);
    }

    // Bar entry ------------------------------------------------------------

    [Fact]
    public void ComputeAfterBoard_BarEntry_PullsFromOnRollBar()
    {
        // Active has 1 on bar (idx 25); plays bar/20 (raw 24, 19).
        var prior = EmptyBoard();
        prior[25] = 1;

        var result = AfterBoardBuilder.ComputeAfterBoard(prior, M(24, 19));

        // On-roll POV after: bar empty, pt 20 = 1.
        // Flipped: on-roll pt 20 ↔ idx 5.
        result[5].Should().Be(-1);
        DecisionMakerCheckerCount(result).Should().Be(1);
    }

    [Fact]
    public void ComputeAfterBoard_BarEntryWithHit_HitTakesPriority()
    {
        // Active has 1 on bar; opponent blot at 20; plays bar/20* (raw 24, 19).
        var prior = EmptyBoard();
        prior[25] = 1;
        prior[20] = -1;

        var result = AfterBoardBuilder.ComputeAfterBoard(prior, M(24, 19));

        // On-roll POV after: bar 0, pt 20 = 1 (active), opp bar (idx 0) = -1.
        result[5].Should().Be(-1);      // active at 20, flipped
        result[25].Should().Be(1);      // opponent bar, flipped
        DecisionMakerCheckerCount(result).Should().Be(1);
        OpponentCheckerCount(result).Should().Be(1);
    }

    // POV-correctness invariant -------------------------------------------

    [Theory]
    [InlineData(0)] // empty play
    [InlineData(1)] // single move
    [InlineData(2)] // hit
    [InlineData(3)] // bear off
    [InlineData(4)] // bar entry
    public void ComputeAfterBoard_DecisionMakerCheckersAreNegative(int scenario)
    {
        int[] prior;
        sbyte[] moves;
        int bearOffs;

        switch (scenario)
        {
            case 0:
                prior = EmptyBoard();
                prior[6] = 5; prior[8] = 3;
                prior[19] = -5; prior[17] = -3;
                moves = M();
                bearOffs = 0;
                break;
            case 1:
                prior = EmptyBoard();
                prior[24] = 2; prior[13] = 5;
                prior[1] = -2; prior[12] = -5;
                moves = M(23, 19, 12, 10);
                bearOffs = 0;
                break;
            case 2:
                prior = EmptyBoard();
                prior[24] = 2;
                prior[18] = -1;
                moves = M(23, 17); // 24/18*
                bearOffs = 0;
                break;
            case 3:
                prior = EmptyBoard();
                prior[2] = 2;
                moves = M(1, -1, 1, -1);
                bearOffs = 2;
                break;
            case 4:
                prior = EmptyBoard();
                prior[25] = 2;
                moves = M(24, 19, 24, 21);
                bearOffs = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var result = AfterBoardBuilder.ComputeAfterBoard(prior, moves);

        // Decision-maker's count in the flipped result equals prior on-roll
        // count minus any bear-offs. Opponent's total count is conserved
        // (hits move a checker from a point to the bar, but don't remove it).
        DecisionMakerCheckerCount(result).Should().Be(OnRollCount(prior) - bearOffs);
        OpponentCheckerCount(result).Should().Be(PriorOpponentCount(prior));

        // Every positive value in the flipped result belongs to the opponent;
        // every negative value belongs to the decision-maker. The invariant
        // we care about is direction: decision-maker's checkers must be
        // negative. Verify no decision-maker checker ended up positive by
        // checking the absolute count matches expected.
        int negatives = 0;
        for (int i = 0; i < 26; i++) if (result[i] < 0) negatives -= result[i];
        negatives.Should().Be(OnRollCount(prior) - bearOffs);
    }

    // FlipBoard (exposed for cube-path callers to produce empty-after-move flips
    // at some later point; independently useful as a 26-element utility). -----

    [Fact]
    public void FlipBoard_Mirrors_AndNegates()
    {
        var board = new int[26];
        board[0] = -2; board[1] = 5; board[24] = -3; board[25] = 1;

        var flipped = AfterBoardBuilder.FlipBoard(board);

        flipped[25].Should().Be(2);  // was board[0]
        flipped[24].Should().Be(-5); // was board[1]
        flipped[1].Should().Be(3);   // was board[24]
        flipped[0].Should().Be(-1);  // was board[25]
    }
}
