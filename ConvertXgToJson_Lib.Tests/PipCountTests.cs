namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for <see cref="XgDecisionIterator.ComputePipCounts"/> — the pip-count
/// helper consumed by <see cref="XgDecisionIterator.IterateDiagramRequests"/>.
/// Synthetic boards exercise the bar-contribution paths that fixture-based
/// tests can't pin precisely.
/// </summary>
public class PipCountTests
{
    /// <summary>
    /// Standard opening position from on-roll POV. Each player has the same
    /// 167-pip starting count regardless of perspective.
    /// </summary>
    private static int[] OpeningBoard()
    {
        var b = new int[26];
        b[24] = 2; b[13] = 5; b[8] = 3; b[6] = 5;
        b[1] = -2; b[12] = -5; b[17] = -3; b[19] = -5;
        return b;
    }

    [Fact]
    public void ComputePipCounts_StandardOpening_BothSidesAt167()
    {
        XgDecisionIterator.ComputePipCounts(OpeningBoard(), out int onRoll, out int opp);
        onRoll.Should().Be(167);
        opp.Should().Be(167);
    }

    /// <summary>
    /// On-roll bar checkers live at <c>board[25]</c> (positive). Each is worth
    /// 25 pips — maximum distance from home.
    /// </summary>
    [Fact]
    public void ComputePipCounts_OnRollOnBar_AddsTwentyFivePerChecker()
    {
        var b = OpeningBoard();
        // Move one of the on-roll checkers from point 24 (24 pips) to bar.
        b[24] -= 1;
        b[25] += 1;
        XgDecisionIterator.ComputePipCounts(b, out int onRoll, out int opp);
        onRoll.Should().Be(167 - 24 + 25, "one checker shifted from point 24 to the bar (24→25 pips)");
        opp.Should().Be(167);
    }

    /// <summary>
    /// Opponent bar checkers live at <c>board[0]</c> with negative entries
    /// (sign convention follows on-roll POV). Each is worth 25 pips for
    /// the opponent.
    /// </summary>
    [Fact]
    public void ComputePipCounts_OpponentOnBar_AddsTwentyFivePerChecker()
    {
        var b = OpeningBoard();
        // Move one opponent checker from point 1 (24 pips for opponent) to bar.
        b[1] += 1;   // -2 → -1
        b[0] -= 1;   //  0 → -1
        XgDecisionIterator.ComputePipCounts(b, out int onRoll, out int opp);
        onRoll.Should().Be(167);
        opp.Should().Be(167 - 24 + 25, "one opponent checker shifted from point 1 to the bar (24→25 pips)");
    }

    /// <summary>
    /// Both sides simultaneously on the bar. Each side's bar contribution
    /// stays independent — board[0] and board[25] do not interact.
    /// </summary>
    [Fact]
    public void ComputePipCounts_BothPlayersOnBar_BothSidesGetTwentyFivePerChecker()
    {
        var b = OpeningBoard();
        b[24] -= 1; b[25] += 1;  // on-roll: 1 checker to bar
        b[1] += 2; b[0] -= 2;    // opponent: 2 checkers to bar
        XgDecisionIterator.ComputePipCounts(b, out int onRoll, out int opp);
        onRoll.Should().Be(167 - 24 + 25);
        opp.Should().Be(167 - 2 * 24 + 2 * 25);
    }

    /// <summary>
    /// Multiple checkers on the bar scale linearly: 3 on-roll bar checkers
    /// add 75 pips, etc. Pin scaling so a future "max one checker counts"
    /// regression can't pass.
    /// </summary>
    [Fact]
    public void ComputePipCounts_MultipleBarCheckers_ScalesLinearly()
    {
        var b = new int[26];
        b[25] = 3;   // 3 on-roll bar checkers
        b[0] = -2;   // 2 opponent bar checkers
        XgDecisionIterator.ComputePipCounts(b, out int onRoll, out int opp);
        onRoll.Should().Be(75);
        opp.Should().Be(50);
    }
}
