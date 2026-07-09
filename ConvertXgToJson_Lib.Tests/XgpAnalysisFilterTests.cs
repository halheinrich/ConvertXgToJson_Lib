using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests the <c>.xgp</c> emission policy shared by
/// <see cref="XgDecisionIterator.Iterate"/> and
/// <see cref="XgDecisionIterator.IterateDiagramRequests"/>. Two rules combine:
///
/// <list type="number">
///   <item><description>
///     <b>Skip unanalysed.</b> An unanalysed decision does not appear at all.
///   </description></item>
///   <item><description>
///     <b>At most one decision per file.</b> Emit the analysed checker-play if
///     the file has one; otherwise the analysed cube, if any. XG always writes
///     a cube pane, so an <c>.xgp</c> saved after the dice were rolled can carry
///     analysis in both panes — but the decision the file records is the play.
///     Depth is not compared: an analysed play wins even against a
///     deeper-analysed cube.
///   </description></item>
/// </list>
///
/// <para>
/// Rule 2 is what makes the bare-filename <c>XgpDecisionId</c> a valid key;
/// without it, a both-panes-analysed <c>.xgp</c> yielded two decisions stamped
/// with the same Id.
/// </para>
///
/// Fixtures live under <c>../TestData/FixtureFiles/</c>:
///   <list type="bullet">
///   <item><c>NoAnalysis.xgp</c> — cube only, never analysed
///         (<c>Level=-100, LevelRequest=0</c>).</item>
///   <item><c>DoubleAnalysis.xgp</c>, <c>TakeAnalysis.xgp</c>,
///         <c>BothAnalysis.xgp</c> — cube only, fully analysed at level 1002.
///         These are curated cube problems: pre-roll positions, so XG wrote no
///         move pane at all.</item>
///   <item><c>PlayAnalysis.xgp</c> — analysed move + unanalysed cube (the cube
///         portion of an .xgp is always emitted by XG even when it has no
///         meaningful analysis).</item>
///   <item><c>Opening 32 65 64 31 65.xgp</c> — analysed move + cube whose
///         analysis was <em>requested</em> (<c>LevelRequest=1002</c>) but
///         <em>never ran</em> (<c>Level=-100</c>). Real-world reproducer for
///         the phantom-cube-slide bug.</item>
///   <item><c>Joe Russell … 12-16-2010_12_25.xgp</c> — <em>both</em> panes
///         analysed (move rolled out at 1296 trials; cube at <c>Level=1</c>).
///         Real-world reproducer for the duplicate-Id bug that rule 2 fixes.</item>
///   </list>
///
/// <para>
/// No on-disk fixture has the cube-fallback shape — an analysed cube sitting
/// beside a move pane the iterator declines to emit — so those cases are driven
/// from synthetic <see cref="XgFile"/>s below.
/// </para>
/// </summary>
[Collection("FileIO")]
public class XgpAnalysisFilterTests
{
    private const string JoeRussellXgp =
        "Joe Russell (4.69) - Bob Glass (3.92) 13pt 12-16-2010_12_25.xgp";

    private static string FixtureDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            @"..\..\..\..\..\TestData\FixtureFiles"));

    private static string Fixture(string name) => Path.Combine(FixtureDir, name);

    // -----------------------------------------------------------------------
    //  Per-fixture decision counts — "skip unanalysed" + "at most one"
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("NoAnalysis.xgp",                 0)]
    [InlineData("DoubleAnalysis.xgp",             1)]
    [InlineData("TakeAnalysis.xgp",               1)]
    [InlineData("PlayAnalysis.xgp",               1)]
    [InlineData("BothAnalysis.xgp",               1)]
    [InlineData("Opening 32 65 64 31 65.xgp",     1)]
    [InlineData(JoeRussellXgp,                    1)]
    public void Iterate_YieldsOnlyAnalysedDecisions(string fileName, int expected)
    {
        var file = XgFileReader.ReadFile(Fixture(fileName));
        XgDecisionIterator.Iterate(file, fileName).Count().Should().Be(expected);
    }

    [Theory]
    [InlineData("NoAnalysis.xgp",                 0)]
    [InlineData("DoubleAnalysis.xgp",             1)]
    [InlineData("TakeAnalysis.xgp",               1)]
    [InlineData("PlayAnalysis.xgp",               1)]
    [InlineData("BothAnalysis.xgp",               1)]
    [InlineData("Opening 32 65 64 31 65.xgp",     1)]
    [InlineData(JoeRussellXgp,                    1)]
    public void IterateDiagramRequests_YieldsOnlyAnalysedDecisions(string fileName, int expected)
    {
        var file = XgFileReader.ReadFile(Fixture(fileName));
        XgDecisionIterator.IterateDiagramRequests(file, fileName).Count().Should().Be(expected);
    }

    // -----------------------------------------------------------------------
    //  PlayAnalysis: the one row must be the move, never the cube
    // -----------------------------------------------------------------------

    [Fact]
    public void PlayAnalysis_YieldsMoveRowNotCubeRow()
    {
        var file = XgFileReader.ReadFile(Fixture("PlayAnalysis.xgp"));
        var rows = XgDecisionIterator.Iterate(file, "PlayAnalysis.xgp").ToList();
        rows.Should().ContainSingle();
        rows[0].IsCube.Should().BeFalse(
            "PlayAnalysis.xgp contains an analysed move and an unanalysed cube; " +
            "only the move row should be yielded");
    }

    [Fact]
    public void Opening_3265643165_YieldsMoveRowOnly_NotPhantomCubeRow()
    {
        const string name = "Opening 32 65 64 31 65.xgp";
        var file = XgFileReader.ReadFile(Fixture(name));
        var rows = XgDecisionIterator.Iterate(file, name).ToList();
        rows.Should().ContainSingle(
            "the cube has Level=-100 (queued, never analysed) — only the analysed " +
            "move row should be yielded");
        rows[0].IsCube.Should().BeFalse(
            "the only yielded row must be the analysed move, not the phantom cube");
    }

    [Fact]
    public void Opening_3265643165_DiagramRequests_YieldMoveRequestOnly()
    {
        const string name = "Opening 32 65 64 31 65.xgp";
        var file = XgFileReader.ReadFile(Fixture(name));
        var requests = XgDecisionIterator.IterateDiagramRequests(file, name).ToList();
        requests.Should().ContainSingle();
        requests[0].Decision.IsCube.Should().BeFalse(
            "the cube was requested but never analysed — the diagram request iterator " +
            "must skip it, not emit an empty cube panel");
    }

    // -----------------------------------------------------------------------
    //  Both panes analysed — the play wins, and the Id stays unique
    // -----------------------------------------------------------------------

    /// <summary>
    /// Pins the fixture's continued usefulness: if XG ever re-saves this file
    /// with only one analysed pane, the regression tests below would pass even
    /// with the emission policy removed.
    /// </summary>
    [Fact]
    public void JoeRussell_FixtureHasBothPanesAnalysed()
    {
        var file = XgFileReader.ReadFile(Fixture(JoeRussellXgp));

        file.Records.OfType<MoveRecord>()
            .Should().ContainSingle(m => m.Analysis.MoveCount > 0 && m.Analysis.Evals.Length > 0,
                "the move pane must be analysed for this fixture to exercise play-over-cube");
        file.Records.OfType<CubeRecord>()
            .Should().ContainSingle(c => c.Analysis.Level > 0,
                "the cube pane must also be analysed — that co-occurrence is the bug shape");
    }

    [Fact]
    public void JoeRussell_Iterate_YieldsPlayOnly_NotBothDecisions()
    {
        var file = XgFileReader.ReadFile(Fixture(JoeRussellXgp));

        var rows = XgDecisionIterator.Iterate(file, JoeRussellXgp).ToList();

        rows.Should().ContainSingle(
            "both panes are analysed, but an .xgp records exactly one decision; " +
            "emitting both stamped them with the same bare-filename XgpDecisionId");
        rows[0].IsCube.Should().BeFalse(
            "dice in the file mean the saved decision is the play — the cube pane is " +
            "XG's always-present incidental");
    }

    [Fact]
    public void JoeRussell_IterateDiagramRequests_YieldsPlayOnly_NotBothDecisions()
    {
        var file = XgFileReader.ReadFile(Fixture(JoeRussellXgp));

        var requests = XgDecisionIterator.IterateDiagramRequests(file, JoeRussellXgp).ToList();

        requests.Should().ContainSingle();
        requests[0].Decision.IsCube.Should().BeFalse(
            "both surfaces route through the same IterateCore and must agree on which " +
            "decision an .xgp represents");
    }

    /// <summary>
    /// The play is emitted even though the cube was analysed too. Depth is
    /// deliberately not the tie-breaker — here the play happens to be the
    /// deeper of the two, so this pins intent rather than an accident of the
    /// fixture; <see cref="SentinelPlayWithAnalysedCube_YieldsCube"/> and
    /// <see cref="UnanalysedPlayWithAnalysedCube_YieldsCube"/> cover the
    /// converse.
    /// </summary>
    [Fact]
    public void JoeRussell_BothSurfaces_AgreeOnTheSameDecisionId()
    {
        var file = XgFileReader.ReadFile(Fixture(JoeRussellXgp));

        var rowId = XgDecisionIterator.Iterate(file, JoeRussellXgp).Single().Id;
        var reqId = XgDecisionIterator.IterateDiagramRequests(file, JoeRussellXgp).Single().Id;

        rowId.Should().Be(reqId);
        rowId.Should().BeOfType<XgpDecisionId>();
    }

    // -----------------------------------------------------------------------
    //  Cube fallback — no on-disk fixture has these shapes
    // -----------------------------------------------------------------------

    /// <summary>
    /// A move pane that carries no analysis must not suppress an analysed cube.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnanalysedPlayWithAnalysedCube_YieldsCube(bool cubeFirst)
    {
        var file = BuildXgp(SyntheticMove.Unanalysed, cubeAnalysed: true, cubeFirst);

        AssertSingleDecision(file, "synthetic.xgp", expectCube: true);
    }

    /// <summary>
    /// A sentinel-skipped play (illegal-play marker / dance) is dropped upstream
    /// of the emission policy, so — per the rule — it falls back to the cube
    /// rather than suppressing it.
    /// </summary>
    [Theory]
    [InlineData(-100, -100)]
    [InlineData(0, 0)]
    public void SentinelPlayWithAnalysedCube_YieldsCube(int from, int to)
    {
        var file = BuildXgp(SyntheticMove.Sentinel((sbyte)from, (sbyte)to), cubeAnalysed: true, cubeFirst: true);

        AssertSingleDecision(file, "synthetic.xgp", expectCube: true);
    }

    [Fact]
    public void UnanalysedPlayWithUnanalysedCube_YieldsNothing()
    {
        var file = BuildXgp(SyntheticMove.Unanalysed, cubeAnalysed: false, cubeFirst: true);

        XgDecisionIterator.Iterate(file, "synthetic.xgp").Should().BeEmpty();
        XgDecisionIterator.IterateDiagramRequests(file, "synthetic.xgp").Should().BeEmpty();
    }

    /// <summary>
    /// Record order must not decide the winner: XG's pane ordering is an
    /// implementation detail, so the policy buffers a leading cube rather than
    /// yielding it eagerly.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnalysedPlayWithAnalysedCube_YieldsPlay_RegardlessOfRecordOrder(bool cubeFirst)
    {
        var file = BuildXgp(SyntheticMove.Analysed, cubeAnalysed: true, cubeFirst);

        AssertSingleDecision(file, "synthetic.xgp", expectCube: false);
    }

    // -----------------------------------------------------------------------
    //  Scope guard — .xg is untouched
    // -----------------------------------------------------------------------

    /// <summary>
    /// The same both-analysed record set read as an <c>.xg</c> match still
    /// yields two decisions. In a match file the cube and the play are distinct
    /// temporal decisions, discriminated by <c>XgDecisionId</c>'s within-file
    /// tuple; only <c>.xgp</c> collapses to one.
    /// </summary>
    [Theory]
    [InlineData("synthetic.xg")]
    [InlineData("synthetic.json")]
    public void AnalysedPlayWithAnalysedCube_NonXgpSource_YieldsBothDecisions(string sourceFile)
    {
        var file = BuildXgp(SyntheticMove.Analysed, cubeAnalysed: true, cubeFirst: true);

        var rows = XgDecisionIterator.Iterate(file, sourceFile).ToList();

        rows.Should().HaveCount(2, "the .xgp single-decision policy must not leak into match files");
        rows.Select(r => r.Id).Distinct().Should().HaveCount(2, "XgDecisionId carries within-file coordinates");
        XgDecisionIterator.IterateDiagramRequests(file, sourceFile).Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    //  Synthetic .xgp construction
    // -----------------------------------------------------------------------

    private static void AssertSingleDecision(XgFile file, string sourceFile, bool expectCube)
    {
        var row = XgDecisionIterator.Iterate(file, sourceFile).Should().ContainSingle().Subject;
        row.IsCube.Should().Be(expectCube);

        var request = XgDecisionIterator.IterateDiagramRequests(file, sourceFile).Should().ContainSingle().Subject;
        request.Decision.IsCube.Should().Be(expectCube);
    }

    /// <summary>
    /// The three move-pane shapes an <c>.xgp</c> can present to the emission
    /// policy: analysed and emittable, analysed but sentinel-only (dropped
    /// upstream), and carrying no analysis at all.
    /// </summary>
    private readonly record struct SyntheticMove(int MoveCount, sbyte[] Candidate)
    {
        /// <summary>24/23 — a benign single-checker move, no hits, no bear-offs.</summary>
        internal static SyntheticMove Analysed => new(1, [23, 22, -1, -1, -1, -1, -1, -1]);

        internal static SyntheticMove Unanalysed => new(0, []);

        internal static SyntheticMove Sentinel(sbyte from, sbyte to) =>
            new(1, [from, to, -1, -1, -1, -1, -1, -1]);
    }

    /// <summary>
    /// Builds an in-memory <c>.xgp</c>-shaped <see cref="XgFile"/>: a match
    /// header, a game header, one MoveRecord and one CubeRecord — the two panes
    /// XG writes for a position file.
    /// </summary>
    private static XgFile BuildXgp(SyntheticMove move, bool cubeAnalysed, bool cubeFirst)
    {
        var position = new PositionEngine { Points = BoardWithCheckerOn24() };

        bool analysed = move.MoveCount > 0;
        var moveRecord = new MoveRecord
        {
            EntryType = RecordType.Move,
            InitialPosition = position,
            FinalPosition = position,
            ActivePlayer = 1,
            Dice = [3, 1],
            CubeValue = 0,
            MoveError = -1000.0,               // unanalysed-error sentinel
            Analysis = new BestMoveAnalysis
            {
                MoveCount = move.MoveCount,
                Evals = analysed ? [new EvalResult { Equity = 0.0f }] : [],
                Moves = analysed ? [move.Candidate] : [],
                EvalLevels = analysed ? [new EvalLevel { Level = 1 }] : [],
                PositionsPlayed = analysed ? [position] : [],
            },
            RolloutIndices = [.. Enumerable.Repeat(-1, 32)],
        };

        var cubeRecord = new CubeRecord
        {
            EntryType = RecordType.Cube,
            ActivePlayer = 1,
            Position = position,
            CubeValue = 0,
            ErrorCube = -1000.0,
            ErrorTake = -1000.0,
            RolloutIndex = -1,
            Analysis = new DoubleActionAnalysis
            {
                // Gate is Level > 0. LevelRequest deliberately stays non-zero in
                // the unanalysed case — that is XG's "queued, never ran" shape.
                Level = cubeAnalysed ? 3 : -100,
                LevelRequest = 3,
                Position = position,
            },
        };

        var file = new XgFile
        {
            Records =
            {
                new MatchHeaderRecord { EntryType = RecordType.HeaderMatch, MatchLength = 7, Player1 = "P1", Player2 = "P2" },
                new GameHeaderRecord { EntryType = RecordType.HeaderGame, InitialPosition = new PositionEngine { Points = StandardOpening() } },
            },
        };

        if (cubeFirst)
        {
            file.Records.Add(cubeRecord);
            file.Records.Add(moveRecord);
        }
        else
        {
            file.Records.Add(moveRecord);
            file.Records.Add(cubeRecord);
        }
        return file;
    }

    private static sbyte[] BoardWithCheckerOn24()
    {
        var pts = new sbyte[26];
        pts[24] = 1;
        return pts;
    }

    private static sbyte[] StandardOpening()
    {
        var pts = new sbyte[26];
        pts[6]  = -5; pts[8]  = -3; pts[13] =  5; pts[24] = -2;
        pts[19] =  5; pts[17] =  3; pts[12] = -5; pts[1]  =  2;
        return pts;
    }
}
