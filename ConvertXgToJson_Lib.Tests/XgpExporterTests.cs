using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// <see cref="XgpExporter"/> tests: decision → .xgp bytes → re-read through
/// our own reader, asserting the record set XG will see.
///
/// <para>
/// The end-to-end oracle is record-level, plus one deliberate iterator
/// assertion: an exported file yields <b>zero</b> decisions from
/// <see cref="XgDecisionIterator"/>. That is <b>by design</b>, not a bug —
/// exports are clean unanalyzed positions (XG re-analyzes on import), and
/// rule 1 of the .xgp emission policy ("skip unanalysed") makes them
/// invisible to this ecosystem's own iterator. Exports are XG-import-only;
/// the ecosystem's re-ingestible format remains BgDecisionData JSON. Do not
/// "fix" the zero-rows assertions to expect one row — that would require
/// carrying analysis through (the booked follow-up), not a test change.
/// </para>
/// </summary>
public class XgpExporterTests
{
    // -----------------------------------------------------------------------
    //  Test decisions
    // -----------------------------------------------------------------------

    /// <summary>A mid-game money-play position, on-roll perspective.</summary>
    private static readonly int[] SampleBoard =
        [0, -1, 0, 0, 0, 0, 5, 2, 3, 0, 0, 0, -6, 3, 0, 0, 0, -2, 0, -4, -2, 1, 0, 0, 1, 0];

    private static BgDecisionData MoneyPlayDecision(int[]? board = null) => new()
    {
        Id = new XgpDecisionId("export.xgp"),
        Xgid = "XGID=-a----E-CB----F---bA-db-B-:0:0:1:65:0:0:1:0:10",
        Position = new PositionData
        {
            Mop = board ?? SampleBoard,
            CubeSize = 1,
            CubeOwner = CubeOwner.Centered,
        },
        Decision = new DecisionData { IsCube = false, Dice = [6, 5] },
        Descriptive = new DescriptiveData
        {
            MatchLength = 0,
            OnRollName = "Hero",
            OpponentName = "Villain",
            Date = new DateOnly(2026, 7, 11),
        },
    };

    private static BgDecisionData MatchCubeDecision() => new()
    {
        Id = new XgpDecisionId("cube.xgp"),
        Position = new PositionData
        {
            Mop = SampleBoard,
            OnRollNeeds = 6,
            OpponentNeeds = 7,
            CubeSize = 2,
            CubeOwner = CubeOwner.OnRoll,
        },
        Decision = new DecisionData { IsCube = true },
        Descriptive = new DescriptiveData
        {
            MatchLength = 13,
            OnRollName = "Joe",
            OpponentName = "Bob",
        },
    };

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
        mh.Jacoby.Should().BeTrue("XGID field 8 carries Jacoby=1");
        mh.Beaver.Should().BeFalse();
        mh.Version.Should().Be(30);
        mh.Date.Should().Be(new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc));

        var gh = file.Records[1].Should().BeOfType<GameHeaderRecord>().Subject;
        gh.Score1.Should().Be(0);
        gh.Score2.Should().Be(0);
        gh.CrawfordApplies.Should().BeFalse();
        gh.GameNumber.Should().Be(1);
        gh.InProgress.Should().BeTrue();
        gh.InitialPosition.Points.Select(p => (int)p).Should().Equal(SampleBoard,
            "XG's position-editor pattern: the game starts at the saved position");

        var cube = file.Records[2].Should().BeOfType<CubeRecord>().Subject;
        cube.ActivePlayer.Should().Be(1);
        cube.Position.Points.Select(p => (int)p).Should().Equal(SampleBoard);
        cube.CubeValue.Should().Be(0, "centred 1-cube");
        cube.DiceRolled.Should().Be("65", "a play decision carries its real roll in the cube pane");

        var move = file.Records[3].Should().BeOfType<MoveRecord>().Subject;
        move.Dice.Should().Equal(6, 5);
        move.ActivePlayer.Should().Be(1);
        move.InitialPosition.Points.Select(p => (int)p).Should().Equal(SampleBoard);
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

        var gh = file.Records[1].Should().BeOfType<GameHeaderRecord>().Subject;
        gh.Score1.Should().Be(7, "score = matchLength - onRollNeeds");
        gh.Score2.Should().Be(6);

        var cube = file.Records[2].Should().BeOfType<CubeRecord>().Subject;
        cube.CubeValue.Should().Be(1, "+log2(2): the on-roll player (player 1) owns a 2-cube");
        cube.DiceRolled.Should().Be("11", "XG's placeholder for a pre-roll cube position");
    }

    [Fact]
    public void CrawfordGame_SetsGameHeaderCrawfordApplies()
    {
        var decision = new BgDecisionData
        {
            Id = new XgpDecisionId("crawford.xgp"),
            Position = new PositionData
            {
                Mop = SampleBoard,
                OnRollNeeds = 1,
                OpponentNeeds = 5,
                CubeSize = 1,
                CubeOwner = CubeOwner.Centered,
                IsCrawford = true,
            },
            Decision = new DecisionData { IsCube = false, Dice = [3, 1] },
            Descriptive = new DescriptiveData { MatchLength = 7 },
        };

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
        var decision = new BgDecisionData
        {
            Id = new XgpDecisionId("cube-enc.xgp"),
            Position = new PositionData { Mop = SampleBoard, OnRollNeeds = 5, OpponentNeeds = 5, CubeSize = size, CubeOwner = owner },
            Decision = new DecisionData { IsCube = true },
            Descriptive = new DescriptiveData { MatchLength = 11 },
        };

        Export(decision).Records[2].Should().BeOfType<CubeRecord>()
            .Which.CubeValue.Should().Be(expectedRaw);
    }

    [Fact]
    public void MoneyFlags_DefaultToJacobyWhenNoXgid()
    {
        var decision = new BgDecisionData
        {
            Id = new XgpDecisionId("no-xgid.xgp"),
            Position = new PositionData { Mop = SampleBoard, CubeSize = 1, CubeOwner = CubeOwner.Centered },
            Decision = new DecisionData { IsCube = true },
            Descriptive = new DescriptiveData { MatchLength = 0 },
        };

        var mh = Export(decision).Records[0].Should().BeOfType<MatchHeaderRecord>().Subject;
        mh.Jacoby.Should().BeTrue("XG's money default");
        mh.Beaver.Should().BeFalse();
    }

    [Fact]
    public void MoneyFlags_ParseBeaverFromXgidField8()
    {
        var decision = new BgDecisionData
        {
            Id = new XgpDecisionId("beaver.xgp"),
            Xgid = "XGID=-a----E-CB----F---bA-db-B-:0:0:1:00:0:0:3:0:10",
            Position = new PositionData { Mop = SampleBoard, CubeSize = 1, CubeOwner = CubeOwner.Centered },
            Decision = new DecisionData { IsCube = true },
            Descriptive = new DescriptiveData { MatchLength = 0 },
        };

        var mh = Export(decision).Records[0].Should().BeOfType<MatchHeaderRecord>().Subject;
        mh.Jacoby.Should().BeTrue();
        mh.Beaver.Should().BeTrue("XGID field 8 = 3 = Jacoby + 2×Beaver");
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
        // Checkers on both bars and men borne off (board sums below 15/side).
        int[] barsAndBearoff =
            [0, -2, 0, 0, 0, 0, 3, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -3, -4, 0, 2, 0, 0, 0, 2];

        var decision = new BgDecisionData
        {
            Id = new XgpDecisionId("edge.xgp"),
            Position = new PositionData { Mop = barsAndBearoff, CubeSize = 1, CubeOwner = CubeOwner.Centered },
            Decision = new DecisionData { IsCube = false, Dice = [2, 2] },
            Descriptive = new DescriptiveData { MatchLength = 0 },
        };

        var file = Export(decision);
        file.Records[2].Should().BeOfType<CubeRecord>()
            .Which.Position.Points.Select(p => (int)p).Should().Equal(barsAndBearoff);
    }

    // -----------------------------------------------------------------------
    //  Validation
    // -----------------------------------------------------------------------

    private static BgDecisionData WithPosition(PositionData position, bool isCube = true, int matchLength = 0) => new()
    {
        Id = new XgpDecisionId("invalid.xgp"),
        Position = position,
        Decision = new DecisionData { IsCube = isCube, Dice = [6, 5] },
        Descriptive = new DescriptiveData { MatchLength = matchLength },
    };

    [Fact]
    public void Export_Throws_OnWrongBoardLength()
    {
        var act = () => XgpExporter.ToBytes(WithPosition(new PositionData { Mop = new int[25] }));
        act.Should().Throw<ArgumentException>().WithMessage("*26 elements*");
    }

    [Fact]
    public void Export_Throws_OnNonPowerOfTwoCube()
    {
        var act = () => XgpExporter.ToBytes(WithPosition(
            new PositionData { Mop = SampleBoard, CubeSize = 3, CubeOwner = CubeOwner.OnRoll }));
        act.Should().Throw<ArgumentException>().WithMessage("*power of two*");
    }

    [Fact]
    public void Export_Throws_OnCentredCubeAboveOne()
    {
        var act = () => XgpExporter.ToBytes(WithPosition(
            new PositionData { Mop = SampleBoard, CubeSize = 2, CubeOwner = CubeOwner.Centered }));
        act.Should().Throw<NotSupportedException>().WithMessage("*centred cube*");
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(7, 5)]
    [InlineData(5, 0)]
    public void Export_Throws_OnInvalidDice(int d1, int d2)
    {
        var decision = new BgDecisionData
        {
            Id = new XgpDecisionId("bad-dice.xgp"),
            Position = new PositionData { Mop = SampleBoard, CubeSize = 1, CubeOwner = CubeOwner.Centered },
            Decision = new DecisionData { IsCube = false, Dice = [d1, d2] },
            Descriptive = new DescriptiveData { MatchLength = 0 },
        };
        var act = () => XgpExporter.ToBytes(decision);
        act.Should().Throw<ArgumentException>().WithMessage("*dice*");
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(8, 3)]
    [InlineData(3, 0)]
    public void Export_Throws_OnNeedsOutsideMatchLength(int onRollNeeds, int opponentNeeds)
    {
        var act = () => XgpExporter.ToBytes(WithPosition(
            new PositionData
            {
                Mop = SampleBoard,
                OnRollNeeds = onRollNeeds,
                OpponentNeeds = opponentNeeds,
                CubeSize = 1,
                CubeOwner = CubeOwner.Centered,
            },
            matchLength: 7));
        act.Should().Throw<ArgumentException>().WithMessage("*needs*");
    }
}
