using BgMoveGen;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for the producer-side notation pipeline:
/// <see cref="XgMoveTranslator.Translate(sbyte[], int[])"/> followed by
/// <see cref="BgMoveGen.MoveNotationFormatter.Format(Play)"/>. The translator
/// owns XG-byte → <see cref="Play"/> conversion and the on-roll-board hit
/// detection; the formatter owns chain compression, ordering, and the
/// final string. Tests exercise the composition because that's the
/// surface consumers depend on.
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

    /// <summary>
    /// Compose the producer-side pipeline: translate XG bytes into a
    /// <see cref="Play"/> (hits encoded into <see cref="Move.ToPt"/>'s sign
    /// while consuming the on-roll-POV board) and format. Tests assert
    /// against the composed output because that's the contract
    /// consumers see.
    /// </summary>
    private static string Render(sbyte[] moves, int[] board)
        => BgMoveGen.MoveNotationFormatter.Format(
            XgMoveTranslator.Translate(moves, board));

    // Non-doubles ----------------------------------------------------------

    /// <summary>
    /// 5-2 opening, raw (12, 7)(23, 21). The local formatter emitted in
    /// XG's input order ("13/8 24/22"); BgMoveGen orders chains by from-pt
    /// descending ("24/22 13/8"). Pins the ordering shift introduced by
    /// the migration.
    /// </summary>
    [Fact]
    public void Format_TwoDistinctMoves_RendersAsPair()
    {
        var result = Render(M(12, 7, 23, 21), OpeningBoard());
        result.Should().Be("24/22 13/8");
    }

    [Fact]
    public void Format_SameMoveTwice_GroupsWithCount()
    {
        // 13/11 13/11 as dice 2 repeated; raw (12, 10)(12, 10)
        var result = Render(M(12, 10, 12, 10), EmptyBoard());
        result.Should().Be("13/11(2)");
    }

    [Fact]
    public void Format_SingleMove_TerminatorImmediatelyAfter()
    {
        // One combined-die move, e.g. bar/18 with dice 6-1
        var result = Render(M(24, 17), EmptyBoard());
        result.Should().Be("bar/18");
    }

    // Doubles --------------------------------------------------------------

    [Fact]
    public void Format_FullDoubles_NoTerminator()
    {
        // Dice 4-4: 24/20 13/9 13/9 8/4 — raw (23, 19)(12, 8)(12, 8)(7, 3)
        var result = Render(M(23, 19, 12, 8, 12, 8, 7, 3), EmptyBoard());
        result.Should().Be("24/20 13/9(2) 8/4");
    }

    [Fact]
    public void Format_DoublesAllSame_GroupsAsFour()
    {
        var result = Render(M(7, 3, 7, 3, 7, 3, 7, 3), EmptyBoard());
        result.Should().Be("8/4(4)");
    }

    // Bar entry ------------------------------------------------------------

    [Fact]
    public void Format_BarEntry_RendersBarPrefix()
    {
        var result = Render(M(24, 20), EmptyBoard());
        result.Should().Be("bar/21");
    }

    [Fact]
    public void Format_TwoBarEntries_Grouped()
    {
        var result = Render(M(24, 22, 24, 22), EmptyBoard());
        result.Should().Be("bar/23(2)");
    }

    // Bear off -------------------------------------------------------------

    [Fact]
    public void Format_BearOff_RendersOffSuffix()
    {
        // pt 2 → off: raw (1, -1)
        var result = Render(M(1, -1), EmptyBoard());
        result.Should().Be("2/off");
    }

    [Fact]
    public void Format_DoublesBearOffSamePoint_Grouped()
    {
        // From the real data: dice 4-4, (3, -1)(3, -1)(3, -1) — three bears off pt 4,
        // preceded by a single (4, 0) = pt 5 → pt 1.
        var result = Render(M(4, 0, 3, -1, 3, -1, 3, -1), EmptyBoard());
        result.Should().Be("5/1 4/off(3)");
    }

    [Fact]
    public void Format_BearOffFromPoint1_RendersAs1Off()
    {
        // Real case from test data: (5, 2) then (0, -1) — pt 6 → pt 3, pt 1 → off.
        var result = Render(M(5, 2, 0, -1), EmptyBoard());
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
        var result = Render(M(23, 17), board);
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
        var result = Render(M(23, 17, 17, 16), board);
        result.Should().Be("24/18* 18/17");
    }

    // Chain compression ----------------------------------------------------

    [Fact]
    public void Format_ChainedSubMoves_CompressToSingleLeg()
    {
        // XG sometimes encodes a single checker's combined-die move as two
        // sub-pairs (real example: dice 6-3, raw (23, 20, 20, 14) for 24/15).
        var result = Render(M(23, 20, 20, 14), EmptyBoard());
        result.Should().Be("24/15");
    }

    [Fact]
    public void Format_BarEntryChain_CompressesToBarSlashFinal()
    {
        // bar → 21 → 15: raw (24, 20, 20, 14). Compress to "bar/15".
        var result = Render(M(24, 20, 20, 14), EmptyBoard());
        result.Should().Be("bar/15");
    }

    [Fact]
    public void Format_ChainThenBearOff_Compresses()
    {
        // pt 4 → pt 1 → off: raw (3, 0, 0, -1). Compress to "4/off".
        var result = Render(M(3, 0, 0, -1), EmptyBoard());
        result.Should().Be("4/off");
    }

    [Fact]
    public void Format_TwoIdenticalChains_GroupAfterMerge()
    {
        // Two checkers each chain 24→20→16: raw (23,19,19,15,23,19,19,15).
        // After merge: two legs of (23, 15) → "24/16(2)".
        var result = Render(M(23, 19, 19, 15, 23, 19, 19, 15), EmptyBoard());
        result.Should().Be("24/16(2)");
    }

    [Fact]
    public void Format_InterleavedDoublesChains_GroupAcrossInterleave()
    {
        // Doubles 6-6 where XG emits the starts together then the
        // continuations together: raw (19,13,19,13,13,7,13,7). Each (13,7)
        // matches one of the earlier open chains ending at 13.
        var result = Render(M(19, 13, 19, 13, 13, 7, 13, 7), EmptyBoard());
        result.Should().Be("20/8(2)");
    }

    [Fact]
    public void Format_HitWithSeparateBlot_BothMarked()
    {
        // Two separate hits in the same turn: (23, 17) hits 18, (12, 8) hits 9
        var board = new int[26];
        board[24] = 1; board[13] = 1; // active
        board[18] = -1; board[9] = -1; // opponent blots
        var result = Render(M(23, 17, 12, 8), board);
        result.Should().Be("24/18* 13/9*");
    }

    // Edge cases -----------------------------------------------------------

    [Fact]
    public void Format_EmptyMoveList_ReturnsEmptyString()
    {
        Render([], EmptyBoard()).Should().Be(string.Empty);
    }

    [Fact]
    public void Format_TerminatorFirst_ReturnsEmptyString()
    {
        Render(M(-1), EmptyBoard()).Should().Be(string.Empty);
    }

    /// <summary>
    /// Hit-detection mutation lives in <see cref="XgMoveTranslator"/> after
    /// the migration — the translator owns the board, the formatter sees
    /// only a sign-encoded <see cref="Play"/>. Caller is responsible for
    /// passing a scratch copy if it needs to preserve the original.
    /// </summary>
    [Fact]
    public void Translate_BoardMutationContained_DoesNotLeakAcrossCalls()
    {
        var board = new int[26];
        board[18] = -1;
        XgMoveTranslator.Translate(M(23, 17), board);
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
            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, Path.GetFileName(path)))
            {
                if (req.Decision.IsCube) continue;
                req.Decision.Plays.Should().NotBeEmpty(
                    $"{Path.GetFileName(path)}: every move request has at least one candidate");
                req.Decision.Plays[0].MoveNotation.Should().NotBeNullOrEmpty(
                    $"{Path.GetFileName(path)}: best candidate must have notation; dice=[{req.Decision.Dice[0]},{req.Decision.Dice[1]}]");
            }
        }
    }

    /// <summary>
    /// Locks in the 21/14 chain-collapse fix from BgMoveGen. Pre-fix,
    /// when XG emitted a single checker's two legs in non-canonical order
    /// (e.g. (20, 14) before (21, 20) for a 7-pip move), the local
    /// formatter's forward-only chain matching produced two separate
    /// legs sharing an intermediate point; bidirectional matching in
    /// BgMoveGen collapses them. This corpus-wide test asserts no
    /// emitted notation contains the uncollapsed pattern: legs i-1 and
    /// i where leg i-1's destination point equals leg i's origin point,
    /// with no <c>*</c> on the intermediate (which would intentionally
    /// keep the legs visible).
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_Notation_NoUncollapsedSameCheckerChains()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, Path.GetFileName(path)))
            {
                if (req.Decision.IsCube) continue;
                foreach (var play in req.Decision.Plays)
                    AssertNoUncollapsedChain(play.MoveNotation, Path.GetFileName(path));
            }
        }
    }

    private static void AssertNoUncollapsedChain(string notation, string fileName)
    {
        if (string.IsNullOrEmpty(notation)) return;
        var legs = notation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < legs.Length; i++)
        {
            // A hit at the intermediate point is intentional and the legs
            // must stay split to keep the * visible. Skip those pairs.
            if (legs[i - 1].Contains('*')) continue;

            string prevTo = StripCount(legs[i - 1]).Split('/')[1];
            string currFrom = StripCount(legs[i]).Split('/')[0];
            prevTo.Should().NotBe(currFrom,
                $"{fileName}: notation '{notation}' has uncollapsed chain at " +
                $"legs {i - 1}/{i} ('{legs[i - 1]}' → '{legs[i]}') — same-checker " +
                "legs sharing a hit-free intermediate point must collapse");
        }
    }

    private static string StripCount(string leg)
    {
        int paren = leg.IndexOf('(');
        return paren < 0 ? leg : leg.Substring(0, paren);
    }
}
