using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Slice-export tests: <see cref="XgpExporter"/>'s second surface, taking
/// the parsed source <see cref="XgFile"/> plus the decision coordinates and
/// carrying the source analysis through. Unlike the clean-position path
/// (see <see cref="XgpExporterTests"/>), a sliced analyzed decision is
/// <b>visible</b> to our own iterator — exactly one decision — because the
/// analysis panes travel with it. Field-level agreement with XG's own
/// save-from-match output is pinned separately in
/// <see cref="XgpExportXgAgreementTests"/>.
/// </summary>
[Collection("FileIO")]
public class XgpSliceExportTests
{
    private static string Fixture(string name) => Path.Combine(TestPaths.FixtureFilesDir, name);

    // -----------------------------------------------------------------------
    //  Iterator visibility — the carve-out from the clean path's
    //  zero-rows-by-design rule: sliced analyzed decisions emit one row.
    // -----------------------------------------------------------------------

    [Fact]
    public void SlicedPlayDecision_YieldsExactlyOneDecision_MatchingTheSource()
    {
        var source = XgFileReader.ReadFile(Fixture("MTCH4064.xg"));
        var original = XgDecisionIterator
            .IterateDiagramRequests(source, "MTCH4064.xg")
            .Single(d => d.Descriptive.Game == 1 && d.Descriptive.MoveNumber == 22 && !d.IsCube);

        using var ms = new MemoryStream(XgpExporter.ToBytes(source, game: 1, moveNumber: 22, isCube: false));
        var sliced = XgFileReader.ReadStream(ms);
        var reRead = XgDecisionIterator.IterateDiagramRequests(sliced, "slice.xgp").Single();

        reRead.IsCube.Should().BeFalse();
        reRead.Xgid.Should().Be(original.Xgid, "the XGID digests position, cube, dice, and match state");
        reRead.Position.Mop.Should().Equal(original.Position.Mop);
        reRead.Decision.Dice.Should().Equal(original.Decision.Dice);
        reRead.Decision.Plays.Should().NotBeEmpty("the analysis panes travel with the slice");
        reRead.Decision.Plays.Count.Should().Be(original.Decision.Plays.Count);
        reRead.Decision.Plays[0].Equity.Should().Be(original.Decision.Plays[0].Equity);
        reRead.Decision.Plays[0].Depth.Should().Be(original.Decision.Plays[0].Depth);
        reRead.Decision.UserPlayError.Should().Be(original.Decision.UserPlayError);
        reRead.Descriptive.OnRollName.Should().Be(original.Descriptive.OnRollName);
    }

    [Fact]
    public void SlicedRolledOutCubeDecision_CarriesRolloutDepthThrough()
    {
        // match35253054.xg g2 m37 is the rolled-out cube decision whose
        // XG-authored save is also pinned (see XgpExportXgAgreementTests).
        var source = XgFileReader.ReadFile(Fixture("match35253054.xg"));
        var original = XgDecisionIterator
            .IterateDiagramRequests(source, "match35253054.xg")
            .Single(d => d.Descriptive.Game == 2 && d.Descriptive.MoveNumber == 37 && d.IsCube);
        original.Decision.CubeDepth.Should().StartWith("Rollout:",
            "this fixture decision is pinned as rolled out");

        using var ms = new MemoryStream(XgpExporter.ToBytes(source, game: 2, moveNumber: 37, isCube: true));
        var sliced = XgFileReader.ReadStream(ms);
        var reRead = XgDecisionIterator.IterateDiagramRequests(sliced, "slice.xgp").Single();

        reRead.IsCube.Should().BeTrue();
        reRead.Xgid.Should().Be(original.Xgid);
        reRead.Decision.CubeDepth.Should().Be(original.Decision.CubeDepth,
            "the referenced rollout contexts are carried and the index remapped");
        reRead.Decision.NoDoubleEquity.Should().Be(original.Decision.NoDoubleEquity);
        reRead.Decision.DoubleTakeEquity.Should().Be(original.Decision.DoubleTakeEquity);
    }

    // -----------------------------------------------------------------------
    //  Rollout remapping — synthetic sources with distinguishable contexts
    // -----------------------------------------------------------------------

    private static RolloutContext Context(int marker) => new() { GamesRolled = 1000 + marker };

    private static XgFile SyntheticSource(CubeRecord? cube, MoveRecord? move, int rolloutCount)
    {
        var records = new List<SaveRecord>
        {
            new MatchHeaderRecord { EntryType = RecordType.HeaderMatch, MatchLength = 7 },
            new GameHeaderRecord { EntryType = RecordType.HeaderGame, Score1 = 1, Score2 = 2 },
        };
        if (cube != null) records.Add(cube);
        if (move != null) records.Add(move);
        return new XgFile
        {
            Records = records,
            Rollouts = [.. Enumerable.Range(0, rolloutCount).Select(Context)],
        };
    }

    [Fact]
    public void MoveSlice_CarriesReferencedContexts_InFirstAppearanceOrder()
    {
        int[] indices = [.. Enumerable.Repeat(-1, 32)];
        indices[1] = 5;
        indices[2] = 3;
        indices[4] = 5; // repeat — must map to the same carried context
        var move = new MoveRecord
        {
            EntryType = RecordType.Move,
            Dice = [6, 2],
            RolloutIndices = indices,
            CommentIndex = 9,
        };

        var sliced = XgpExporter.ToXgFileSlice(SyntheticSource(cube: null, move, rolloutCount: 6),
            game: 1, moveNumber: 1, isCube: false);

        sliced.Rollouts.Select(r => r.GamesRolled).Should().Equal(new[] { 1005, 1003 },
            "only referenced contexts are carried, in first-appearance order");
        var slicedMove = sliced.Records.OfType<MoveRecord>().Single();
        slicedMove.RolloutIndices[1].Should().Be(0);
        slicedMove.RolloutIndices[2].Should().Be(1);
        slicedMove.RolloutIndices[4].Should().Be(0);
        slicedMove.RolloutIndices.Where((_, i) => i is not (1 or 2 or 4)).Should().OnlyContain(x => x == -1);
        slicedMove.CommentIndex.Should().Be(-1, "comments are not carried in a slice");
    }

    [Fact]
    public void CubeSlice_CarriesTheAdjacentContextPair_PointingAtTheSecondLeg()
    {
        // XG stores a cube rollout as an adjacent context pair with the
        // record pointing at the second leg (match35253054_2_37 ground
        // truth). The slice must carry the companion leg too.
        var cube = new CubeRecord { EntryType = RecordType.Cube, RolloutIndex = 4, CommentIndex = 7 };

        var sliced = XgpExporter.ToXgFileSlice(SyntheticSource(cube, move: null, rolloutCount: 6),
            game: 1, moveNumber: 1, isCube: true);

        sliced.Rollouts.Select(r => r.GamesRolled).Should().Equal(1003, 1004);
        var slicedCube = sliced.Records.OfType<CubeRecord>().Single();
        slicedCube.RolloutIndex.Should().Be(1, "the referenced leg stays second in the pair");
        slicedCube.CommentIndex.Should().Be(-1);
    }

    [Fact]
    public void CubeSlice_AtTableStart_CarriesSingleContext()
    {
        var cube = new CubeRecord { EntryType = RecordType.Cube, RolloutIndex = 0 };

        var sliced = XgpExporter.ToXgFileSlice(SyntheticSource(cube, move: null, rolloutCount: 2),
            game: 1, moveNumber: 1, isCube: true);

        sliced.Rollouts.Select(r => r.GamesRolled).Should().Equal(1000);
        sliced.Records.OfType<CubeRecord>().Single().RolloutIndex.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    //  Record-set shape
    // -----------------------------------------------------------------------

    [Fact]
    public void PlaySlice_WithoutSameTurnCube_SynthesizesIncidentalCubePane()
    {
        var move = new MoveRecord
        {
            EntryType = RecordType.Move,
            ActivePlayer = -1,
            Dice = [5, 2],
            CubeValue = -1,
            InitialPosition = new PositionEngine { Points = [.. Enumerable.Repeat((sbyte)1, 26)] },
        };

        var sliced = XgpExporter.ToXgFileSlice(SyntheticSource(cube: null, move, rolloutCount: 0),
            game: 1, moveNumber: 1, isCube: false);

        sliced.Records.Select(r => r.EntryType).Should().Equal(
            RecordType.HeaderMatch, RecordType.HeaderGame, RecordType.Cube, RecordType.Move);

        var incidental = sliced.Records.OfType<CubeRecord>().Single();
        incidental.ActivePlayer.Should().Be(-1, "the slice preserves the source perspective");
        incidental.CubeValue.Should().Be(-1);
        incidental.Doubled.Should().Be(-2, "XG's incidental-pane marker beside a play decision");
        incidental.DiceRolled.Should().Be("52");
        incidental.Analysis.Level.Should().Be(-100, "the incidental pane is never analysed");
        incidental.Position.Points.Should().Equal(move.InitialPosition.Points);
    }

    [Fact]
    public void CubeSlice_EmitsNoMoveRecord()
    {
        var source = XgFileReader.ReadFile(Fixture("match35253054.xg"));
        var sliced = XgpExporter.ToXgFileSlice(source, game: 2, moveNumber: 37, isCube: true);
        sliced.Records.Select(r => r.EntryType).Should().Equal(
            RecordType.HeaderMatch, RecordType.HeaderGame, RecordType.Cube);
    }

    // -----------------------------------------------------------------------
    //  Validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Slice_Throws_WhenCoordinatesNotFound()
    {
        var source = XgFileReader.ReadFile(Fixture("MTCH4064.xg"));
        var act = () => XgpExporter.ToBytes(source, game: 99, moveNumber: 1, isCube: false);
        act.Should().Throw<ArgumentException>().WithMessage("*game 99*");
    }

    [Fact]
    public void Slice_Throws_WhenSourceHasNoMatchHeader()
    {
        var act = () => XgpExporter.ToBytes(new XgFile(), game: 1, moveNumber: 1, isCube: false);
        act.Should().Throw<ArgumentException>().WithMessage("*MatchHeaderRecord*");
    }
}
