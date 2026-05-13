using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for the iterator-level sentinel-only-analysis skip in
/// <see cref="XgDecisionIterator.Iterate"/> and
/// <see cref="XgDecisionIterator.IterateDiagramRequests"/>.
///
/// <para>
/// Two known XG sentinel patterns reach the iterator in real files:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>Moves[best] = [-100, -100, …]</c> — XG's <b>illegal-play
///     workaround</b>. Source play was illegal, XG forces the next
///     position rather than refusing to load and emits this as the
///     lone "candidate." Without the iterator skip, this used to
///     reach <see cref="Parsing.AfterBoardBuilder.ComputeAfterBoard"/>
///     and throw <see cref="IndexOutOfRangeException"/> on
///     <c>board[from + 1] = board[-99]</c>.
///   </description></item>
///   <item><description>
///     <c>Moves[best] = [0, 0, …]</c> — XG's <b>no-legal-move</b>
///     (dance) encoding. Without the iterator skip, this used to
///     reach <see cref="XgMoveTranslator.Translate"/> and render a
///     "1/1" notation glitch.
///   </description></item>
/// </list>
///
/// <para>
/// Both are filtered at the iterator boundary because neither is of
/// interest downstream — there is no real candidate to evaluate.
/// </para>
/// </summary>
[Collection("FileIO")]
public class XgDecisionIteratorSentinelTests
{
    // -----------------------------------------------------------------------
    //  Synthetic — deterministic coverage of both sentinel encodings
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(-100, -100)]
    [InlineData(0, 0)]
    public void Iterate_SentinelOnlyAnalysis_RowSkipped(int from, int to)
    {
        var file = BuildFileWithSentinelOnlyMove((sbyte)from, (sbyte)to);

        var rows = XgDecisionIterator.Iterate(file, sourceFile: "synthetic.xg").ToList();

        rows.Should().BeEmpty(
            "the only MoveRecord in the file carries a sentinel-only analysis " +
            $"({from}, {to}); iterator must filter it");
    }

    [Theory]
    [InlineData(-100, -100)]
    [InlineData(0, 0)]
    public void IterateDiagramRequests_SentinelOnlyAnalysis_RequestSkipped(int from, int to)
    {
        var file = BuildFileWithSentinelOnlyMove((sbyte)from, (sbyte)to);

        var requests = XgDecisionIterator.IterateDiagramRequests(file, sourceFile: "synthetic.xg").ToList();

        requests.Should().BeEmpty(
            "the only MoveRecord in the file carries a sentinel-only analysis " +
            $"({from}, {to}); diagram-request iterator must filter it");
    }

    /// <summary>
    /// Sanity check: the same harness with a normal (non-sentinel)
    /// candidate produces a row. Guards against the synthetic file being
    /// degenerate for some unrelated reason.
    /// </summary>
    [Fact]
    public void Iterate_RealCandidate_RowEmitted()
    {
        // 24/23 — raw (23, 22). Just a benign single-checker move with no
        // hits and no bear-offs. The active player is on point 24.
        var file = BuildFileWithMoves((sbyte)23, (sbyte)22);

        var rows = XgDecisionIterator.Iterate(file, sourceFile: "synthetic.xg").ToList();

        rows.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    //  Fixture-driven — confirmed regression file
    // -----------------------------------------------------------------------

    /// <summary>
    /// Achim Mueller — Mario Sequeira (Monte Carlo WC 2008 SF) is a real
    /// tournament file that contains at least one illegal-play workaround
    /// emission. Before the iterator-level skip, running
    /// <see cref="XgDecisionIterator.Iterate"/> on this file threw
    /// <see cref="IndexOutOfRangeException"/> from
    /// <see cref="Parsing.AfterBoardBuilder.ComputeAfterBoard"/>.
    /// </summary>
    [Fact]
    public void Iterate_AchimMuellerSF_DoesNotThrow()
    {
        var path = TestPaths.AchimMuellerSeqXg;
        var xg = XgFileReader.ReadFile(path);

        var rows = XgDecisionIterator.Iterate(xg, Path.GetFileName(path)).ToList();

        rows.Should().NotBeEmpty(
            "the file contains analysed decisions besides the workaround emissions");
    }

    [Fact]
    public void IterateDiagramRequests_AchimMuellerSF_DoesNotThrow()
    {
        var path = TestPaths.AchimMuellerSeqXg;
        var xg = XgFileReader.ReadFile(path);

        var requests = XgDecisionIterator.IterateDiagramRequests(xg, Path.GetFileName(path)).ToList();

        requests.Should().NotBeEmpty(
            "the file contains analysed decisions besides the workaround emissions");
    }

    /// <summary>
    /// At least one MoveRecord in the corpus must be sentinel-only — otherwise
    /// the regression coverage is illusory (the file would pass even without
    /// the iterator skip). Pins the fixture's continued usefulness.
    /// </summary>
    [Fact]
    public void AchimMuellerSF_ContainsAtLeastOneSentinelOnlyMoveRecord()
    {
        var xg = XgFileReader.ReadFile(TestPaths.AchimMuellerSeqXg);

        bool hasSentinel = xg.Records.OfType<MoveRecord>().Any(IsSentinelOnly);

        hasSentinel.Should().BeTrue(
            "fixture is intentionally chosen because it carries the workaround " +
            "emission; if this fails, the file no longer exercises the bug path");
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal in-memory <see cref="XgFile"/> containing a single
    /// MoveRecord whose analysis carries one candidate consisting of the
    /// supplied <paramref name="from"/> / <paramref name="to"/> sentinel pair
    /// followed by terminator filler.
    /// </summary>
    private static XgFile BuildFileWithSentinelOnlyMove(sbyte from, sbyte to)
    {
        var moves = new sbyte[] { from, to, -1, -1, -1, -1, -1, -1 };
        return BuildFileWithMoves(moves);
    }

    /// <summary>
    /// Builds a minimal in-memory <see cref="XgFile"/> with a single MoveRecord
    /// whose first candidate's <c>Moves</c> array is the supplied <paramref name="vals"/>,
    /// padded with -1 to length 8.
    /// </summary>
    private static XgFile BuildFileWithMoves(params sbyte[] vals)
    {
        var moves = new sbyte[8];
        for (int i = 0; i < 8; i++) moves[i] = i < vals.Length ? vals[i] : (sbyte)-1;

        var initialPos = new PositionEngine
        {
            // One active checker on point 24 so a real (23, 22) move
            // wouldn't underflow. Sentinel cases never read it.
            Points = MakeStandardEnoughBoard(),
        };

        var move = new MoveRecord
        {
            EntryType = RecordType.Move,
            InitialPosition = initialPos,
            FinalPosition = initialPos,        // unused on the sentinel path
            ActivePlayer = 1,
            Dice = [3, 1],
            CubeValue = 0,
            MoveError = -1000.0,               // unanalysed-error sentinel
            Analysis = new BestMoveAnalysis
            {
                MoveCount = 1,
                Evals = [new EvalResult { Equity = 0.0f }],
                Moves = [moves],
                EvalLevels = [new EvalLevel { Level = 1 }],
                PositionsPlayed = [initialPos],
            },
            RolloutIndices = new int[32].Select(_ => -1).ToArray(),
        };

        return new XgFile
        {
            Records =
            {
                new MatchHeaderRecord { EntryType = RecordType.HeaderMatch, MatchLength = 7, Player1 = "P1", Player2 = "P2" },
                new GameHeaderRecord
                {
                    EntryType = RecordType.HeaderGame,
                    InitialPosition = new PositionEngine { Points = StandardOpening() },
                },
                move,
            },
        };
    }

    /// <summary>
    /// A 26-element board with one active checker on point 24 — enough that
    /// a synthetic non-sentinel test move (24/23) is plausible without
    /// the test having to model a full game position.
    /// </summary>
    private static sbyte[] MakeStandardEnoughBoard()
    {
        var pts = new sbyte[26];
        pts[24] = 1;
        return pts;
    }

    private static sbyte[] StandardOpening()
    {
        var pts = new sbyte[26];
        // Standard backgammon opening, on-roll-relative (player1 perspective).
        pts[6]  = -5; pts[8]  = -3; pts[13] =  5; pts[24] = -2;
        pts[19] =  5; pts[17] =  3; pts[12] = -5; pts[1]  =  2;
        return pts;
    }

    private static bool IsSentinelOnly(MoveRecord move)
    {
        if (move.Analysis.Moves.Length == 0) return false;
        sbyte[] m = move.Analysis.Moves[0];
        if (m.Length < 2) return false;
        return (m[0] == -100 && m[1] == -100) || (m[0] == 0 && m[1] == 0);
    }
}
