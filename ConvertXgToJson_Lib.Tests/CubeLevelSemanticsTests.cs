using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins the cube level-semantics ruling of halheinrich/backgammon#161: a
/// cube depth label names the level that <em>ran</em>
/// (<c>DoubleActionAnalysis.Level</c>, the provenance of the emitted
/// equities), never the level the user <em>requested</em>
/// (<c>LevelRequest</c>, a setting). XG's governor serves a request at a
/// different level on 59% of analysed corpus cubes, in both directions, so
/// the divergent pane is an ordinary shape — see INSTRUCTIONS.md, "Level
/// semantics: <c>LevelRequest</c> vs <c>Level</c>".
///
/// <para>
/// The gating pins are synthetic (TestData rule: nothing gating may depend
/// on <c>TestData/</c>): <see cref="XgFileBuilder"/> synthesizes both
/// divergence directions, and a raw-record <see cref="XgFile"/> pins the
/// analysed-gate's <c>Level == 0</c> exclusion — the all-zero never-written
/// incidental pane, the corpus's entire unanalysed population. The
/// end-to-end counterpart against real XG bytes is the CI-excluded
/// <c>RequiresFixtureFiles</c> test at the bottom (the 3-ply Red
/// precedent).
/// </para>
/// </summary>
[Collection("FileIO")]
public class CubeLevelSemanticsTests
{
    private const string Xg = "synthetic.xg";

    private static List<DecisionRow> Rows(XgFile file) =>
        XgDecisionIterator.Iterate(file, Xg).ToList();

    private static List<BgDecisionData> Requests(XgFile file) =>
        XgDecisionIterator.IterateDiagramRequests(file, Xg).ToList();

    // -----------------------------------------------------------------------
    //  Divergence, both directions — the label follows what ran
    // -----------------------------------------------------------------------

    /// <summary>
    /// The corpus-dominant direction (9,348 of 16,067 analysed panes as of
    /// 2026-08-31): the user asked for 5-ply, XG's governor served the
    /// lopsided decision at 2-ply. The label must say 2-ply — the stored
    /// equities are 2-ply numbers.
    /// </summary>
    [Fact]
    public void CubeDecision_RequestedDeeperThanRan_LabelsFromRanLevel()
    {
        var builder = XgFileBuilder.ForMatch(5, "Alice", "Bob");
        builder.AddGame().CubeDecision(
            XgPlayer.Player1, new XgCubeEquities(-0.15, -0.62, 1.0),
            ply: 2, requestedPly: 5);
        var file = builder.Build();

        var row = Rows(file).Should().ContainSingle().Subject;
        row.AnalysisDepth.Should().Be("2-ply", "the equities came from a 2-ply evaluation");
        row.AnalysisMode.Should().Be(AnalysisMode.Evaluation);
        row.AnalysisLevel.Should().Be(AnalysisLevel.Ply2);

        var req = Requests(file).Should().ContainSingle().Subject;
        req.Decision.CubeDepth.Should().Be("2-ply");
        req.Decision.CubeAnalysisMode.Should().Be(AnalysisMode.Evaluation);
        req.Decision.CubeAnalysisLevel.Should().Be(AnalysisLevel.Ply2);
        req.Decision.CubeDepthRank.Should().Be(20);
    }

    /// <summary>
    /// The opposite direction (80 corpus panes ran XG Roller++ against a
    /// 3-ply request as of 2026-08-31 — "requested ≥ ran" is a wrong mental
    /// model): the user asked for 2-ply, XG deepened the close decision to
    /// 5-ply. The label must say 5-ply.
    /// </summary>
    [Fact]
    public void CubeDecision_RequestedShallowerThanRan_LabelsFromRanLevel()
    {
        var builder = XgFileBuilder.ForMatch(5, "Alice", "Bob");
        builder.AddGame().CubeDecision(
            XgPlayer.Player1, new XgCubeEquities(0.41, 0.43, 1.0),
            ply: 5, requestedPly: 2);
        var file = builder.Build();

        var row = Rows(file).Should().ContainSingle().Subject;
        row.AnalysisDepth.Should().Be("5-ply", "the equities came from a 5-ply evaluation");
        row.AnalysisMode.Should().Be(AnalysisMode.Evaluation);
        row.AnalysisLevel.Should().Be(AnalysisLevel.Ply5);

        var req = Requests(file).Should().ContainSingle().Subject;
        req.Decision.CubeDepth.Should().Be("5-ply");
        req.Decision.CubeAnalysisMode.Should().Be(AnalysisMode.Evaluation);
        req.Decision.CubeAnalysisLevel.Should().Be(AnalysisLevel.Ply5);
        req.Decision.CubeDepthRank.Should().Be(50);
    }

    /// <summary>
    /// The default keeps today's behaviour: no <c>requestedPly</c> means the
    /// request matches what ran, on the pane pair and on the record-level
    /// play-time stamp alike.
    /// </summary>
    [Fact]
    public void CubeDecision_NoRequestedPly_RequestMatchesRan()
    {
        var builder = XgFileBuilder.ForMatch(5, "Alice", "Bob");
        builder.AddGame().CubeDecision(
            XgPlayer.Player1, new XgCubeEquities(0.1, 0.2, 1.0), ply: 4);

        var cube = builder.Build().Records.OfType<CubeRecord>()
            .Should().ContainSingle().Subject;
        ((int)cube.Analysis.LevelRequest).Should().Be(cube.Analysis.Level);
        cube.AnalyzeLevelRequested.Should().Be(cube.AnalyzeLevel);
    }

    /// <summary>
    /// The synthesized record carries the divergence exactly as the corpus
    /// does: the pane pair diverges, and the record-level play-time stamp
    /// (<c>AnalyzeLevelRequested</c> / <c>AnalyzeLevel</c>) mirrors it —
    /// measured corpus shape for every ply-family pane
    /// (halheinrich/backgammon#161).
    /// </summary>
    [Fact]
    public void CubeDecision_DivergentLevels_RecordPairMirrorsPanePair()
    {
        var builder = XgFileBuilder.ForMatch(5, "Alice", "Bob");
        builder.AddGame().CubeDecision(
            XgPlayer.Player1, new XgCubeEquities(-0.15, -0.62, 1.0),
            ply: 2, requestedPly: 5);

        var cube = builder.Build().Records.OfType<CubeRecord>()
            .Should().ContainSingle().Subject;
        cube.Analysis.Level.Should().Be(1, "2-ply ran (PLAYERLEVEL code is ply − 1)");
        cube.Analysis.LevelRequest.Should().Be(4, "5-ply was requested");
        cube.AnalyzeLevel.Should().Be(1);
        cube.AnalyzeLevelRequested.Should().Be(4);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void CubeDecision_RequestedPlyOutOfRange_Throws(int requestedPly)
    {
        var game = XgFileBuilder.ForMatch(5, "Alice", "Bob").AddGame();
        var act = () => game.CubeDecision(
            XgPlayer.Player1, new XgCubeEquities(0.1, 0.2, 1.0),
            ply: 3, requestedPly: requestedPly);

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("requestedPly");
    }

    // -----------------------------------------------------------------------
    //  The analysed gate — Level == 0 is the never-written pane
    // -----------------------------------------------------------------------

    /// <summary>
    /// The gate's entire corpus exclusion set is the all-zero never-written
    /// incidental pane: <c>Level == 0</c>, <c>LevelRequest == 0</c>, zero
    /// equities, <c>IsBeaver == −100</c>, record pair <c>−1/−1</c> (23,036
    /// of 23,036 gated-out records as of 2026-08-31). Neither surface may
    /// emit it.
    /// </summary>
    [Fact]
    public void Iterate_LevelZeroNeverWrittenPane_IsNotEmitted()
    {
        var file = FileWithSingleCube(new DoubleActionAnalysis
        {
            // All-zero defaults except the never-analysed IsBeaver sentinel:
            // Level == 0, LevelRequest == 0, zero equity vectors — the
            // Doubled == −2 incidental pane XG writes beside a checker play.
            IsBeaver = -100,
        });

        Rows(file).Should().BeEmpty(
            "a Level == 0 pane is structurally empty — nothing ran");
        Requests(file).Should().BeEmpty();
    }

    /// <summary>
    /// Contrast pin for the gate: an otherwise-identical pane whose
    /// <c>Level</c> is positive emits — the predicate keys on Level alone,
    /// not on the request (which stays zero here).
    /// </summary>
    [Fact]
    public void Iterate_PositiveLevelWithZeroRequest_IsEmitted()
    {
        var file = FileWithSingleCube(new DoubleActionAnalysis
        {
            Level = 1,
            EquityNoDouble = -0.1f,
            EquityDoubleTake = -0.6f,
            EquityDoubleDrop = 1.0f,
        });

        var row = Rows(file).Should().ContainSingle().Subject;
        row.AnalysisDepth.Should().Be("2-ply", "level code 1 ran, and the label names what ran");
    }

    private static XgFile FileWithSingleCube(DoubleActionAnalysis analysis)
    {
        var position = new PositionEngine { Points = StandardOpening() };
        return new XgFile
        {
            Records =
            {
                new MatchHeaderRecord { EntryType = RecordType.HeaderMatch, MatchLength = 5, Player1 = "P1", Player2 = "P2" },
                new GameHeaderRecord { EntryType = RecordType.HeaderGame, InitialPosition = position },
                new CubeRecord
                {
                    EntryType = RecordType.Cube,
                    ActivePlayer = 1,
                    Doubled = -2,
                    Taken = -1,
                    Position = position,
                    CubeValue = 0,
                    ErrorCube = -1000.0,
                    ErrorTake = -1000.0,
                    RolloutIndex = -1,
                    AnalyzeLevel = -1,
                    AnalyzeLevelRequested = -1,
                    CommentIndex = -1,
                    Analysis = analysis,
                },
            },
        };
    }

    private static sbyte[] StandardOpening()
    {
        var pts = new sbyte[26];
        pts[6]  = -5; pts[8]  = -3; pts[13] =  5; pts[24] = -2;
        pts[19] =  5; pts[17] =  3; pts[12] = -5; pts[1]  =  2;
        return pts;
    }

    // -----------------------------------------------------------------------
    //  Real XG bytes — local-only fixture pin (the 3-ply Red precedent)
    // -----------------------------------------------------------------------

    /// <summary>
    /// End-to-end counterpart against a real XG match file. The
    /// 2026-07-21 fixture's first emitted cube (record cube #2: dice 31,
    /// requested 5-ply, ran 2-ply) must label 2-ply after the
    /// halheinrich/backgammon#161 fix; its second (cube #3: dice 52, ran =
    /// requested = 5-ply) still labels 5-ply. Identity is anchored on the
    /// cubeful equities, byte-verified in the #161 probe (cube #2:
    /// ND −0.1533 / DT −0.6208 / DP +1.0000).
    ///
    /// <para>
    /// Local-only by the TestData rule: the fixture lives in the gitignored
    /// <c>TestData/FixtureFiles/</c>, so this test carries the CI-excluded
    /// <c>RequiresFixtureFiles</c> trait and nothing gating depends on it.
    /// The gating coverage for both divergence directions is the synthetic
    /// builder pair above.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixtureFiles")]
    public void Fixture20260721_DivergentCubeLabelsRanLevel_ConvergentCubeUnchanged()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "gobetzu-XG Roller++ 2026-07-21.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on the 2026-07-21 gobetzu fixture in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        var cubeRows = XgDecisionIterator.Iterate(file, Path.GetFileName(path))
            .Where(r => r.IsCube)
            .Take(2)
            .ToList();

        cubeRows.Should().HaveCount(2);

        // Cube #2 — the file's first analysed cube (its record cube #1 is a
        // never-written Level == 0 incidental pane, gated out).
        cubeRows[0].Equity.Should().BeApproximately(-0.1533, 0.0001,
            "identity anchor: cube #2's no-double equity as byte-verified in the probe");
        cubeRows[0].AnalysisDepth.Should().Be("2-ply",
            "XG served the 5-ply request at 2-ply; the label names what ran");
        cubeRows[0].AnalysisLevel.Should().Be(AnalysisLevel.Ply2);

        // Cube #3 — ran at the requested level; the fix changes nothing.
        cubeRows[1].Equity.Should().BeApproximately(0.0497, 0.0001,
            "identity anchor: cube #3's no-double equity");
        cubeRows[1].AnalysisDepth.Should().Be("5-ply");
        cubeRows[1].AnalysisLevel.Should().Be(AnalysisLevel.Ply5);
    }
}
