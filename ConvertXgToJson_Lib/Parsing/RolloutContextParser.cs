using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Parsing;

/// <summary>
/// Parses TRolloutContext records from temp.xgr.
/// Each record is exactly 2184 bytes.
/// </summary>
internal static class RolloutContextParser
{
    private const int RecordSize = 2184;

    public static List<RolloutContext> ReadAll(Stream stream)
    {
        var result = new List<RolloutContext>();

        while (stream.Length - stream.Position >= RecordSize)
        {
            long start = stream.Position;
            using var sub = new SubStream(stream, start, RecordSize, leaveOpen: true);
            using var r = new PascalBinaryReader(sub);
            result.Add(ReadOne(r));

            long remaining = RecordSize - (stream.Position - start);
            if (remaining > 0) stream.Seek(remaining, SeekOrigin.Current);
        }

        return result;
    }

    private static RolloutContext ReadOne(PascalBinaryReader r)
    {
        // --- inputs ---
        bool truncated    = r.ReadBoolean();
        bool errLimited   = r.ReadBoolean();
        // Truncate: integer (4-byte align)
        int truncate      = r.ReadInteger();
        int minRoll       = r.ReadInteger();
        // ErrorLimit: double (8-byte align)
        double errLimit   = r.ReadDouble();
        int maxRoll       = r.ReadInteger();
        int level1        = r.ReadInteger();
        int level2        = r.ReadInteger();
        int levelCut      = r.ReadInteger();
        bool variance     = r.ReadBoolean();
        bool cubeless     = r.ReadBoolean();
        bool timeLimited  = r.ReadBoolean();
        // Level1C: integer (4-byte align)
        int level1c       = r.ReadInteger();
        int level2c       = r.ReadInteger();
        // TimeLimit: Dword (4-byte align, same)
        uint timeLimit    = r.ReadDword();
        int truncBO       = r.ReadInteger();
        int rndSeed       = r.ReadInteger();
        int rndSeedI      = r.ReadInteger();
        bool rollBoth     = r.ReadBoolean();
        // searchinterval: single (4-byte align)
        float searchInt   = r.ReadSingle();
        int met           = r.ReadInteger(); // unused
        bool firstRoll    = r.ReadBoolean();
        bool doDouble     = r.ReadBoolean();
        bool extended     = r.ReadBoolean();

        // --- outputs ---
        // Rolled: integer (4-byte align)
        int rolled        = r.ReadInteger();
        bool dblFirst     = r.ReadBoolean();

        // Arrays of 37 doubles (0..36)
        double[] sum1        = ReadDoubleArray(r, 37);
        double[] sumSq1      = ReadDoubleArray(r, 37);
        double[] sum2        = ReadDoubleArray(r, 37);
        double[] sumSq2      = ReadDoubleArray(r, 37);
        double[] stdev1      = ReadDoubleArray(r, 37);
        double[] stdev2      = ReadDoubleArray(r, 37);
        // RolledD: array[0..36] of integer
        int[] rolledD        = ReadIntArray(r, 37);

        // Error1, Error2: single (4-byte align)
        float err1           = r.ReadSingle();
        float err2           = r.ReadSingle();

        // Result1, Result2: array[0..6] of single
        float[] result1      = ReadSingleArray(r, 7);
        float[] result2      = ReadSingleArray(r, 7);

        float mwc1           = r.ReadSingle();
        float mwc2           = r.ReadSingle();

        int prevLevel        = r.ReadInteger();
        float[] prevEval     = ReadSingleArray(r, 7);
        float prevND         = r.ReadSingle();
        float prevD          = r.ReadSingle();
        float duration       = r.ReadSingle();

        int levelTrunc       = r.ReadInteger();
        int rolled2          = r.ReadInteger();

        int multipleMin      = r.ReadInteger();
        bool multiStopAll    = r.ReadBoolean();
        bool multiStopOne    = r.ReadBoolean();
        // MultipleStopAllValue: single (4-byte align)
        float stopAllVal     = r.ReadSingle();
        float stopOneVal     = r.ReadSingle();
        bool asTake          = r.ReadBoolean();
        // Rotation: integer (4-byte align)
        int rotation         = r.ReadInteger();
        bool userInterrupted = r.ReadBoolean();
        // VerMaj, VerMin: Word (2-byte align)
        ushort verMaj        = r.ReadWord();
        ushort verMin        = r.ReadWord();
        int fixed0           = r.ReadInteger(); // unused
        // Filler: array[1..1] of integer
        r.ReadInteger();

        return new RolloutContext
        {
            Truncated             = truncated,
            ErrorLimited          = errLimited,
            TruncateLevel         = truncate,
            MinRolls              = minRoll,
            ErrorLimit            = errLimit,
            MaxRolls              = maxRoll,
            Level1                = level1,
            Level2                = level2,
            LevelCut              = levelCut,
            VarianceReduction     = variance,
            Cubeless              = cubeless,
            TimeLimited           = timeLimited,
            Level1Cube            = level1c,
            Level2Cube            = level2c,
            TimeLimit             = timeLimit,
            TruncateBO            = truncBO,
            RandomSeed            = rndSeed,
            RandomSeedInitial     = rndSeedI,
            RollBoth              = rollBoth,
            SearchInterval        = searchInt,
            FirstRoll             = firstRoll,
            DoDouble              = doDouble,
            Extended              = extended,
            GamesRolled           = rolled,
            DoubleFirst           = dblFirst,
            Sum1                  = sum1,
            SumSquare1            = sumSq1,
            Sum2                  = sum2,
            SumSquare2            = sumSq2,
            Stdev1                = stdev1,
            Stdev2                = stdev2,
            RolledPerDice         = rolledD,
            Error1                = err1,
            Error2                = err2,
            Result1               = result1,
            Result2               = result2,
            Mwc1                  = mwc1,
            Mwc2                  = mwc2,
            PrevLevel             = prevLevel,
            PrevEval              = prevEval,
            PrevND                = prevND,
            PrevD                 = prevD,
            Duration              = duration,
            LevelTrunc            = levelTrunc,
            GamesRolledDouble     = rolled2,
            MultipleMin           = multipleMin,
            MultipleStopAll       = multiStopAll,
            MultipleStopOne       = multiStopOne,
            MultipleStopAllValue  = stopAllVal,
            MultipleStopOneValue  = stopOneVal,
            AsTake                = asTake,
            Rotation              = rotation,
            UserInterrupted       = userInterrupted,
            VersionMajor          = verMaj,
            VersionMinor          = verMin,
        };
    }

    private static double[] ReadDoubleArray(PascalBinaryReader r, int count)
    {
        var arr = new double[count];
        for (int i = 0; i < count; i++) arr[i] = r.ReadDouble();
        return arr;
    }

    private static int[] ReadIntArray(PascalBinaryReader r, int count)
    {
        var arr = new int[count];
        for (int i = 0; i < count; i++) arr[i] = r.ReadInteger();
        return arr;
    }

    private static float[] ReadSingleArray(PascalBinaryReader r, int count)
    {
        var arr = new float[count];
        for (int i = 0; i < count; i++) arr[i] = r.ReadSingle();
        return arr;
    }
}
