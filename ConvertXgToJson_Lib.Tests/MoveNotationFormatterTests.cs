namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for <see cref="MoveNotationFormatter"/>. The formatter consumes XG's
/// raw per-candidate move encoding and on-roll-POV board, and returns
/// standard backgammon notation.
/// </summary>
[Collection("FileIO")]
public class MoveNotationFormatterTests
{
    // Helpers --------------------------------------------------------------

    /// <summary>Empty 26-element board — no hit detection fires.</summary>
    private static int[] EmptyBoard() => new int[26];

    /// <summary>Standard opening position from on-roll POV (positive = active).</summary>
    private static int[] OpeningBoard()
    {
        var b = new int[26];
        b[24] = 2; b[13] = 5; b[8] = 3; b[6] = 5;
        b[1] = -2; b[12] = -5; b[17] = -3; b[19] = -5;
        return b;
    }

    private static sbyte[] M(params int[] vals)
    {
        var arr = new sbyte[8];
        for (int i = 0; i < 8; i++) arr[i] = i < vals.Length ? (sbyte)vals[i] : (sbyte)-1;
        return arr;
    }

    // Non-doubles ----------------------------------------------------------

    [Fact]
    public void Format_TwoDistinctMoves_RendersAsPair()
    {
        // 13/8 and 24/22 from standard opening, raw (12, 7)(23, 21), dice 5-2
        var result = MoveNotationFormatter.Format(M(12, 7, 23, 21), OpeningBoard());
        result.Should().Be("13/8 24/22");
    }

    [Fact]
    public void Format_SameMoveTwice_GroupsWithCount()
    {
        // 13/11 13/11 as dice 2 repeated; raw (12, 10)(12, 10)
        var result = MoveNotationFormatter.Format(M(12, 10, 12, 10), EmptyBoard());
        result.Should().Be("13/11(2)");
    }

    [Fact]
    public void Format_SingleMove_TerminatorImmediatelyAfter()
    {
        // One combined-die move, e.g. bar/18 with dice 6-1
        var result = MoveNotationFormatter.Format(M(24, 17), EmptyBoard());
        result.Should().Be("bar/18");
    }

    // Doubles --------------------------------------------------------------

    [Fact]
    public void Format_FullDoubles_NoTerminator()
    {
        // Dice 4-4: 24/20 13/9 13/9 8/4 — raw (23, 19)(12, 8)(12, 8)(7, 3)
        var result = MoveNotationFormatter.Format(
            M(23, 19, 12, 8, 12, 8, 7, 3), EmptyBoard());
        result.Should().Be("24/20 13/9(2) 8/4");
    }

    [Fact]
    public void Format_DoublesAllSame_GroupsAsFour()
    {
        var result = MoveNotationFormatter.Format(
            M(7, 3, 7, 3, 7, 3, 7, 3), EmptyBoard());
        result.Should().Be("8/4(4)");
    }

    // Bar entry ------------------------------------------------------------

    [Fact]
    public void Format_BarEntry_RendersBarPrefix()
    {
        var result = MoveNotationFormatter.Format(M(24, 20), EmptyBoard());
        result.Should().Be("bar/21");
    }

    [Fact]
    public void Format_TwoBarEntries_Grouped()
    {
        var result = MoveNotationFormatter.Format(M(24, 22, 24, 22), EmptyBoard());
        result.Should().Be("bar/23(2)");
    }

    // Bear off -------------------------------------------------------------

    [Fact]
    public void Format_BearOff_RendersOffSuffix()
    {
        // pt 2 → off: raw (1, -1)
        var result = MoveNotationFormatter.Format(M(1, -1), EmptyBoard());
        result.Should().Be("2/off");
    }

    [Fact]
    public void Format_DoublesBearOffSamePoint_Grouped()
    {
        // From the real data: dice 4-4, (3, -1)(3, -1)(3, -1) — three bears off pt 4,
        // preceded by a single (4, 0) = pt 5 → pt 1.
        var result = MoveNotationFormatter.Format(
            M(4, 0, 3, -1, 3, -1, 3, -1), EmptyBoard());
        result.Should().Be("5/1 4/off(3)");
    }

    [Fact]
    public void Format_BearOffFromPoint1_RendersAs1Off()
    {
        // Real case from test data: (5, 2) then (0, -1) — pt 6 → pt 3, pt 1 → off.
        var result = MoveNotationFormatter.Format(M(5, 2, 0, -1), EmptyBoard());
        result.Should().Be("6/3 1/off");
    }

    // Hits -----------------------------------------------------------------

    [Fact]
    public void Format_HitOnDestination_AppendsAsterisk()
    {
        // On-roll POV board has opponent blot at point 18
        var board = new int[26];
        board[24] = 2; // active has 2 on 24
        board[18] = -1; // opponent blot on 18
        // Raw: (23, 17) = 24 → 18, die 6
        var result = MoveNotationFormatter.Format(M(23, 17), board);
        result.Should().Be("24/18*");
    }

    [Fact]
    public void Format_HitThenContinue_KeepsBothLegsSoHitIsVisible()
    {
        // Active at 24(2), opponent blot at 18. Dice 6-1: 24/18*/17.
        // Raw (23, 17, 17, 16): first leg hits 18, so the chain is NOT
        // compressed — a hit at the intermediate must stay visible.
        var board = new int[26];
        board[24] = 2;
        board[18] = -1;
        var result = MoveNotationFormatter.Format(M(23, 17, 17, 16), board);
        result.Should().Be("24/18* 18/17");
    }

    // Chain compression ----------------------------------------------------

    [Fact]
    public void Format_ChainedSubMoves_CompressToSingleLeg()
    {
        // XG sometimes encodes a single checker's combined-die move as two
        // sub-pairs (real example: dice 6-3, raw (23, 20, 20, 14) for 24/15).
        var result = MoveNotationFormatter.Format(M(23, 20, 20, 14), EmptyBoard());
        result.Should().Be("24/15");
    }

    [Fact]
    public void Format_BarEntryChain_CompressesToBarSlashFinal()
    {
        // bar → 21 → 15: raw (24, 20, 20, 14). Compress to "bar/15".
        var result = MoveNotationFormatter.Format(M(24, 20, 20, 14), EmptyBoard());
        result.Should().Be("bar/15");
    }

    [Fact]
    public void Format_ChainThenBearOff_Compresses()
    {
        // pt 4 → pt 1 → off: raw (3, 0, 0, -1). Compress to "4/off".
        var result = MoveNotationFormatter.Format(M(3, 0, 0, -1), EmptyBoard());
        result.Should().Be("4/off");
    }

    [Fact]
    public void Format_TwoIdenticalChains_GroupAfterMerge()
    {
        // Two checkers each chain 24→20→16: raw (23,19,19,15,23,19,19,15).
        // After merge: two legs of (23, 15) → "24/16(2)".
        var result = MoveNotationFormatter.Format(
            M(23, 19, 19, 15, 23, 19, 19, 15), EmptyBoard());
        result.Should().Be("24/16(2)");
    }

    [Fact]
    public void Format_InterleavedDoublesChains_GroupAcrossInterleave()
    {
        // Doubles 6-6 where XG emits the starts together then the
        // continuations together: raw (19,13,19,13,13,7,13,7). Each (13,7)
        // matches one of the earlier open chains ending at 13.
        var result = MoveNotationFormatter.Format(
            M(19, 13, 19, 13, 13, 7, 13, 7), EmptyBoard());
        result.Should().Be("20/8(2)");
    }

    [Fact]
    public void Format_HitWithSeparateBlot_BothMarked()
    {
        // Two separate hits in the same turn: (23, 17) hits 18, (12, 8) hits 9
        var board = new int[26];
        board[24] = 1; board[13] = 1; // active
        board[18] = -1; board[9] = -1; // opponent blots
        var result = MoveNotationFormatter.Format(M(23, 17, 12, 8), board);
        result.Should().Be("24/18* 13/9*");
    }

    // Edge cases -----------------------------------------------------------

    [Fact]
    public void Format_EmptyMoveList_ReturnsEmptyString()
    {
        MoveNotationFormatter.Format([], EmptyBoard()).Should().Be(string.Empty);
    }

    [Fact]
    public void Format_TerminatorFirst_ReturnsEmptyString()
    {
        MoveNotationFormatter.Format(M(-1), EmptyBoard()).Should().Be(string.Empty);
    }

    [Fact]
    public void Format_BoardMutationContained_DoesNotLeakAcrossCalls()
    {
        // Caller is responsible for passing a scratch copy if they need
        // to preserve the original; this test documents the mutation.
        var board = new int[26];
        board[18] = -1;
        MoveNotationFormatter.Format(M(23, 17), board);
        board[18].Should().Be(0);
        board[0].Should().Be(-1); // opponent now on bar index in on-roll POV
    }

    // Real-corpus end-to-end ----------------------------------------------

    /// <summary>
    /// Every analysed move in every .xg test file produces a non-empty
    /// MoveNotation for its best candidate. Guards against stale sentinel
    /// assumptions silently rendering empty strings.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_AllBestCandidates_HaveNonEmptyNotation()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file))
            {
                if (req.Decision.IsCube) continue;
                req.Decision.Plays.Should().NotBeEmpty(
                    $"{Path.GetFileName(path)}: every move request has at least one candidate");
                req.Decision.Plays[0].MoveNotation.Should().NotBeNullOrEmpty(
                    $"{Path.GetFileName(path)}: best candidate must have notation; dice=[{req.Decision.Dice[0]},{req.Decision.Dice[1]}]");
            }
        }
    }
}
