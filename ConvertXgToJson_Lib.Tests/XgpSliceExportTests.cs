using BgDataTypes_Lib;
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

    private static XgFile SyntheticSource(
        CubeRecord? cube, MoveRecord? move, int rolloutCount, List<string>? comments = null)
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
            Comments = comments ?? [],
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
        slicedMove.CommentIndex.Should().Be(-1,
            "a source comment index with no table entry behind it is dangling and must clear, not carry");
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
        slicedCube.CommentIndex.Should().Be(-1, "the source has no comment table entry behind index 7");
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
    //  Comment carriage — the sliced decision records' comments travel into
    //  a fresh dense table, matching XG's own SaveAs of a commented move
    //  (CommentExported.xgp ground truth). Match- and game-level comments
    //  are not carried; their header/footer indices are cleared so nothing
    //  dangles into the rebuilt table.
    // -----------------------------------------------------------------------

    [Fact]
    public void SlicedCommentedMove_CarriesTheCommentThrough()
    {
        // CommentExported.xgp is XG's own SaveAs of a commented move, so it
        // is simultaneously the input and the shape oracle: one RTF entry,
        // the move pointing at it, the cube pane and all four match/game
        // header indices at -1, and the outer header's Comments mirror empty.
        var source = XgFileReader.ReadFile(Fixture("CommentExported.xgp"));
        source.Comments.Should().ContainSingle("fixture precondition: exactly one RTF comment")
            .Which.Should().StartWith(@"{\rtf", "XG stores decision comments as RTF");
        source.Records.OfType<MoveRecord>().Single().CommentIndex.Should().Be(0,
            "fixture precondition: the move carries the comment");

        using var ms = new MemoryStream(XgpExporter.ToBytes(source, game: 1, moveNumber: 1, isCube: false));
        var sliced = XgFileReader.ReadStream(ms);

        sliced.Comments.Should().Equal(source.Comments, "the RTF payload travels verbatim");
        sliced.Records.OfType<MoveRecord>().Single().CommentIndex.Should().Be(0,
            "the sole comment lands at index 0 of the rebuilt table");
        sliced.Records.OfType<CubeRecord>().Single().CommentIndex.Should().Be(-1);
        sliced.Header.Comments.Should().BeEmpty(
            "XG's own SaveAs leaves the outer-header mirror empty for a decision comment");
    }

    [Fact]
    public void PlaySlice_WithCubeAndMoveComments_BuildsDenseTableInRecordOrder()
    {
        // Source indices deliberately reversed and sparse: the slice's
        // table must come out dense and in record order (cube pane first),
        // with the unreferenced entry left behind.
        var cube = new CubeRecord { EntryType = RecordType.Cube, CommentIndex = 2 };
        var move = new MoveRecord { EntryType = RecordType.Move, Dice = [3, 1], CommentIndex = 0 };
        var source = SyntheticSource(cube, move, rolloutCount: 0,
            comments: [@"{\rtf move}", @"{\rtf unreferenced}", @"{\rtf cube}"]);

        var sliced = XgpExporter.ToXgFileSlice(source, game: 1, moveNumber: 1, isCube: false);

        sliced.Comments.Should().Equal(@"{\rtf cube}", @"{\rtf move}");
        sliced.Records.OfType<CubeRecord>().Single().CommentIndex.Should().Be(0);
        sliced.Records.OfType<MoveRecord>().Single().CommentIndex.Should().Be(1);
    }

    [Fact]
    public void Slice_ClearsMatchAndGameHeaderCommentIndices()
    {
        // Copied verbatim (as the rest of the headers are), these indices
        // would dangle into the slice's rebuilt comment table.
        var records = new List<SaveRecord>
        {
            new MatchHeaderRecord
            {
                EntryType = RecordType.HeaderMatch,
                MatchLength = 7,
                CommentHeaderMatchIndex = 0,
                CommentFooterMatchIndex = 1,
            },
            new GameHeaderRecord
            {
                EntryType = RecordType.HeaderGame,
                CommentHeaderGameIndex = 2,
                CommentFooterGameIndex = 3,
            },
            // CommentIndex = -1 as in a real parsed record (the model
            // default 0 would reference the table's first entry).
            new MoveRecord { EntryType = RecordType.Move, Dice = [3, 1], CommentIndex = -1 },
        };
        var source = new XgFile { Records = records, Comments = ["a", "b", "c", "d"] };

        var sliced = XgpExporter.ToXgFileSlice(source, game: 1, moveNumber: 1, isCube: false);

        var mh = (MatchHeaderRecord)sliced.Records[0];
        mh.CommentHeaderMatchIndex.Should().Be(-1);
        mh.CommentFooterMatchIndex.Should().Be(-1);
        var gh = sliced.Records.OfType<GameHeaderRecord>().Single();
        gh.CommentHeaderGameIndex.Should().Be(-1);
        gh.CommentFooterGameIndex.Should().Be(-1);
        sliced.Comments.Should().BeEmpty(
            "only decision-record comments are carried, and no decision record references one");

        // And through the container: a written slice with no comments emits
        // no temp.xgc, and the re-read table stays empty.
        using var ms = new MemoryStream(XgpExporter.ToBytes(source, game: 1, moveNumber: 1, isCube: false));
        XgFileReader.ReadStream(ms).Comments.Should().BeEmpty(
            "the commentless slice must re-read with an empty comment table");
    }

    [Fact]
    public void Slice_OfMatchCommentedSource_EmitsNoDanglingIndices()
    {
        // End-to-end run of the header-clearing rule against the XG-authored
        // fixture whose match header really points into the comment table.
        var source = XgFileReader.ReadFile(Fixture("CommentsAddedToXgp.xgp"));
        var srcMh = (MatchHeaderRecord)source.Records[0];
        srcMh.CommentHeaderMatchIndex.Should().Be(0, "fixture precondition: before-session comment");
        srcMh.CommentFooterMatchIndex.Should().Be(1, "fixture precondition: after-session comment");

        using var ms = new MemoryStream(XgpExporter.ToBytes(source, game: 1, moveNumber: 1, isCube: false));
        var sliced = XgFileReader.ReadStream(ms);

        var mh = (MatchHeaderRecord)sliced.Records[0];
        mh.CommentHeaderMatchIndex.Should().Be(-1, "match-level comments are not carried in a slice");
        mh.CommentFooterMatchIndex.Should().Be(-1);
        sliced.Comments.Should().BeEmpty(
            "this fixture's decision records carry no comments, so the re-read table is empty");
    }

    [Fact]
    public void CommentsAddedToXgp_ParseOracle_PinsWhyMatchCommentsStayOutOfScope()
    {
        // Parse oracle for the fixture itself: XG stores match-level
        // comments as RTF table entries referenced by the match header AND
        // mirrors the header comment as plain text into the outer header's
        // Comments field. Carrying match-level comments through a slice
        // would therefore require RTF→plain-text extraction — the reason
        // slice comment carriage stops at decision records.
        var file = XgFileReader.ReadFile(Fixture("CommentsAddedToXgp.xgp"));

        file.Comments.Should().HaveCount(2);
        file.Comments.Should().OnlyContain(c => c.StartsWith(@"{\rtf"));
        var mh = (MatchHeaderRecord)file.Records[0];
        mh.CommentHeaderMatchIndex.Should().Be(0);
        mh.CommentFooterMatchIndex.Should().Be(1);
        file.Header.Comments.Should().Be("Comments before session\r\n",
            "XG mirrors the match header comment into the outer header as plain text");
    }

    // -----------------------------------------------------------------------
    //  XgDecisionId addressing — the Id overloads destructure the same
    //  (Game, MoveNumber, IsCube) coordinates the bare surface takes, so
    //  their output must be byte-identical. Filename is not consulted.
    // -----------------------------------------------------------------------

    [Fact]
    public void IdOverload_PlayDecision_MatchesCoordinateOverloadByteForByte()
    {
        var source = XgFileReader.ReadFile(Fixture("MTCH4064.xg"));
        var id = new XgDecisionId("MTCH4064.xg", Game: 1, MoveNumber: 22, IsCube: false);

        XgpExporter.ToBytes(source, id).Should().Equal(
            XgpExporter.ToBytes(source, game: 1, moveNumber: 22, isCube: false));
    }

    [Fact]
    public void IdOverload_CubeDecision_MatchesCoordinateOverloadByteForByte()
    {
        var source = XgFileReader.ReadFile(Fixture("match35253054.xg"));
        var id = new XgDecisionId("match35253054.xg", Game: 2, MoveNumber: 37, IsCube: true);

        XgpExporter.ToBytes(source, id).Should().Equal(
            XgpExporter.ToBytes(source, game: 2, moveNumber: 37, isCube: true));
    }

    [Fact]
    public void IdOverload_WithOptions_MatchesCoordinateOverloadByteForByte()
    {
        var source = XgFileReader.ReadFile(Fixture("MTCH4064.xg"));
        var id = new XgDecisionId("MTCH4064.xg", Game: 1, MoveNumber: 22, IsCube: false);

        XgpExporter.ToBytes(source, id, XgpSliceOptions.Anonymized).Should().Equal(
            XgpExporter.ToBytes(source, game: 1, moveNumber: 22, isCube: false, XgpSliceOptions.Anonymized));
    }

    [Fact]
    public void IdOverload_NullId_Throws()
    {
        var source = XgFileReader.ReadFile(Fixture("MTCH4064.xg"));
        var act = () => XgpExporter.ToBytes(source, id: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    //  Player-name overrides (XgpSliceOptions)
    // -----------------------------------------------------------------------

    [Fact]
    public void AnonymizedSlice_RewritesNames_EverywhereNamesSurface()
    {
        var source = XgFileReader.ReadFile(Fixture("MTCH4064.xg"));
        var srcMh = (MatchHeaderRecord)source.Records[0];
        srcMh.Player1.Should().NotBe("Player 1",
            "fixture precondition: the source must carry real names for this test to prove anything");
        var original = XgDecisionIterator
            .IterateDiagramRequests(source, "MTCH4064.xg")
            .Single(d => d.Descriptive.Game == 1 && d.Descriptive.MoveNumber == 22 && !d.IsCube);

        using var ms = new MemoryStream(XgpExporter.ToBytes(
            source, game: 1, moveNumber: 22, isCube: false, XgpSliceOptions.Anonymized));
        var sliced = XgFileReader.ReadStream(ms);

        var mh = (MatchHeaderRecord)sliced.Records[0];
        mh.Player1.Should().Be("Player 1");
        mh.Player2.Should().Be("Player 2");
        mh.Player1Ansi.Should().Be("Player 1", "the XG1-compat twin is rewritten in step");
        mh.Player2Ansi.Should().Be("Player 2");

        var info = XgDecisionIterator.ExtractMatchInfo(sliced);
        info!.Player1.Should().Be("Player 1");
        info.Player2.Should().Be("Player 2");

        var reRead = XgDecisionIterator.IterateDiagramRequests(sliced, "slice.xgp").Single();
        bool onRollIsPlayer1 = original.Descriptive.OnRollName == srcMh.Player1;
        reRead.Descriptive.OnRollName.Should().Be(onRollIsPlayer1 ? "Player 1" : "Player 2");
        reRead.Descriptive.OpponentName.Should().Be(onRollIsPlayer1 ? "Player 2" : "Player 1");
        reRead.Xgid.Should().Be(original.Xgid, "names never participate in the XGID");
    }

    [Fact]
    public void SliceOptions_MatchingSourceNames_AreByteInvisible()
    {
        var source = XgFileReader.ReadFile(Fixture("MTCH4064.xg"));
        var srcMh = (MatchHeaderRecord)source.Records[0];
        // Fixture precondition: the ANSI twins mirror the Unicode names, so
        // an override equal to the source names must reproduce all four
        // fields — and therefore the whole file — exactly.
        srcMh.Player1Ansi.Should().Be(srcMh.Player1);
        srcMh.Player2Ansi.Should().Be(srcMh.Player2);

        var options = new XgpSliceOptions
        {
            Player1Name = srcMh.Player1,
            Player2Name = srcMh.Player2,
        };

        XgpExporter.ToBytes(source, game: 1, moveNumber: 22, isCube: false, options)
            .Should().Equal(XgpExporter.ToBytes(source, game: 1, moveNumber: 22, isCube: false),
                "the header copy may rewrite only the four name fields — any other " +
                "difference means a field was dropped or transposed in the copy");
    }

    [Fact]
    public void AnonymizedSlice_PreservesLocationProvenance()
    {
        var source = XgFileReader.ReadFile(Fixture("MTCH4064.xg"));
        var srcMh = (MatchHeaderRecord)source.Records[0];

        var sliced = XgpExporter.ToXgFileSlice(
            source, game: 1, moveNumber: 22, isCube: false, XgpSliceOptions.Anonymized);

        var mh = (MatchHeaderRecord)sliced.Records[0];
        mh.Location.Should().Be(srcMh.Location,
            "Location is the ecosystem's producer fingerprint and must survive anonymization");
        mh.LocationAnsi.Should().Be(srcMh.LocationAnsi);
        mh.Event.Should().Be(srcMh.Event);
        mh.Date.Should().Be(srcMh.Date);
    }

    [Fact]
    public void SliceOptions_PartialOverride_KeepsTheOtherPlayersNames()
    {
        var records = new List<SaveRecord>
        {
            new MatchHeaderRecord
            {
                EntryType = RecordType.HeaderMatch,
                MatchLength = 7,
                Player1 = "Alice", Player1Ansi = "Alice",
                Player2 = "Bob", Player2Ansi = "Bob",
            },
            new GameHeaderRecord { EntryType = RecordType.HeaderGame },
            new MoveRecord { EntryType = RecordType.Move, Dice = [3, 1] },
        };

        var sliced = XgpExporter.ToXgFileSlice(new XgFile { Records = records },
            game: 1, moveNumber: 1, isCube: false, new XgpSliceOptions { Player1Name = "Anon" });

        var mh = (MatchHeaderRecord)sliced.Records[0];
        mh.Player1.Should().Be("Anon");
        mh.Player1Ansi.Should().Be("Anon");
        mh.Player2.Should().Be("Bob", "an unset override keeps that player's source name");
        mh.Player2Ansi.Should().Be("Bob");
    }

    [Fact]
    public void SliceOptions_NonLatin1Override_DegradesOnlyTheAnsiTwin()
    {
        var source = XgFileReader.ReadFile(Fixture("MTCH4064.xg"));

        using var ms = new MemoryStream(XgpExporter.ToBytes(
            source, game: 1, moveNumber: 22, isCube: false,
            new XgpSliceOptions { Player1Name = "Bjørn 東京" }));
        var mh = (MatchHeaderRecord)XgFileReader.ReadStream(ms).Records[0];

        mh.Player1.Should().Be("Bjørn 東京", "the Unicode field carries the true name");
        mh.Player1Ansi.Should().Be("Bjørn ??",
            "characters outside Latin1 degrade to '?' in the XG1-compat twin (ø is Latin1 and survives)");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SliceOptions_WhitespaceOverride_ThrowsAtConstruction(string bad)
    {
        var act1 = () => new XgpSliceOptions { Player1Name = bad };
        act1.Should().Throw<ArgumentException>(
            "null means keep the source name; an explicit empty name is always a caller bug");
        var act2 = () => new XgpSliceOptions { Player2Name = bad };
        act2.Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    //  Anonymize-copy (whole-file re-emit with name overrides) — not a
    //  slice: every record, rollout context, and comment travels verbatim;
    //  only the match header's name fields are rewritten.
    // -----------------------------------------------------------------------

    [Fact]
    public void Copy_WithSourceNames_IsByteIdenticalToXgFileWriterOutput()
    {
        var source = XgFileReader.ReadFile(Fixture("MTCH4064_1_22.xgp"));
        var srcMh = (MatchHeaderRecord)source.Records[0];
        // Fixture precondition (see SliceOptions_MatchingSourceNames_AreByteInvisible).
        srcMh.Player1Ansi.Should().Be(srcMh.Player1);
        srcMh.Player2Ansi.Should().Be(srcMh.Player2);

        var expected = XgFileWriter.ToBytes(source);

        XgpExporter.ToBytes(source, new XgpSliceOptions
        {
            Player1Name = srcMh.Player1,
            Player2Name = srcMh.Player2,
        }).Should().Equal(expected,
            "a copy whose overrides equal the source names is a plain re-emit");

        XgpExporter.ToBytes(source, new XgpSliceOptions()).Should().Equal(expected,
            "a copy with no overrides is a plain re-emit");
    }

    [Fact]
    public void AnonymizedCopy_RoundTrips_NamesOverridden_AnalysisAndRolloutsPreserved()
    {
        // The rolled-out cube save: two rollout contexts (the adjacent
        // pair), so depth survival proves the rollout table travels.
        var source = XgFileReader.ReadFile(Fixture("match35253054_2_37.xgp"));
        var srcMh = (MatchHeaderRecord)source.Records[0];
        var original = XgDecisionIterator
            .IterateDiagramRequests(source, "match35253054_2_37.xgp")
            .Single();

        using var ms = new MemoryStream(XgpExporter.ToBytes(source, XgpSliceOptions.Anonymized));
        var copy = XgFileReader.ReadStream(ms);

        var mh = (MatchHeaderRecord)copy.Records[0];
        mh.Player1.Should().Be("Player 1");
        mh.Player2.Should().Be("Player 2");
        mh.Player1Ansi.Should().Be("Player 1");
        mh.Player2Ansi.Should().Be("Player 2");
        mh.Location.Should().Be(srcMh.Location, "provenance survives an anonymize-copy");
        copy.Records.Count.Should().Be(source.Records.Count, "a copy selects nothing out");
        copy.Rollouts.Count.Should().Be(source.Rollouts.Count);

        var reRead = XgDecisionIterator.IterateDiagramRequests(copy, "copy.xgp").Single();
        reRead.IsCube.Should().BeTrue();
        reRead.Xgid.Should().Be(original.Xgid, "names never participate in the XGID");
        reRead.Decision.CubeDepth.Should().Be(original.Decision.CubeDepth,
            "the rollout table travels verbatim — no remapping, no dropped legs");
        reRead.Decision.NoDoubleEquity.Should().Be(original.Decision.NoDoubleEquity);
        reRead.Decision.DoubleTakeEquity.Should().Be(original.Decision.DoubleTakeEquity);
    }

    [Fact]
    public void AnonymizedCopy_PreservesComments()
    {
        // The slice path rebuilds the comment table (dense, decision
        // comments only); the copy path must carry table and indices verbatim.
        var source = new XgFile
        {
            Records =
            [
                new MatchHeaderRecord
                {
                    EntryType = RecordType.HeaderMatch,
                    MatchLength = 7,
                    Player1 = "Alice", Player1Ansi = "Alice",
                    Player2 = "Bob", Player2Ansi = "Bob",
                },
                new GameHeaderRecord { EntryType = RecordType.HeaderGame },
                new MoveRecord { EntryType = RecordType.Move, Dice = [3, 1], CommentIndex = 0 },
            ],
            Comments = ["anonymize copy comment"],
        };

        using var ms = new MemoryStream(XgpExporter.ToBytes(source, XgpSliceOptions.Anonymized));
        var copy = XgFileReader.ReadStream(ms);

        ((MatchHeaderRecord)copy.Records[0]).Player1.Should().Be("Player 1");
        ((MatchHeaderRecord)copy.Records[0]).Player2.Should().Be("Player 2");
        copy.Comments.Should().Equal("anonymize copy comment");
        copy.Records.OfType<MoveRecord>().Single().CommentIndex.Should().Be(0,
            "the copy path keeps comment indices verbatim — remapping is slice behavior");
    }

    [Fact]
    public void Copy_Throws_WhenSourceHasNoMatchHeader()
    {
        var act = () => XgpExporter.ToBytes(new XgFile(), XgpSliceOptions.Anonymized);
        act.Should().Throw<ArgumentException>().WithMessage("*MatchHeaderRecord*");
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
