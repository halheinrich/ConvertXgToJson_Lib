using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;

namespace ConvertXgToJson_Lib.Writing;

/// <summary>
/// Serializes <see cref="SaveRecord"/> variants into complete 2560-byte
/// TSaveRec records — the byte-layout mirror of
/// <see cref="SaveRecordParser"/>. Every write sequence matches the parser's
/// read sequence call-for-call, so field offsets agree by construction; the
/// per-record round-trip tests pin that agreement field-by-field.
///
/// Regions the parser never reads are written as zeros. Real XG files carry
/// only two kinds of content there: uninitialized heap noise (the 8-byte
/// Previous/Next pointer preamble, Eval/EvalLevel slots beyond MoveCount)
/// and fields XG's own Delphi source marks unused (`met`). XG rebuilds the
/// pointers on load and ignores the rest, so zero-fill is both safe and the
/// cleanest deterministic choice. Verified against the fixture corpus: no
/// real file carries meaningful data outside the parsed extent.
/// </summary>
internal static class SaveRecordWriter
{
    /// <summary>
    /// Serializes <paramref name="records"/> into a contiguous byte buffer,
    /// one 2560-byte record each, in order.
    /// </summary>
    internal static byte[] WriteAll(IReadOnlyList<SaveRecord> records)
    {
        byte[] buffer = new byte[records.Count * SaveRecordParser.RecordSize];
        for (int i = 0; i < records.Count; i++)
            WriteOne(records[i], buffer.AsSpan(i * SaveRecordParser.RecordSize, SaveRecordParser.RecordSize));
        return buffer;
    }

    /// <summary>Serializes a single record into exactly 2560 bytes.</summary>
    internal static byte[] Write(SaveRecord record)
    {
        byte[] buffer = new byte[SaveRecordParser.RecordSize];
        WriteOne(record, buffer);
        return buffer;
    }

    private static void WriteOne(SaveRecord record, Span<byte> target)
    {
        byte[] buffer = new byte[SaveRecordParser.RecordSize];
        using var ms = new MemoryStream(buffer);   // fixed-size: overrun throws
        using var w = new PascalBinaryWriter(ms);

        // Fixed preamble: Previous/Next in-memory pointers (XG rebuilds them
        // on load; real files carry heap garbage here) + EntryType at offset 8.
        w.WriteDword(0);
        w.WriteDword(0);
        w.WriteByte((byte)record.EntryType);

        switch (record)
        {
            case MatchHeaderRecord mh: WriteHeaderMatch(w, mh); break;
            case GameHeaderRecord gh:  WriteHeaderGame(w, gh);  break;
            case CubeRecord cb:        WriteCube(w, cb);        break;
            case MoveRecord mv:        WriteMove(w, mv);        break;
            case GameFooterRecord gf:  WriteFooterGame(w, gf);  break;
            case MatchFooterRecord mf: WriteFooterMatch(w, mf); break;
            default: break; // UnknownRecord: preamble + type only, rest zeros
        }

        buffer.CopyTo(target);
    }

    // ------------------------------------------------------------------
    //  tsHeaderMatch — mirror of SaveRecordParser.ReadHeaderMatch
    // ------------------------------------------------------------------
    private static void WriteHeaderMatch(PascalBinaryWriter w, MatchHeaderRecord mh)
    {
        w.WritePascalAnsiString(mh.Player1Ansi, 40);
        w.WritePascalAnsiString(mh.Player2Ansi, 40);
        w.WriteInteger(mh.MatchLength);
        w.WriteInteger(mh.Variation);
        w.WriteBoolean(mh.Crawford);
        w.WriteBoolean(mh.Jacoby);
        w.WriteBoolean(mh.Beaver);
        w.WriteBoolean(mh.AutoDouble);
        w.WriteDouble(mh.Elo1);
        w.WriteDouble(mh.Elo2);
        w.WriteInteger(mh.Experience1);
        w.WriteInteger(mh.Experience2);
        w.WriteTDateTime(mh.Date);
        w.WritePascalAnsiString(mh.EventAnsi, 128);
        w.WriteInteger(mh.GameId);
        w.WriteInteger(mh.CompLevel1);
        w.WriteInteger(mh.CompLevel2);
        w.WriteBoolean(mh.CountForElo);
        w.WriteBoolean(mh.AddToProfile1);
        w.WriteBoolean(mh.AddToProfile2);
        w.WritePascalAnsiString(mh.LocationAnsi, 128);
        w.WriteInteger((int)mh.GameMode);
        w.WriteBoolean(mh.Imported);
        w.WritePascalAnsiString(mh.RoundAnsi, 128);
        w.WriteInteger(mh.Invert);
        w.WriteInteger(mh.Version);
        w.WriteInteger(mh.Magic);
        w.WriteInteger(mh.MoneyInitGames);
        w.WriteInteger(mh.MoneyInitScore.Length > 0 ? mh.MoneyInitScore[0] : 0);
        w.WriteInteger(mh.MoneyInitScore.Length > 1 ? mh.MoneyInitScore[1] : 0);
        w.WriteBoolean(mh.Entered);
        w.WriteBoolean(mh.Counted);
        w.WriteBoolean(mh.UnratedImport);
        w.WriteInteger(mh.CommentHeaderMatchIndex);
        w.WriteInteger(mh.CommentFooterMatchIndex);
        w.WriteBoolean(mh.IsMoneyMatch);
        w.WriteSingle(mh.WinMoney);
        w.WriteSingle(mh.LoseMoney);
        w.WriteInteger((int)mh.Currency);
        w.WriteSingle(mh.FeeMoney);
        w.WriteSingle(mh.TableStake);
        w.WriteInteger((int)mh.SiteId);
        w.WriteInteger(mh.CubeLimit);
        w.WriteInteger(mh.AutoDoubleMax);
        w.WriteBoolean(mh.Transcribed);
        w.WriteShortUnicodeString(mh.Event);
        w.WriteShortUnicodeString(mh.Player1);
        w.WriteShortUnicodeString(mh.Player2);
        w.WriteShortUnicodeString(mh.Location);
        w.WriteShortUnicodeString(mh.Round);
        WriteTimeSetting(w, mh.TimeSetting);
        w.WriteInteger(mh.TotalTimeDelayMoves);
        w.WriteInteger(mh.TotalTimeDelayCubes);
        w.WriteInteger(mh.TotalTimeDelayMovesDone);
        w.WriteInteger(mh.TotalTimeDelayCubesDone);
        w.WriteShortUnicodeString(mh.Transcriber);
    }

    // ------------------------------------------------------------------
    //  tsHeaderGame — mirror of SaveRecordParser.ReadHeaderGame
    // ------------------------------------------------------------------
    private static void WriteHeaderGame(PascalBinaryWriter w, GameHeaderRecord gh)
    {
        w.WriteInteger(gh.Score1);
        w.WriteInteger(gh.Score2);
        w.WriteBoolean(gh.CrawfordApplies);
        WritePosition(w, gh.InitialPosition);
        w.WriteInteger(gh.GameNumber);
        w.WriteBoolean(gh.InProgress);
        w.WriteInteger(gh.CommentHeaderGameIndex);
        w.WriteInteger(gh.CommentFooterGameIndex);
        w.WriteInteger(gh.NumberOfAutoDoubles);
    }

    // ------------------------------------------------------------------
    //  tsCube — mirror of SaveRecordParser.ReadCube
    // ------------------------------------------------------------------
    private static void WriteCube(PascalBinaryWriter w, CubeRecord cb)
    {
        w.WriteInteger(cb.ActivePlayer);
        w.WriteInteger(cb.Doubled);
        w.WriteInteger(cb.Taken);
        w.WriteInteger(cb.BeaverAccepted);
        w.WriteInteger(cb.RaccoonAccepted);
        w.WriteInteger(cb.CubeValue);
        WritePosition(w, cb.Position);
        w.AlignTo(4);
        WriteDoubleAction(w, cb.Analysis);
        w.WriteDouble(cb.ErrorCube);
        w.WritePascalAnsiString(cb.DiceRolled, 2);
        w.WriteDouble(cb.ErrorTake);
        w.WriteInteger(cb.RolloutIndex);
        w.WriteInteger(cb.ComputerChoice);
        w.WriteInteger(cb.AnalyzeLevel);
        w.WriteDouble(cb.ErrorBeaver);
        w.WriteDouble(cb.ErrorRaccoon);
        w.WriteInteger(cb.AnalyzeLevelRequested);
        w.WriteInteger(cb.InvalidDecision);
        w.WriteShortInt(cb.TutorCube);
        w.WriteShortInt(cb.TutorTake);
        w.WriteDouble(cb.ErrorTutorCube);
        w.WriteDouble(cb.ErrorTutorTake);
        w.WriteBoolean(cb.Flagged);
        w.WriteInteger(cb.CommentIndex);
        w.WriteBoolean(cb.Edited);
        w.WriteBoolean(cb.TimeDelayed);
        w.WriteBoolean(cb.TimeDelayDone);
        w.WriteInteger(cb.NumberOfAutoDoubles);
        w.WriteInteger(cb.TimeBotLeft);
        w.WriteInteger(cb.TimeTopLeft);
    }

    // ------------------------------------------------------------------
    //  tsMove — mirror of SaveRecordParser.ReadMove
    // ------------------------------------------------------------------
    private static void WriteMove(PascalBinaryWriter w, MoveRecord mv)
    {
        WritePosition(w, mv.InitialPosition);
        WritePosition(w, mv.FinalPosition);
        w.WriteInteger(mv.ActivePlayer);
        for (int i = 0; i < 8; i++)
            w.WriteInteger(i < mv.MoveList.Length ? mv.MoveList[i] : 0);
        w.WriteInteger(mv.Dice.Length > 0 ? mv.Dice[0] : 0);
        w.WriteInteger(mv.Dice.Length > 1 ? mv.Dice[1] : 0);
        w.WriteInteger(mv.CubeValue);
        w.WriteDouble(mv.ErrorMove);
        w.WriteInteger(mv.CandidateCount);
        w.AlignTo(4);
        WriteBestMove(w, mv.Analysis);
        w.WriteBoolean(mv.Played);
        w.WriteDouble(mv.MoveError);
        w.WriteDouble(mv.LuckError);
        w.WriteInteger(mv.ComputerChoice);
        w.WriteDouble(mv.InitialEquity);
        for (int i = 0; i < 32; i++)
            w.WriteInteger(i < mv.RolloutIndices.Length ? mv.RolloutIndices[i] : -1);
        w.WriteInteger(mv.AnalyzeLevel);
        w.WriteInteger(mv.AnalyzeLevelLuck);
        w.WriteInteger(mv.InvalidDecision);
        WritePosition(w, mv.TutorPosition);
        w.WriteShortInt(mv.TutorMoveIndex);
        w.WriteDouble(mv.ErrorTutorMove);
        w.WriteBoolean(mv.Flagged);
        w.WriteInteger(mv.CommentIndex);
        w.WriteBoolean(mv.Edited);
        w.WriteDword(mv.TimeDelayBits);
        w.WriteDword(mv.TimeDelayDoneBits);
        w.WriteInteger(mv.NumberOfAutoDoubles);
        // Filler: array[1..4] of integer
        for (int i = 0; i < 4; i++) w.WriteInteger(0);
    }

    // ------------------------------------------------------------------
    //  tsFooterGame — mirror of SaveRecordParser.ReadFooterGame
    // ------------------------------------------------------------------
    private static void WriteFooterGame(PascalBinaryWriter w, GameFooterRecord gf)
    {
        w.WriteInteger(gf.Score1);
        w.WriteInteger(gf.Score2);
        w.WriteBoolean(gf.CrawfordAppliesNext);
        w.WriteInteger(gf.Winner);
        w.WriteInteger(gf.PointsWon);
        w.WriteInteger(gf.Termination);
        w.WriteDouble(gf.ErrorResign);
        w.WriteDouble(gf.ErrorTakeResign);
        for (int i = 0; i < 7; i++)
            w.WriteDouble(i < gf.FinalEval.Length ? gf.FinalEval[i] : 0.0);
        w.WriteInteger(gf.EvalLevel);
    }

    // ------------------------------------------------------------------
    //  tsFooterMatch — mirror of SaveRecordParser.ReadFooterMatch
    // ------------------------------------------------------------------
    private static void WriteFooterMatch(PascalBinaryWriter w, MatchFooterRecord mf)
    {
        w.WriteInteger(mf.Score1);
        w.WriteInteger(mf.Score2);
        w.WriteInteger(mf.Winner);
        w.WriteDouble(mf.Elo1);
        w.WriteDouble(mf.Elo2);
        w.WriteInteger(mf.Exp1);
        w.WriteInteger(mf.Exp2);
        w.WriteTDateTime(mf.Date);
    }

    // ------------------------------------------------------------------
    //  Shared sub-structure writers — mirrors of the parser's counterparts
    // ------------------------------------------------------------------

    private static void WritePosition(PascalBinaryWriter w, PositionEngine pos)
    {
        for (int i = 0; i < 26; i++)
            w.WriteShortInt(i < pos.Points.Length ? pos.Points[i] : (sbyte)0);
    }

    private static void WriteEvalResult7Single(PascalBinaryWriter w, EvalResult e)
    {
        w.WriteSingle(e.LoseBackgammon);
        w.WriteSingle(e.LoseGammon);
        w.WriteSingle(e.LoseSingle);
        w.WriteSingle(e.WinSingle);
        w.WriteSingle(e.WinGammon);
        w.WriteSingle(e.WinBackgammon);
        w.WriteSingle(e.Equity);
    }

    private static void WriteEvalLevel(PascalBinaryWriter w, EvalLevel e)
    {
        w.WriteSmallInt(e.Level);
        w.WriteBoolean(e.IsDouble);
        w.WriteByte(0); // filler
    }

    private static void WriteTimeSetting(PascalBinaryWriter w, TimeSetting t)
    {
        w.WriteInteger((int)t.ClockType);
        w.WriteBoolean(t.PerGame);
        w.WriteInteger(t.Time1);
        w.WriteInteger(t.Time2);
        w.WriteInteger(t.Penalty);
        w.WriteInteger(t.TimeLeft1);
        w.WriteInteger(t.TimeLeft2);
        w.WriteInteger(t.PenaltyMoney);
    }

    private static void WriteDoubleAction(PascalBinaryWriter w, DoubleActionAnalysis a)
    {
        WritePosition(w, a.Position);
        w.WriteInteger(a.Level);
        w.WriteInteger(a.Score.Length > 0 ? a.Score[0] : 0);
        w.WriteInteger(a.Score.Length > 1 ? a.Score[1] : 0);
        w.WriteInteger(a.Cube);
        w.WriteInteger(a.CubePosition);
        w.WriteSmallInt((short)a.Jacoby);
        w.WriteSmallInt(0); // met (unused; parser discards it)
        w.WriteSmallInt(a.Crawford);
        w.WriteSmallInt(a.FlagDouble);
        w.WriteSmallInt(a.IsBeaver);
        WriteEvalResult7Single(w, a.EvalNoDouble);
        w.WriteSingle(a.EquityNoDouble);
        w.WriteSingle(a.EquityDoubleTake);
        w.WriteSingle(a.EquityDoubleDrop);
        w.WriteSmallInt(a.LevelRequest);
        w.WriteSmallInt(a.DoubleChoice3);
        WriteEvalResult7Single(w, a.EvalDoubleTake);
    }

    private static void WriteBestMove(PascalBinaryWriter w, BestMoveAnalysis a)
    {
        WritePosition(w, a.Position);
        w.WriteInteger(a.Dice.Length > 0 ? a.Dice[0] : 0);
        w.WriteInteger(a.Dice.Length > 1 ? a.Dice[1] : 0);
        w.WriteInteger(a.Level);
        w.WriteInteger(a.Score.Length > 0 ? a.Score[0] : 0);
        w.WriteInteger(a.Score.Length > 1 ? a.Score[1] : 0);
        w.WriteInteger(a.Cube);
        w.WriteInteger(a.CubePosition);
        w.WriteInteger(a.Crawford);
        w.WriteInteger(a.Jacoby);
        w.WriteInteger(a.MoveCount);

        // PosPlayed: array[1..32] of PositionEngine
        for (int i = 0; i < 32; i++)
            WritePosition(w, i < a.PositionsPlayed.Length ? a.PositionsPlayed[i] : EmptyPosition);

        // Moves: array[1..32, 1..8] of ShortInt
        for (int i = 0; i < 32; i++)
        {
            sbyte[] moves = i < a.Moves.Length ? a.Moves[i] : [];
            for (int j = 0; j < 8; j++)
                w.WriteShortInt(j < moves.Length ? moves[j] : (sbyte)0);
        }

        // EvalLevel: array[1..32] of TEvalLevel
        for (int i = 0; i < 32; i++)
            WriteEvalLevel(w, i < a.EvalLevels.Length ? a.EvalLevels[i] : EmptyEvalLevel);

        // Eval: array[1..32, 0..6] of single
        for (int i = 0; i < 32; i++)
            WriteEvalResult7Single(w, i < a.Evals.Length ? a.Evals[i] : EmptyEval);

        w.WriteBoolean(a.Irrelevant);
        w.WriteShortInt(0); // met (unused; parser discards it)
        w.WriteShortInt(a.Choice1Ply);
        w.WriteShortInt(a.Choice3Ply);
    }

    private static readonly PositionEngine EmptyPosition = new();
    private static readonly EvalLevel EmptyEvalLevel = new();
    private static readonly EvalResult EmptyEval = new();
}
