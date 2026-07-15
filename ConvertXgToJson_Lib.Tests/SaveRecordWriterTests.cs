using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;
using ConvertXgToJson_Lib.Writing;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Per-record-type write→parse round-trips with a distinctive value in
/// every field. This suite is the guard on the reader/writer layout
/// duality: the byte layout is encoded twice (in
/// <see cref="SaveRecordParser"/> and <see cref="SaveRecordWriter"/>), and
/// any drift — a missed field, a wrong alignment, a transposed pair —
/// surfaces here as a field-equality failure. Values are deliberately
/// unique per field so a transposition cannot cancel out.
/// </summary>
public class SaveRecordWriterTests
{
    private static T RoundTrip<T>(T record) where T : SaveRecord
    {
        byte[] bytes = SaveRecordWriter.Write(record);
        bytes.Length.Should().Be(SaveRecordParser.RecordSize);
        using var ms = new MemoryStream(bytes);
        var records = SaveRecordParser.ReadAll(ms);
        records.Should().ContainSingle();
        return records[0].Should().BeOfType<T>().Subject;
    }

    private static PositionEngine DistinctPosition(sbyte seed)
    {
        var points = new sbyte[26];
        for (int i = 0; i < 26; i++)
            points[i] = (sbyte)((i % 2 == 0 ? 1 : -1) * ((seed + i) % 15));
        return new PositionEngine { Points = points };
    }

    private static EvalResult DistinctEval(float seed) => new()
    {
        LoseBackgammon = seed + 0.01f,
        LoseGammon = seed + 0.02f,
        LoseSingle = seed + 0.03f,
        WinSingle = seed + 0.04f,
        WinGammon = seed + 0.05f,
        WinBackgammon = seed + 0.06f,
        Equity = seed + 0.07f,
    };

    [Fact]
    public void MatchHeader_RoundTripsEveryField()
    {
        var original = new MatchHeaderRecord
        {
            EntryType = RecordType.HeaderMatch,
            Player1Ansi = "Alpha Ansi",
            Player2Ansi = "Beta Ansi",
            MatchLength = 13,
            Variation = 2,
            Crawford = true,
            Jacoby = true,
            Beaver = true,
            AutoDouble = true,
            Elo1 = 1712.5,
            Elo2 = 1698.25,
            Experience1 = 401,
            Experience2 = 402,
            // Noon = exactly .5 days: TDateTime is a double of days on the
            // wire, so only binary-exact fractions round-trip tick-for-tick.
            // (Real-file dates are already double-quantized at parse time and
            // therefore round-trip exactly — see the corpus tests.)
            Date = new DateTime(2020, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            EventAnsi = "Event Ansi",
            GameId = 12345678,
            CompLevel1 = 7,
            CompLevel2 = 8,
            CountForElo = true,
            AddToProfile1 = true,
            AddToProfile2 = true,
            LocationAnsi = "Location Ansi",
            GameMode = GameMode.Teaching,
            Imported = true,
            RoundAnsi = "Round Ansi",
            Invert = 1,
            Version = 30,
            Magic = 1229737284,
            MoneyInitGames = 3,
            MoneyInitScore = [11, 22],
            Entered = true,
            Counted = true,
            UnratedImport = true,
            CommentHeaderMatchIndex = 5,
            CommentFooterMatchIndex = 6,
            IsMoneyMatch = true,
            WinMoney = 1.5f,
            LoseMoney = 2.5f,
            Currency = CurrencyId.SwissFranc,
            FeeMoney = 3.5f,
            TableStake = 4.5f,
            SiteId = SiteId.GridGammon,
            CubeLimit = 10,
            AutoDoubleMax = 4,
            Transcribed = true,
            Event = "Event Unicode ü",
            Player1 = "Alpha Unicode é",
            Player2 = "Beta Unicode ß",
            Location = "Location Unicode ø",
            Round = "Round Unicode ñ",
            TimeSetting = new TimeSetting
            {
                ClockType = ClockType.Bronstein,
                PerGame = true,
                Time1 = 101,
                Time2 = 102,
                Penalty = 103,
                TimeLeft1 = 104,
                TimeLeft2 = 105,
                PenaltyMoney = 106,
            },
            TotalTimeDelayMoves = 201,
            TotalTimeDelayCubes = 202,
            TotalTimeDelayMovesDone = 203,
            TotalTimeDelayCubesDone = 204,
            Transcriber = "Transcriber Unicode",
        };

        RoundTrip(original).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void GameHeader_RoundTripsEveryField()
    {
        var original = new GameHeaderRecord
        {
            EntryType = RecordType.HeaderGame,
            Score1 = 7,
            Score2 = 6,
            CrawfordApplies = true,
            InitialPosition = DistinctPosition(3),
            GameNumber = 12,
            InProgress = true,
            CommentHeaderGameIndex = 9,
            CommentFooterGameIndex = 10,
            NumberOfAutoDoubles = 2,
        };

        RoundTrip(original).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Cube_RoundTripsEveryField()
    {
        var original = new CubeRecord
        {
            EntryType = RecordType.Cube,
            ActivePlayer = -1,
            Doubled = 1,
            Taken = 2,
            BeaverAccepted = 1,
            RaccoonAccepted = -1,
            CubeValue = -2,
            Position = DistinctPosition(5),
            Analysis = new DoubleActionAnalysis
            {
                Position = DistinctPosition(7),
                Level = 1002,
                Score = [6, 7],
                Cube = 4,
                CubePosition = -1,
                Jacoby = 1,
                Crawford = 1,
                FlagDouble = 1,
                IsBeaver = 1,
                EvalNoDouble = DistinctEval(0.1f),
                EquityNoDouble = 0.234f,
                EquityDoubleTake = -0.345f,
                EquityDoubleDrop = 1.0f,
                LevelRequest = 3,
                DoubleChoice3 = 2,
                EvalDoubleTake = DistinctEval(0.2f),
            },
            ErrorCube = -0.123,
            DiceRolled = "54",
            ErrorTake = -0.456,
            RolloutIndex = 4,
            ComputerChoice = 1,
            AnalyzeLevel = 3,
            ErrorBeaver = -0.5,
            ErrorRaccoon = -0.25,
            AnalyzeLevelRequested = 5,
            InvalidDecision = 1,
            TutorCube = 2,
            TutorTake = -3,
            ErrorTutorCube = -0.75,
            ErrorTutorTake = -0.875,
            Flagged = true,
            CommentIndex = 3,
            Edited = true,
            TimeDelayed = true,
            TimeDelayDone = true,
            NumberOfAutoDoubles = 1,
            TimeBotLeft = 301,
            TimeTopLeft = 302,
        };

        RoundTrip(original).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Move_RoundTripsEveryField()
    {
        var positionsPlayed = new PositionEngine[32];
        var moves = new sbyte[32][];
        var evalLevels = new EvalLevel[32];
        var evals = new EvalResult[32];
        var rolloutIndices = new int[32];
        for (int i = 0; i < 32; i++)
        {
            positionsPlayed[i] = DistinctPosition((sbyte)(i + 1));
            moves[i] = [(sbyte)i, (sbyte)(i + 1), (sbyte)(i + 2), (sbyte)(i + 3), -1, 0, 0, 0];
            evalLevels[i] = new EvalLevel { Level = (short)(i % 4), IsDouble = i % 2 == 0 };
            evals[i] = DistinctEval(i * 0.01f);
            rolloutIndices[i] = i - 1;
        }

        var original = new MoveRecord
        {
            EntryType = RecordType.Move,
            InitialPosition = DistinctPosition(2),
            FinalPosition = DistinctPosition(4),
            ActivePlayer = -1,
            MoveList = [23, 18, 18, 13, -1, -1, -1, -1],
            Dice = [6, 5],
            CubeValue = 2,
            ErrorMove = -0.111,
            CandidateCount = 25,
            Analysis = new BestMoveAnalysis
            {
                Position = DistinctPosition(6),
                Dice = [6, 5],
                Level = 1002,
                Score = [-1, -1],
                Cube = 2,
                CubePosition = 1,
                Crawford = 1,
                Jacoby = 1,
                MoveCount = 25,
                PositionsPlayed = positionsPlayed,
                Moves = moves,
                EvalLevels = evalLevels,
                Evals = evals,
                Irrelevant = true,
                Choice1Ply = 1,
                Choice3Ply = 2,
            },
            Played = true,
            MoveError = -0.222,
            LuckError = 0.0559,
            ComputerChoice = 1,
            InitialEquity = 0.0091,
            RolloutIndices = rolloutIndices,
            AnalyzeLevel = 1002,
            AnalyzeLevelLuck = 3,
            InvalidDecision = 1,
            TutorPosition = DistinctPosition(8),
            TutorMoveIndex = 5,
            ErrorTutorMove = -0.333,
            Flagged = true,
            CommentIndex = 2,
            Edited = true,
            TimeDelayBits = 0xDEADBEEF,
            TimeDelayDoneBits = 0xCAFEBABE,
            NumberOfAutoDoubles = 1,
        };

        RoundTrip(original).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void FooterGame_RoundTripsEveryField()
    {
        var original = new GameFooterRecord
        {
            EntryType = RecordType.FooterGame,
            Score1 = 9,
            Score2 = 4,
            CrawfordAppliesNext = true,
            Winner = -1,
            PointsWon = 2,
            Termination = 102,
            ErrorResign = -0.15,
            ErrorTakeResign = -0.35,
            FinalEval = [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7],
            EvalLevel = 3,
        };

        RoundTrip(original).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void FooterMatch_RoundTripsEveryField()
    {
        var original = new MatchFooterRecord
        {
            EntryType = RecordType.FooterMatch,
            Score1 = 13,
            Score2 = 11,
            Winner = 1,
            Elo1 = 1725.5,
            Elo2 = 1690.75,
            Exp1 = 501,
            Exp2 = 502,
            Date = new DateTime(2021, 3, 9, 18, 0, 0, DateTimeKind.Utc),
        };

        RoundTrip(original).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void RolloutContext_RoundTripsEveryField()
    {
        var original = new RolloutContext
        {
            Truncated = true,
            ErrorLimited = true,
            TruncateLevel = 7,
            MinRolls = 36,
            ErrorLimit = 0.0123,
            MaxRolls = 1296,
            Level1 = 2,
            Level2 = 3,
            LevelCut = 1,
            VarianceReduction = true,
            Cubeless = true,
            TimeLimited = true,
            Level1Cube = 4,
            Level2Cube = 5,
            TimeLimit = 3600,
            TruncateBO = 2,
            RandomSeed = 987654,
            RandomSeedInitial = 123456,
            RollBoth = true,
            SearchInterval = 1.25f,
            FirstRoll = true,
            DoDouble = true,
            Extended = true,
            GamesRolled = 1296,
            DoubleFirst = true,
            Sum1 = [.. Enumerable.Range(0, 37).Select(i => i * 1.1)],
            SumSquare1 = [.. Enumerable.Range(0, 37).Select(i => i * 1.2)],
            Sum2 = [.. Enumerable.Range(0, 37).Select(i => i * 1.3)],
            SumSquare2 = [.. Enumerable.Range(0, 37).Select(i => i * 1.4)],
            Stdev1 = [.. Enumerable.Range(0, 37).Select(i => i * 1.5)],
            Stdev2 = [.. Enumerable.Range(0, 37).Select(i => i * 1.6)],
            RolledPerDice = [.. Enumerable.Range(100, 37)],
            Error1 = 0.011f,
            Error2 = 0.022f,
            Result1 = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f],
            Result2 = [1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f],
            Mwc1 = 0.61f,
            Mwc2 = 0.62f,
            PrevLevel = 3,
            PrevEval = [2.1f, 2.2f, 2.3f, 2.4f, 2.5f, 2.6f, 2.7f],
            PrevND = 0.31f,
            PrevD = 0.32f,
            Duration = 42.5f,
            LevelTrunc = 2,
            GamesRolledDouble = 648,
            MultipleMin = 5,
            MultipleStopAll = true,
            MultipleStopOne = true,
            MultipleStopAllValue = 0.9f,
            MultipleStopOneValue = 0.8f,
            AsTake = true,
            Rotation = 3,
            UserInterrupted = true,
            VersionMajor = 2,
            VersionMinor = 19,
        };

        byte[] bytes = RolloutContextWriter.WriteAll([original]);
        bytes.Length.Should().Be(RolloutContextParser.RecordSize);
        using var ms = new MemoryStream(bytes);
        var parsed = RolloutContextParser.ReadAll(ms);
        parsed.Should().ContainSingle();
        parsed[0].Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Comments_RoundTrip_IncludingEmbeddedCrlfEscape()
    {
        List<string> comments =
        [
            "plain comment",
            "multi\r\nline\r\ncomment",
            @"{\rtf1 rtf-style comment}",
        ];

        byte[] bytes = CommentWriter.WriteAll(comments);
        using var ms = new MemoryStream(bytes);
        CommentParser.ReadAll(ms).Should().Equal(comments);
    }

    [Fact]
    public void Comments_RoundTrip_KeepsInteriorEmptyComment()
    {
        // CommentWriter writes an empty comment as a bare CRLF line, so the
        // parser must keep interior empty segments — skipping one shifts
        // every later entry and desyncs the records' CommentIndex
        // references. Only the empty segment after the final CRLF is a
        // split artifact rather than a comment.
        List<string> comments = ["first", "", "third"];

        byte[] bytes = CommentWriter.WriteAll(comments);
        using var ms = new MemoryStream(bytes);
        CommentParser.ReadAll(ms).Should().Equal(comments);

        // The hardest case: a real empty comment in final position. Its
        // own CRLF terminator still produces exactly one artifact segment,
        // which is all the parser may drop.
        List<string> trailingEmpty = ["first", ""];
        using var ms2 = new MemoryStream(CommentWriter.WriteAll(trailingEmpty));
        CommentParser.ReadAll(ms2).Should().Equal(trailingEmpty);
    }
}
