using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// <see cref="XgpExporter"/> <b>clean-position path</b> tests: decision →
/// .xgp bytes → re-read through our own reader, asserting the record set
/// XG will see.
///
/// <para>
/// The end-to-end oracle is record-level, plus one deliberate iterator
/// assertion: a clean export yields <b>zero</b> decisions from
/// <see cref="XgDecisionIterator"/>. That is <b>by design</b>, not a bug —
/// clean exports are unanalyzed positions (XG re-analyzes on import), and
/// rule 1 of the .xgp emission policy ("skip unanalysed") makes them
/// invisible to this ecosystem's own iterator. The clean path is
/// XG-import-only; the ecosystem's re-ingestible format remains
/// BgDecisionData JSON. Do not "fix" the zero-rows assertions to expect one
/// row — analysis carry-through exists, but it is the <b>slice-export</b>
/// surface (source XgFile + decision coordinates; see
/// <see cref="XgpSliceExportTests"/>), not a change to this path.
/// </para>
/// </summary>
public class XgpExporterTests
{
    // -----------------------------------------------------------------------
    //  Test decisions
    // -----------------------------------------------------------------------

    /// <summary>A mid-game position, on-roll perspective.</summary>
    private static readonly BoardPosition SampleBoard = new(
        [0, -1, 0, 0, 0, 0, 5, 2, 3, 0, 0, 0, -6, 3, 0, 0, 0, -2, 0, -4, -2, 1, 0, 0, 1, 0]);

    /// <summary>
    /// A checker play at <paramref name="position"/> with <paramref name="dice"/>;
    /// its one candidate is the pass, valid from every position — the clean
    /// export writes no analysis, so the candidates never reach the file.
    /// </summary>
    private static CheckerPlayDecision PlayAt(PositionData position, int[] dice, string file = "export.xgp",
        DescriptiveData? descriptive = null) =>
        TestRecords.CheckerPlay(
            id: new XgpDecisionId(file),
            position: position,
            decision: TestRecords.CheckerPlayData(dice: dice, plays: [TestRecords.Candidate(play: [])]),
            descriptive: descriptive);

    private static CubeDecision CubeAt(PositionData position, string file = "cube.xgp", DescriptiveData? descriptive = null) =>
        TestRecords.Cube(id: new XgpDecisionId(file), position: position, descriptive: descriptive);

    private static BgDecisionData MoneyPlayDecision() => PlayAt(
        TestRecords.Position(mop: SampleBoard, session: TestRecords.MoneySession(isJacoby: true)),
        dice: [6, 5],
        descriptive: TestRecords.Descriptive(
            onRollName: "Hero", opponentName: "Villain", date: new DateOnly(2026, 7, 11), isStandardStart: null));

    private static BgDecisionData MatchCubeDecision() => CubeAt(
        TestRecords.Position(
            mop: SampleBoard, cubeSize: 2, cubeOwner: CubeOwner.OnRoll,
            session: TestRecords.MatchSession(length: 13, onRollNeeds: 6, opponentNeeds: 7)),
        descriptive: TestRecords.Descriptive(onRollName: "Joe", opponentName: "Bob", isStandardStart: null));

    private static XgFile Export(BgDecisionData decision)
    {
        using var ms = new MemoryStream(XgpExporter.ToBytes(decision));
        return XgFileReader.ReadStream(ms);
    }

    // -----------------------------------------------------------------------
    //  Record shape
    // -----------------------------------------------------------------------

    [Fact]
    public void PlayDecision_ExportsMatchHeaderGameHeaderCubeAndMove()
    {
        var file = Export(MoneyPlayDecision());
        file.Records.Select(r => r.EntryType).Should().Equal(
            RecordType.HeaderMatch, RecordType.HeaderGame, RecordType.Cube, RecordType.Move);
    }

    [Fact]
    public void CubeDecision_ExportsCubeRecordButNoMoveRecord()
    {
        var file = Export(MatchCubeDecision());
        file.Records.Select(r => r.EntryType).Should().Equal(
            RecordType.HeaderMatch, RecordType.HeaderGame, RecordType.Cube);
    }

    // -----------------------------------------------------------------------
    //  Money-game header + position content
    // -----------------------------------------------------------------------

    [Fact]
    public void MoneyPlay_WritesXgMoneyConventions()
    {
        var file = Export(MoneyPlayDecision());

        var mh = file.Records[0].Should().BeOfType<MatchHeaderRecord>().Subject;
        mh.MatchLength.Should().Be(99999, "XG's money sentinel");
        mh.Player1.Should().Be("Hero", "on-roll player is written as player 1");
        mh.Player2.Should().Be("Villain");
        mh.Player1Ansi.Should().Be("Hero");
        mh.Jacoby.Should().BeTrue("the money session's terms state the Jacoby rule");
        mh.Beaver.Should().BeFalse();
        mh.CubeLimit.Should().Be(10, "the terms' cube limit, 1024, as XG's exponent");
        mh.Version.Should().Be(30);
        mh.Date.Should().Be(new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc));

        var gh = file.Records[1].Should().BeOfType<GameHeaderRecord>().Subject;
        gh.Score1.Should().Be(0);
        gh.Score2.Should().Be(0);
        gh.CrawfordApplies.Should().BeFalse();
        gh.GameNumber.Should().Be(1);
        gh.InProgress.Should().BeTrue();
        gh.InitialPosition.ToBoardPosition().Should().Be(SampleBoard,
            "XG's position-editor pattern: the game starts at the saved position");

        var cube = file.Records[2].Should().BeOfType<CubeRecord>().Subject;
        cube.ActivePlayer.Should().Be(1);
        cube.Position.ToBoardPosition().Should().Be(SampleBoard);
        cube.CubeValue.Should().Be(0, "centred 1-cube");
        cube.DiceRolled.Should().Be("65", "a play decision carries its real roll in the cube pane");

        var move = file.Records[3].Should().BeOfType<MoveRecord>().Subject;
        move.Dice.Should().Equal(6, 5);
        move.ActivePlayer.Should().Be(1);
        move.InitialPosition.ToBoardPosition().Should().Be(SampleBoard);
        move.FinalPosition.Points.Should().OnlyContain(p => p == 0, "no play has been made");
    }

    [Fact]
    public void MatchCube_WritesScoresCrawfordAndCubeOwnership()
    {
        var decision = MatchCubeDecision();
        var file = Export(decision);

        var mh = file.Records[0].Should().BeOfType<MatchHeaderRecord>().Subject;
        mh.MatchLength.Should().Be(13);
        mh.Jacoby.Should().BeFalse("Jacoby is a money-game rule");
        mh.Crawford.Should().BeTrue("the match-play rule flag is on, mirroring XG");
        mh.CubeLimit.Should().Be(10, "a match's terms state no limit, so the header writes XG's default exponent");

        var gh = file.Records[1].Should().BeOfType<GameHeaderRecord>().Subject;
        gh.Score1.Should().Be(7, "score = the length less the on-roll player's away score");
        gh.Score2.Should().Be(6);

        var cube = file.Records[2].Should().BeOfType<CubeRecord>().Subject;
        cube.CubeValue.Should().Be(1, "+log2(2): the on-roll player (player 1) owns a 2-cube");
        cube.DiceRolled.Should().Be("11", "XG's placeholder for a pre-roll cube position");
    }

    [Fact]
    public void CrawfordGame_SetsGameHeaderCrawfordApplies()
    {
        var decision = PlayAt(
            TestRecords.Position(mop: SampleBoard,
                session: TestRecords.MatchSession(length: 7, onRollNeeds: 1, opponentNeeds: 5, isCrawford: true)),
            dice: [3, 1], file: "crawford.xgp");

        var file = Export(decision);
        file.Records[1].Should().BeOfType<GameHeaderRecord>()
            .Which.CrawfordApplies.Should().BeTrue();
    }

    [Theory]
    [InlineData(CubeOwner.Centered, 1, 0)]
    [InlineData(CubeOwner.OnRoll, 4, 2)]
    [InlineData(CubeOwner.Opponent, 2, -1)]
    [InlineData(CubeOwner.Opponent, 8, -3)]
    public void CubeEncoding_IsSignedLog2(CubeOwner owner, int size, int expectedRaw)
    {
        var decision = CubeAt(TestRecords.Position(
            mop: SampleBoard, cubeSize: size, cubeOwner: owner,
            session: TestRecords.MatchSession(length: 11, onRollNeeds: 5, opponentNeeds: 5)), file: "cube-enc.xgp");

        Export(decision).Records[2].Should().BeOfType<CubeRecord>()
            .Which.CubeValue.Should().Be(expectedRaw);
    }

    /// <summary>
    /// A money session's terms are written as the header's: its Jacoby and
    /// beaver rules, and its cube limit as XG's exponent — the inverse of the
    /// reader's projection, so each round-trips (the XGID the exporter once
    /// parsed them from is derived now, and carries nothing a record lacks).
    /// </summary>
    [Theory]
    [InlineData(true, false, 1024, 10)]
    [InlineData(false, true, 64, 6)]
    [InlineData(true, true, 8, 3)]
    public void MoneyTerms_AreWrittenAsTheHeaders(bool jacoby, bool beaver, int cubeLimit, int exponent)
    {
        var decision = CubeAt(TestRecords.Position(
            mop: SampleBoard,
            session: TestRecords.MoneySession(isJacoby: jacoby, isBeaver: beaver, cubeLimit: cubeLimit)), file: "terms.xgp");

        var file = Export(decision);
        var mh = file.Records[0].Should().BeOfType<MatchHeaderRecord>().Subject;
        mh.MatchLength.Should().Be(MatchHeaderRecord.MoneyMatchLengthSentinel);
        mh.Jacoby.Should().Be(jacoby);
        mh.Beaver.Should().Be(beaver);
        mh.CubeLimit.Should().Be(exponent);
        XgMatchInfo.From(mh).Terms.Should().Be(((MoneySession)decision.Session).Terms, "the reader reads back the terms written");
    }

    /// <summary>
    /// A money session's scores are the game header's, the player on roll's
    /// as player 1's — the session's standing, never the zeros a money
    /// stand-in once wrote.
    /// </summary>
    [Fact]
    public void MoneyScores_AreWrittenAsTheGameHeaders()
    {
        var decision = CubeAt(TestRecords.Position(
            mop: SampleBoard, session: TestRecords.MoneySession(onRollScore: 3, opponentScore: 1)), file: "scores.xgp");

        var gh = Export(decision).Records[1].Should().BeOfType<GameHeaderRecord>().Subject;
        gh.Score1.Should().Be(3);
        gh.Score2.Should().Be(1);
        gh.CrawfordApplies.Should().BeFalse();
    }

    /// <summary>A record that names no player is written with XG's default names.</summary>
    [Fact]
    public void UnnamedPlayers_AreWrittenAsXgsDefaults()
    {
        var decision = CubeAt(TestRecords.Position(mop: SampleBoard), file: "unnamed.xgp",
            descriptive: TestRecords.Descriptive(onRollName: null, opponentName: null, isStandardStart: null));

        var mh = Export(decision).Records[0].Should().BeOfType<MatchHeaderRecord>().Subject;
        mh.Player1.Should().Be("Player 1");
        mh.Player2.Should().Be("Player 2");
    }

    // -----------------------------------------------------------------------
    //  Unanalyzed sentinels — the clean-export contract
    // -----------------------------------------------------------------------

    [Fact]
    public void Export_WritesXgUnanalysedSentinels()
    {
        var file = Export(MoneyPlayDecision());

        var cube = file.Records[2].Should().BeOfType<CubeRecord>().Subject;
        cube.Analysis.Level.Should().Be(-100, "XG's 'never analysed' level sentinel");
        cube.Analysis.LevelRequest.Should().Be(0);
        cube.Analysis.IsBeaver.Should().Be(-100);
        cube.ErrorCube.Should().Be(-1000);
        cube.ErrorTake.Should().Be(-1000);
        cube.AnalyzeLevel.Should().Be(-1);
        cube.AnalyzeLevelRequested.Should().Be(-1);
        cube.RolloutIndex.Should().Be(-1);
        cube.CommentIndex.Should().Be(-1);

        var move = file.Records[3].Should().BeOfType<MoveRecord>().Subject;
        move.Analysis.Level.Should().Be(-100);
        move.Analysis.MoveCount.Should().Be(0);
        move.MoveError.Should().Be(-1000);
        move.Played.Should().BeFalse();
        move.RolloutIndices.Should().OnlyContain(i => i == -1);
        move.AnalyzeLevel.Should().Be(-1);
        move.CommentIndex.Should().Be(-1);
    }

    // -----------------------------------------------------------------------
    //  Iterator visibility — zero rows BY DESIGN (XG-import-only exports)
    // -----------------------------------------------------------------------

    [Fact]
    public void ExportedPlayDecision_YieldsZeroDecisions_ByDesign()
    {
        // Rule 1 of the .xgp emission policy: unanalysed decisions are
        // skipped. A clean export is unanalysed by definition, so it is
        // invisible to our own iterator — the exported file is for real XG
        // (which re-analyzes on import), not for re-ingestion here. This
        // assertion pins the intended boundary; see the class remarks
        // before "fixing" it to expect one row.
        var file = Export(MoneyPlayDecision());
        XgDecisionIterator.IterateDiagramRequests(file, "export.xgp").Should().BeEmpty();
        XgDecisionIterator.Iterate(file, "export.xgp").Should().BeEmpty();
    }

    [Fact]
    public void ExportedCubeDecision_YieldsZeroDecisions_ByDesign()
    {
        var file = Export(MatchCubeDecision());
        XgDecisionIterator.IterateDiagramRequests(file, "cube.xgp").Should().BeEmpty();
        XgDecisionIterator.Iterate(file, "cube.xgp").Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    //  Determinism + header
    // -----------------------------------------------------------------------

    [Fact]
    public void Export_IsByteDeterministic()
    {
        var decision = MoneyPlayDecision();
        XgpExporter.ToBytes(decision).Should().Equal(XgpExporter.ToBytes(decision),
            "no timestamps or random ids may leak into the output");
    }

    [Fact]
    public void Export_SelfIdentifiesInLocation()
    {
        // Location is the ecosystem's producer fingerprint (Galaxy writes
        // "BackgammonGalaxy" there; IsGalaxyMoneyGame keys on it). Exports
        // self-identify rather than mimic XG's "eXtreme Gammon" — this is
        // the stable hook for ever special-casing our own exports, so treat
        // a change to the string as a breaking change to provenance.
        var mh = Export(MoneyPlayDecision()).Records[0].Should().BeOfType<MatchHeaderRecord>().Subject;
        mh.LocationAnsi.Should().Be("ConvertXgToJson_Lib");
        mh.Location.Should().Be("ConvertXgToJson_Lib");
    }

    [Fact]
    public void Export_WritesXgpHeaderConstantsAndSaveName()
    {
        var money = Export(MoneyPlayDecision());
        money.Header.GameId.Should().Be(new Guid("2f5af5e1-e021-4832-a423-ef480ec58a0b"),
            "XG stamps this constant GUID into every .xgp");
        money.Header.SaveName.Should().Be("Position:  Unlimited Game, Jacoby");

        var match = Export(MatchCubeDecision());
        match.Header.SaveName.Should().Be("Position: 13 point match 7-6");
    }

    [Fact]
    public void Export_EdgePositions_RoundTripThroughReader()
    {
        // A checker on the on-roll bar and men borne off (fewer than 15 a side).
        var barsAndBearoff = new BoardPosition(
            [0, -2, 0, 0, 0, 0, 3, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -3, -4, 0, 2, 0, 0, 0, 2]);

        var decision = PlayAt(TestRecords.Position(mop: barsAndBearoff), dice: [2, 2], file: "edge.xgp");

        var file = Export(decision);
        file.Records[2].Should().BeOfType<CubeRecord>()
            .Which.Position.ToBoardPosition().Should().Be(barsAndBearoff);
    }

    // -----------------------------------------------------------------------
    //  Validation — the one requirement a well-formed record can fail
    // -----------------------------------------------------------------------

    /// <summary>
    /// A record is well-formed by construction — its board, cube, roll and
    /// session keep BgDataTypes_Lib's rules — so the export checks none of
    /// them again. What remains is the XG encoding's own limit: a centred cube
    /// above 1 (an auto-doubled money position) has no representation.
    /// </summary>
    [Fact]
    public void Export_Throws_OnCentredCubeAboveOne()
    {
        var decision = CubeAt(TestRecords.Position(
            mop: SampleBoard, cubeSize: 2, cubeOwner: CubeOwner.Centered, session: TestRecords.MoneySession()));

        FluentActions.Invoking(() => XgpExporter.ToBytes(decision))
            .Should().Throw<NotSupportedException>().WithMessage("*centred cube*");
    }
}
