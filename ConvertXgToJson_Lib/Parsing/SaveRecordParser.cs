using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Parsing;

/// <summary>
/// Parses TSaveRec records from a stream.
///
/// Each record is exactly 2560 bytes on disk.  The first 9 bytes are always:
///   Previous : Pointer  (4 bytes, ignored)
///   Next     : Pointer  (4 bytes, ignored)
///   EntryType: byte     (1 byte – RecordType enum)
///
/// After EntryType the variant part begins.  We track the byte offset within
/// the record (starting at 0) and apply Pascal alignment rules as we go.
/// After reading the variant fields we skip any remaining bytes to land on
/// the next 2560-byte boundary.
/// </summary>
internal static class SaveRecordParser
{
    private const int RecordSize = 2560;

    public static List<SaveRecord> ReadAll(Stream stream)
    {
        var records = new List<SaveRecord>();
        long start = stream.Position;

        while (stream.Length - stream.Position >= RecordSize)
        {
            long recordStart = stream.Position;
            var record = ReadOne(stream);
            records.Add(record);

            // Ensure we are exactly at the next record boundary
            long bytesRead = stream.Position - recordStart;
            long remaining = RecordSize - bytesRead;
            if (remaining > 0)
                stream.Seek(remaining, SeekOrigin.Current);
            else if (remaining < 0)
                throw new InvalidDataException(
                    $"SaveRecord parser overread by {-remaining} bytes at offset {recordStart}.");
        }

        return records;
    }

    private static SaveRecord ReadOne(Stream stream)
    {
        // We wrap a subsection of the stream in a PascalBinaryReader.
        // Because PascalBinaryReader uses stream.Position for alignment,
        // it must see the absolute stream position.  We read the fixed
        // 9-byte preamble first using a plain BinaryReader so that
        // PascalBinaryReader alignment is based on position WITHIN the record.
        //
        // Trick: we create a sub-stream view that starts at byte 0 of the record,
        // so alignment is relative to the record start.

        long recordStart = stream.Position;
        using var sub = new SubStream(stream, recordStart, RecordSize, leaveOpen: true);
        using var r = new PascalBinaryReader(sub);

        // Fixed preamble (9 bytes, no alignment on pointers/byte)
        _ = r.ReadDword();   // Previous (Pointer = 4 bytes, but alignment is relative – at offset 0 it's fine)
        _ = r.ReadDword();   // Next
        var entryType = (RecordType)r.ReadByte(); // offset 8

        // Now at offset 9 inside the record
        return entryType switch
        {
            RecordType.HeaderMatch => ReadHeaderMatch(r, entryType),
            RecordType.HeaderGame  => ReadHeaderGame(r, entryType),
            RecordType.Cube        => ReadCube(r, entryType),
            RecordType.Move        => ReadMove(r, entryType),
            RecordType.FooterGame  => ReadFooterGame(r, entryType),
            RecordType.FooterMatch => ReadFooterMatch(r, entryType),
            _ => new UnknownRecord(entryType),
        };
    }

    // ------------------------------------------------------------------
    //  tsHeaderMatch  (starts at offset 9 inside a 2560-byte record)
    // ------------------------------------------------------------------
    private static MatchHeaderRecord ReadHeaderMatch(PascalBinaryReader r, RecordType type)
    {
        // Offset 9 – string[40] = 41 bytes, no align
        string player1Ansi = r.ReadPascalAnsiString(40);   // 41 bytes  → offset 50
        string player2Ansi = r.ReadPascalAnsiString(40);   // 41 bytes  → offset 91

        // MatchLength: integer (4-byte align).  Offset 91 → pad 1 → 92
        int matchLength = r.ReadInteger();                  // offset 96
        int variation   = r.ReadInteger();                  // offset 100
        bool crawford   = r.ReadBoolean();                  // offset 101
        bool jacoby     = r.ReadBoolean();                  // offset 102
        bool beaver     = r.ReadBoolean();                  // offset 103
        bool autoDouble = r.ReadBoolean();                  // offset 104

        // Elo1: Double (8-byte align).  Offset 104 = multiple of 8 → no pad
        double elo1     = r.ReadDouble();                   // offset 112
        double elo2     = r.ReadDouble();                   // offset 120
        int exp1        = r.ReadInteger();                  // offset 124
        int exp2        = r.ReadInteger();                  // offset 128

        // Date: TDateTime (8-byte align). Offset 128 → no pad
        DateTime date   = r.ReadTDateTime();                // offset 136

        // SEvent: string[128] = 129 bytes, no align
        string eventAnsi = r.ReadPascalAnsiString(128);    // offset 265

        // GameId: integer (4-byte align). Offset 265 → pad 3 → 268
        int gameId      = r.ReadInteger();                  // offset 272
        int compLevel1  = r.ReadInteger();                  // offset 276
        int compLevel2  = r.ReadInteger();                  // offset 280
        bool countElo   = r.ReadBoolean();                  // offset 281
        bool addProf1   = r.ReadBoolean();                  // offset 282
        bool addProf2   = r.ReadBoolean();                  // offset 283

        // SLocation: string[128] = 129 bytes, no align.  Offset 283
        string locationAnsi = r.ReadPascalAnsiString(128); // offset 412

        // GameMode: integer (4-byte align). Offset 412 → no pad (412 % 4 = 0)
        var gameMode    = (GameMode)r.ReadInteger();        // offset 416
        bool imported   = r.ReadBoolean();                  // offset 417

        // SRound: string[128] = 129 bytes, no align. Offset 417
        string roundAnsi = r.ReadPascalAnsiString(128);    // offset 546

        // Invert: integer (4-byte align). Offset 546 → pad 2 → 548
        int invert      = r.ReadInteger();                  // offset 552
        int version     = r.ReadInteger();                  // offset 556
        int magic       = r.ReadInteger();                  // offset 560
        int moneyInitG  = r.ReadInteger();                  // offset 564

        // MoneyInitScore: array[1..2] of integer (already 4-byte aligned, offset 568)
        int moneyScore1 = r.ReadInteger();                  // offset 572
        int moneyScore2 = r.ReadInteger();                  // offset 576

        bool entered    = r.ReadBoolean();                  // offset 577
        bool counted    = r.ReadBoolean();                  // offset 578
        bool unratedImp = r.ReadBoolean();                  // offset 579

        // CommentHeaderMatch: integer (4-byte align). Offset 579 → pad 1 → 580
        int commentHeader = r.ReadInteger();                // offset 584
        int commentFooter = r.ReadInteger();                // offset 588
        bool isMoneyMatch = r.ReadBoolean();                // offset 589

        // WinMoney: single (4-byte align). Offset 589 → pad 3 → 592
        float winMoney  = r.ReadSingle();                   // offset 596
        float loseMoney = r.ReadSingle();                   // offset 600

        // Currency: integer. offset 600 → no pad
        var currency    = (CurrencyId)r.ReadInteger();      // offset 604
        float feeMoney  = r.ReadSingle();                   // offset 608
        float tableStake = r.ReadSingle();                  // offset 612
        var siteId      = (SiteId)r.ReadInteger();          // offset 616
        int cubeLimit   = r.ReadInteger();                   // offset 620
        int autoDblMax  = r.ReadInteger();                   // offset 624
        bool transcribed = r.ReadBoolean();                  // offset 625

        // Event: TShortUnicodeString (array[0..128] of char = 258 bytes, 2-byte align)
        // Offset 625 → pad 1 → 626
        string eventUni    = r.ReadShortUnicodeString();    // offset 884
        string player1Uni  = r.ReadShortUnicodeString();    // offset 1142
        string player2Uni  = r.ReadShortUnicodeString();    // offset 1400
        string locationUni = r.ReadShortUnicodeString();    // offset 1658
        string roundUni    = r.ReadShortUnicodeString();    // offset 1916

        // TimeSetting (32 bytes, starts with integer → 4-byte align)
        // Offset 1916 → no pad (1916 % 4 = 0)
        var timeSetting = ReadTimeSetting(r);               // offset 1948

        // v26 fields (integers)
        int totDelayMove     = r.ReadInteger();             // offset 1952
        int totDelayCube     = r.ReadInteger();             // offset 1956
        int totDelayMoveDone = r.ReadInteger();             // offset 1960
        int totDelayCubeDone = r.ReadInteger();             // offset 1964

        // Transcriber: TShortUnicodeString (2-byte align, offset 1964 → no pad)
        string transcriber = r.ReadShortUnicodeString();    // offset 2222

        return new MatchHeaderRecord
        {
            EntryType              = type,
            Player1Ansi            = player1Ansi,
            Player2Ansi            = player2Ansi,
            MatchLength            = matchLength,
            Variation              = variation,
            Crawford               = crawford,
            Jacoby                 = jacoby,
            Beaver                 = beaver,
            AutoDouble             = autoDouble,
            Elo1                   = elo1,
            Elo2                   = elo2,
            Experience1            = exp1,
            Experience2            = exp2,
            Date                   = date,
            EventAnsi              = eventAnsi,
            GameId                 = gameId,
            CompLevel1             = compLevel1,
            CompLevel2             = compLevel2,
            CountForElo            = countElo,
            AddToProfile1          = addProf1,
            AddToProfile2          = addProf2,
            LocationAnsi           = locationAnsi,
            GameMode               = gameMode,
            Imported               = imported,
            RoundAnsi              = roundAnsi,
            Invert                 = invert,
            Version                = version,
            Magic                  = magic,
            MoneyInitGames         = moneyInitG,
            MoneyInitScore         = [moneyScore1, moneyScore2],
            Entered                = entered,
            Counted                = counted,
            UnratedImport          = unratedImp,
            CommentHeaderMatchIndex = commentHeader,
            CommentFooterMatchIndex = commentFooter,
            IsMoneyMatch           = isMoneyMatch,
            WinMoney               = winMoney,
            LoseMoney              = loseMoney,
            Currency               = currency,
            FeeMoney               = feeMoney,
            TableStake             = tableStake,
            SiteId                 = siteId,
            CubeLimit              = cubeLimit,
            AutoDoubleMax          = autoDblMax,
            Transcribed            = transcribed,
            Event                  = eventUni,
            Player1                = player1Uni,
            Player2                = player2Uni,
            Location               = locationUni,
            Round                  = roundUni,
            TimeSetting            = timeSetting,
            TotalTimeDelayMoves    = totDelayMove,
            TotalTimeDelayCubes    = totDelayCube,
            TotalTimeDelayMovesDone = totDelayMoveDone,
            TotalTimeDelayCubesDone = totDelayCubeDone,
            Transcriber            = transcriber,
        };
    }

    // ------------------------------------------------------------------
    //  tsHeaderGame
    // ------------------------------------------------------------------
    private static GameHeaderRecord ReadHeaderGame(PascalBinaryReader r, RecordType type)
    {
        // Starts at offset 9. score1: integer → 4-byte align → pad 3 → offset 12
        int score1   = r.ReadInteger();
        int score2   = r.ReadInteger();
        bool crawford = r.ReadBoolean();
        // PosInit: PositionEngine = array[0..25] of ShortInt = 26 bytes, no align
        var posInit  = ReadPosition(r);
        // GameNumber: integer (4-byte align)
        int gameNum  = r.ReadInteger();
        bool inProg  = r.ReadBoolean();
        int cmtH     = r.ReadInteger();
        int cmtF     = r.ReadInteger();
        int numAutoDbl = r.ReadInteger();

        return new GameHeaderRecord
        {
            EntryType              = type,
            Score1                 = score1,
            Score2                 = score2,
            CrawfordApplies        = crawford,
            InitialPosition        = posInit,
            GameNumber             = gameNum,
            InProgress             = inProg,
            CommentHeaderGameIndex = cmtH,
            CommentFooterGameIndex = cmtF,
            NumberOfAutoDoubles    = numAutoDbl,
        };
    }

    // ------------------------------------------------------------------
    //  tsCube
    // ------------------------------------------------------------------
    private static CubeRecord ReadCube(PascalBinaryReader r, RecordType type)
    {
        // Starts at offset 9 → Actif: integer (4-byte align) → pad 3 → offset 12
        int active   = r.ReadInteger();
        int doubled  = r.ReadInteger();
        int taken    = r.ReadInteger();
        int beaverR  = r.ReadInteger();
        int raccoonR = r.ReadInteger();
        int cubeVal  = r.ReadInteger();
        var pos = ReadPosition(r);
        // EngineStructDoubleAction is 4-byte aligned within the parent record
        r.AlignTo(4);
        var analysis = ReadDoubleAction(r);

        // ErrCube: Double (8-byte align)
        double errCube   = r.ReadDouble();
        // DiceRolled: string[2] = 3 bytes, no align
        string diceRolled = r.ReadPascalAnsiString(2);
        // ErrTake: Double (8-byte align)
        double errTake   = r.ReadDouble();
        int roIdx        = r.ReadInteger();
        int compChoice   = r.ReadInteger();
        int analyzeC     = r.ReadInteger();
        double errBeaver = r.ReadDouble();
        double errRaccoon = r.ReadDouble();
        int analyzeCR    = r.ReadInteger();
        int invalid      = r.ReadInteger();
        sbyte tutorCube  = r.ReadShortInt();
        sbyte tutorTake  = r.ReadShortInt();
        double errTutorCube = r.ReadDouble();
        double errTutorTake = r.ReadDouble();
        bool flagged     = r.ReadBoolean();
        int cmtIdx       = r.ReadInteger();
        bool edited      = r.ReadBoolean();
        bool timeDelay   = r.ReadBoolean();
        bool timeDelayDone = r.ReadBoolean();
        int numAutoDbl   = r.ReadInteger();
        int timeBot      = r.ReadInteger();
        int timeTop      = r.ReadInteger();

        return new CubeRecord
        {
            EntryType              = type,
            ActivePlayer           = active,
            Doubled                = doubled,
            Taken                  = taken,
            BeaverAccepted         = beaverR,
            RaccoonAccepted        = raccoonR,
            CubeValue              = cubeVal,
            Position               = pos,
            Analysis               = analysis,
            ErrorCube              = errCube,
            DiceRolled             = diceRolled,
            ErrorTake              = errTake,
            RolloutIndex           = roIdx,
            ComputerChoice         = compChoice,
            AnalyzeLevel           = analyzeC,
            ErrorBeaver            = errBeaver,
            ErrorRaccoon           = errRaccoon,
            AnalyzeLevelRequested  = analyzeCR,
            InvalidDecision        = invalid,
            TutorCube              = tutorCube,
            TutorTake              = tutorTake,
            ErrorTutorCube         = errTutorCube,
            ErrorTutorTake         = errTutorTake,
            Flagged                = flagged,
            CommentIndex           = cmtIdx,
            Edited                 = edited,
            TimeDelayed            = timeDelay,
            TimeDelayDone          = timeDelayDone,
            NumberOfAutoDoubles    = numAutoDbl,
            TimeBotLeft            = timeBot,
            TimeTopLeft            = timeTop,
        };
    }

    // ------------------------------------------------------------------
    //  tsMove
    // ------------------------------------------------------------------
    private static MoveRecord ReadMove(PascalBinaryReader r, RecordType type)
    {
        // Starts at offset 9. PositionI: no align (26 bytes)
        var posInit  = ReadPosition(r);
        var posEnd   = ReadPosition(r);
        // ActifP: integer (4-byte align)
        int active   = r.ReadInteger();

        // Moves: array[1..8] of integer (4-byte align, already aligned)
        int[] moves  = [r.ReadInteger(), r.ReadInteger(), r.ReadInteger(), r.ReadInteger(),
                        r.ReadInteger(), r.ReadInteger(), r.ReadInteger(), r.ReadInteger()];
        int[] dice   = [r.ReadInteger(), r.ReadInteger()];
        int cubeVal  = r.ReadInteger();
        double errM  = r.ReadDouble();
        int nMoves   = r.ReadInteger();

        // DataMoves: EngineStructBestMove (2184 bytes)
        r.AlignTo(4);
        var analysis = ReadBestMove(r);

        bool played      = r.ReadBoolean();
        double errMove   = r.ReadDouble();
        double errLuck   = r.ReadDouble();
        int compChoice   = r.ReadInteger();
        double initEq    = r.ReadDouble();

        // RolloutindexM: array[1..32] of integer
        int[] roIdx = new int[32];
        for (int i = 0; i < 32; i++) roIdx[i] = r.ReadInteger();

        int analyzeM   = r.ReadInteger();
        int analyzeL   = r.ReadInteger();
        int invalidM   = r.ReadInteger();
        var posTutor   = ReadPosition(r);
        sbyte tutor    = r.ReadShortInt();
        double errTutor = r.ReadDouble();
        bool flagged   = r.ReadBoolean();
        int cmtIdx     = r.ReadInteger();
        bool edited    = r.ReadBoolean();
        // TimeDelayMove: Dword (4-byte align)
        uint tdMove    = r.ReadDword();
        uint tdMoveDone = r.ReadDword();
        int numAutoDbl = r.ReadInteger();
        // Filler: array[1..4] of integer
        for (int i = 0; i < 4; i++) r.ReadInteger();

        return new MoveRecord
        {
            EntryType           = type,
            InitialPosition     = posInit,
            FinalPosition       = posEnd,
            ActivePlayer        = active,
            MoveList            = moves,
            Dice                = dice,
            CubeValue           = cubeVal,
            ErrorMove           = errM,
            CandidateCount      = nMoves,
            Analysis            = analysis,
            Played              = played,
            MoveError           = errMove,
            LuckError           = errLuck,
            ComputerChoice      = compChoice,
            InitialEquity       = initEq,
            RolloutIndices      = roIdx,
            AnalyzeLevel        = analyzeM,
            AnalyzeLevelLuck    = analyzeL,
            InvalidDecision     = invalidM,
            TutorPosition       = posTutor,
            TutorMoveIndex      = tutor,
            ErrorTutorMove      = errTutor,
            Flagged             = flagged,
            CommentIndex        = cmtIdx,
            Edited              = edited,
            TimeDelayBits       = tdMove,
            TimeDelayDoneBits   = tdMoveDone,
            NumberOfAutoDoubles = numAutoDbl,
        };
    }

    // ------------------------------------------------------------------
    //  tsFooterGame
    // ------------------------------------------------------------------
    private static GameFooterRecord ReadFooterGame(PascalBinaryReader r, RecordType type)
    {
        // Starts at offset 9. Score1g: integer → pad 3 → offset 12
        int score1     = r.ReadInteger();
        int score2     = r.ReadInteger();
        bool crawford  = r.ReadBoolean();
        // Winner: integer (4-byte align)
        int winner     = r.ReadInteger();
        int points     = r.ReadInteger();
        int term       = r.ReadInteger();
        double errResign     = r.ReadDouble();
        double errTakeResign = r.ReadDouble();

        // Eval: array[0..6] of Double
        double[] eval = new double[7];
        for (int i = 0; i < 7; i++) eval[i] = r.ReadDouble();

        int evalLevel  = r.ReadInteger();

        return new GameFooterRecord
        {
            EntryType              = type,
            Score1                 = score1,
            Score2                 = score2,
            CrawfordAppliesNext    = crawford,
            Winner                 = winner,
            PointsWon              = points,
            Termination            = term,
            ErrorResign            = errResign,
            ErrorTakeResign        = errTakeResign,
            FinalEval              = eval,
            EvalLevel              = evalLevel,
        };
    }

    // ------------------------------------------------------------------
    //  tsFooterMatch
    // ------------------------------------------------------------------
    private static MatchFooterRecord ReadFooterMatch(PascalBinaryReader r, RecordType type)
    {
        // Starts at offset 9. Score1m: integer → pad 3 → offset 12
        int score1   = r.ReadInteger();
        int score2   = r.ReadInteger();
        int winner   = r.ReadInteger();
        // Elo1m: Double (8-byte align)
        double elo1  = r.ReadDouble();
        double elo2  = r.ReadDouble();
        int exp1     = r.ReadInteger();
        int exp2     = r.ReadInteger();
        // Datem: TDateTime (8-byte align)
        DateTime date = r.ReadTDateTime();

        return new MatchFooterRecord
        {
            EntryType = type,
            Score1    = score1,
            Score2    = score2,
            Winner    = winner,
            Elo1      = elo1,
            Elo2      = elo2,
            Exp1      = exp1,
            Exp2      = exp2,
            Date      = date,
        };
    }

    // ------------------------------------------------------------------
    //  Shared sub-structure parsers
    // ------------------------------------------------------------------

    private static PositionEngine ReadPosition(PascalBinaryReader r)
    {
        // array[0..25] of ShortInt = 26 bytes, no alignment
        var points = new sbyte[26];
        for (int i = 0; i < 26; i++) points[i] = r.ReadShortInt();
        return new PositionEngine { Points = points };
    }

    private static EvalResult ReadEvalResult7Single(PascalBinaryReader r)
    {
        // 7 Singles (4-byte align on first)
        float loseBG  = r.ReadSingle();
        float loseG   = r.ReadSingle();
        float loseS   = r.ReadSingle();
        float winS    = r.ReadSingle();
        float winG    = r.ReadSingle();
        float winBG   = r.ReadSingle();
        float equity  = r.ReadSingle();
        return new EvalResult
        {
            LoseBackgammon = loseBG, LoseGammon = loseG, LoseSingle = loseS,
            WinSingle = winS, WinGammon = winG, WinBackgammon = winBG, Equity = equity,
        };
    }

    private static EvalLevel ReadEvalLevel(PascalBinaryReader r)
    {
        // SmallInt (2-byte align) + Boolean + Fill1(byte) = 4 bytes
        short level  = r.ReadSmallInt();
        bool isDbl   = r.ReadBoolean();
        _ = r.ReadByte(); // filler
        return new EvalLevel { Level = level, IsDouble = isDbl };
    }

    private static TimeSetting ReadTimeSetting(PascalBinaryReader r)
    {
        // ClockType: integer (4-byte align, already guaranteed by caller)
        var clockType  = (ClockType)r.ReadInteger();
        bool perGame   = r.ReadBoolean();
        // Time1: integer (4-byte align)
        int time1      = r.ReadInteger();
        int time2      = r.ReadInteger();
        int penalty    = r.ReadInteger();
        int timeLeft1  = r.ReadInteger();
        int timeLeft2  = r.ReadInteger();
        int penMoney   = r.ReadInteger();
        return new TimeSetting
        {
            ClockType    = clockType,
            PerGame      = perGame,
            Time1        = time1,
            Time2        = time2,
            Penalty      = penalty,
            TimeLeft1    = timeLeft1,
            TimeLeft2    = timeLeft2,
            PenaltyMoney = penMoney,
        };
    }

    private static DoubleActionAnalysis ReadDoubleAction(PascalBinaryReader r)
    {
        // EngineStructDoubleAction = 132 bytes
        // Pos: PositionEngine (26 bytes, no align)
        var pos      = ReadPosition(r);
        // Level: integer (4-byte align)
        int level    = r.ReadInteger();
        int score1   = r.ReadInteger();
        int score2   = r.ReadInteger();
        int cube = r.ReadInteger();
        int cubePos = r.ReadInteger();
        short jacoby = r.ReadSmallInt();  // try SmallInt instead of Integer
        short met = r.ReadSmallInt();  // unused
        short crawford = r.ReadSmallInt();
        short flagDbl = r.ReadSmallInt();
        short isBeaver = r.ReadSmallInt();
        // Eval: array[0..6] of single (4-byte align)
        var evalND   = ReadEvalResult7Single(r);
        // equB, equDouble, equDrop: singles (4-byte aligned, continuing)
        float equND  = r.ReadSingle();
        float equDT  = r.ReadSingle();
        float equDrop = r.ReadSingle();
        short lvlReq = r.ReadSmallInt();
        short dblCh3 = r.ReadSmallInt();
        var evalDT   = ReadEvalResult7Single(r);

        return new DoubleActionAnalysis
        {
            Position         = pos,
            Level            = level,
            Score            = [score1, score2],
            Cube             = cube,
            CubePosition     = cubePos,
            Jacoby           = jacoby,
            Crawford         = crawford,
            FlagDouble       = flagDbl,
            IsBeaver         = isBeaver,
            EvalNoDouble     = evalND,
            EquityNoDouble   = equND,
            EquityDoubleTake = equDT,
            EquityDoubleDrop = equDrop,
            LevelRequest     = lvlReq,
            DoubleChoice3    = dblCh3,
            EvalDoubleTake   = evalDT,
        };
    }

    private static BestMoveAnalysis ReadBestMove(PascalBinaryReader r)
    {
        // EngineStructBestMove = 2184 bytes
        var pos      = ReadPosition(r);
        // Dice: array[1..2] of integer (4-byte align)
        int dice1    = r.ReadInteger();
        int dice2    = r.ReadInteger();
        int level    = r.ReadInteger();
        int sc1      = r.ReadInteger();
        int sc2      = r.ReadInteger();
        int cube     = r.ReadInteger();
        int cubePos  = r.ReadInteger();
        int crawford = r.ReadInteger();
        int jacoby   = r.ReadInteger();
        int nMoves   = r.ReadInteger();

        // PosPlayed: array[1..32] of PositionEngine (32 × 26 = 832 bytes, no align)
        var posPlayed = new PositionEngine[32];
        for (int i = 0; i < 32; i++) posPlayed[i] = ReadPosition(r);

        // Moves: array[1..32, 1..8] of ShortInt (32×8 = 256 bytes, no align)
        var movesList = new sbyte[32][];
        for (int i = 0; i < 32; i++)
        {
            movesList[i] = new sbyte[8];
            for (int j = 0; j < 8; j++) movesList[i][j] = r.ReadShortInt();
        }

        // EvalLevel: array[1..32] of TEvalLevel (32 × 4 = 128 bytes)
        var evalLevels = new EvalLevel[32];
        for (int i = 0; i < 32; i++) evalLevels[i] = ReadEvalLevel(r);

        // Eval: array[1..32, 0..6] of single (32 × 7 × 4 = 896 bytes, 4-byte align)
        var evals = new EvalResult[32];
        for (int i = 0; i < 32; i++) evals[i] = ReadEvalResult7Single(r);

        bool irrelevant = r.ReadBoolean();
        sbyte met       = r.ReadShortInt(); // unused
        sbyte choice0   = r.ReadShortInt();
        sbyte choice3   = r.ReadShortInt();

        return new BestMoveAnalysis
        {
            Position        = pos,
            Dice            = [dice1, dice2],
            Level           = level,
            Score           = [sc1, sc2],
            Cube            = cube,
            CubePosition    = cubePos,
            Crawford        = crawford,
            Jacoby          = jacoby,
            MoveCount       = nMoves,
            PositionsPlayed = posPlayed,
            Moves           = movesList,
            EvalLevels      = evalLevels,
            Evals           = evals,
            Irrelevant      = irrelevant,
            Choice1Ply      = choice0,
            Choice3Ply      = choice3,
        };
    }
}

/// <summary>Placeholder for unrecognised record types.</summary>
internal sealed class UnknownRecord : SaveRecord
{
    public UnknownRecord(RecordType entryType) { EntryType = entryType; }
    public UnknownRecord() : this(RecordType.Missing) { }
}

/// <summary>
/// A read-only view into a parent stream, with its own Position counter
/// starting at 0 relative to the sub-region.  Used so that PascalBinaryReader
/// alignment is relative to the record start, not the file start.
/// </summary>
internal sealed class SubStream(Stream parent, long origin, long length, bool leaveOpen = false) : Stream
{
    private long _position;

    public override bool CanRead  => true;
    public override bool CanSeek  => true;
    public override bool CanWrite => false;
    public override long Length   => length;

    public override long Position
    {
        get => _position;
        set
        {
            _position = value;
            parent.Position = origin + value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        long remaining = length - _position;
        if (remaining <= 0) return 0;
        int toRead = (int)Math.Min(count, remaining);
        parent.Position = origin + _position;
        int read = parent.Read(buffer, offset, toRead);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin2)
    {
        long newPos = origin2 switch
        {
            SeekOrigin.Begin   => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End     => length + offset,
            _ => throw new ArgumentOutOfRangeException()
        };
        Position = newPos;
        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen) parent.Dispose();
        base.Dispose(disposing);
    }
}
