using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;

namespace ConvertXgToJson_Lib.Writing;

/// <summary>
/// Serializes <see cref="RolloutContext"/> records into complete 2184-byte
/// TRolloutContext records — the byte-layout mirror of
/// <see cref="RolloutContextParser"/>. Fields the parser discards (`met`,
/// the trailing fixed/filler integers) are written as zeros; XG's own
/// source marks them unused.
/// </summary>
internal static class RolloutContextWriter
{
    /// <summary>
    /// Serializes <paramref name="rollouts"/> into a contiguous byte buffer,
    /// one 2184-byte record each, in order.
    /// </summary>
    internal static byte[] WriteAll(IReadOnlyList<RolloutContext> rollouts)
    {
        byte[] buffer = new byte[rollouts.Count * RolloutContextParser.RecordSize];
        for (int i = 0; i < rollouts.Count; i++)
        {
            using var ms = new MemoryStream(buffer, i * RolloutContextParser.RecordSize,
                RolloutContextParser.RecordSize, writable: true);
            using var w = new PascalBinaryWriter(ms);
            WriteOne(w, rollouts[i]);
        }
        return buffer;
    }

    private static void WriteOne(PascalBinaryWriter w, RolloutContext rc)
    {
        // --- inputs ---
        w.WriteBoolean(rc.Truncated);
        w.WriteBoolean(rc.ErrorLimited);
        w.WriteInteger(rc.TruncateLevel);
        w.WriteInteger(rc.MinRolls);
        w.WriteDouble(rc.ErrorLimit);
        w.WriteInteger(rc.MaxRolls);
        w.WriteInteger(rc.Level1);
        w.WriteInteger(rc.Level2);
        w.WriteInteger(rc.LevelCut);
        w.WriteBoolean(rc.VarianceReduction);
        w.WriteBoolean(rc.Cubeless);
        w.WriteBoolean(rc.TimeLimited);
        w.WriteInteger(rc.Level1Cube);
        w.WriteInteger(rc.Level2Cube);
        w.WriteDword(rc.TimeLimit);
        w.WriteInteger(rc.TruncateBO);
        w.WriteInteger(rc.RandomSeed);
        w.WriteInteger(rc.RandomSeedInitial);
        w.WriteBoolean(rc.RollBoth);
        w.WriteSingle(rc.SearchInterval);
        w.WriteInteger(0); // met (unused; parser discards it)
        w.WriteBoolean(rc.FirstRoll);
        w.WriteBoolean(rc.DoDouble);
        w.WriteBoolean(rc.Extended);

        // --- outputs ---
        w.WriteInteger(rc.GamesRolled);
        w.WriteBoolean(rc.DoubleFirst);

        WriteDoubleArray(w, rc.Sum1, 37);
        WriteDoubleArray(w, rc.SumSquare1, 37);
        WriteDoubleArray(w, rc.Sum2, 37);
        WriteDoubleArray(w, rc.SumSquare2, 37);
        WriteDoubleArray(w, rc.Stdev1, 37);
        WriteDoubleArray(w, rc.Stdev2, 37);
        WriteIntArray(w, rc.RolledPerDice, 37);

        w.WriteSingle(rc.Error1);
        w.WriteSingle(rc.Error2);

        WriteSingleArray(w, rc.Result1, 7);
        WriteSingleArray(w, rc.Result2, 7);

        w.WriteSingle(rc.Mwc1);
        w.WriteSingle(rc.Mwc2);

        w.WriteInteger(rc.PrevLevel);
        WriteSingleArray(w, rc.PrevEval, 7);
        w.WriteSingle(rc.PrevND);
        w.WriteSingle(rc.PrevD);
        w.WriteSingle(rc.Duration);

        w.WriteInteger(rc.LevelTrunc);
        w.WriteInteger(rc.GamesRolledDouble);

        w.WriteInteger(rc.MultipleMin);
        w.WriteBoolean(rc.MultipleStopAll);
        w.WriteBoolean(rc.MultipleStopOne);
        w.WriteSingle(rc.MultipleStopAllValue);
        w.WriteSingle(rc.MultipleStopOneValue);
        w.WriteBoolean(rc.AsTake);
        w.WriteInteger(rc.Rotation);
        w.WriteBoolean(rc.UserInterrupted);
        w.WriteWord(rc.VersionMajor);
        w.WriteWord(rc.VersionMinor);
        w.WriteInteger(0); // fixed (unused; parser discards it)
        w.WriteInteger(0); // Filler: array[1..1] of integer
    }

    private static void WriteDoubleArray(PascalBinaryWriter w, double[] values, int count)
    {
        for (int i = 0; i < count; i++)
            w.WriteDouble(i < values.Length ? values[i] : 0.0);
    }

    private static void WriteIntArray(PascalBinaryWriter w, int[] values, int count)
    {
        for (int i = 0; i < count; i++)
            w.WriteInteger(i < values.Length ? values[i] : 0);
    }

    private static void WriteSingleArray(PascalBinaryWriter w, float[] values, int count)
    {
        for (int i = 0; i < count; i++)
            w.WriteSingle(i < values.Length ? values[i] : 0f);
    }
}
