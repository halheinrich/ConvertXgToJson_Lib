using FluentAssertions;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for TRolloutContext parsing.
/// </summary>
public class RolloutContextTests
{
    private const int RecordSize = 2184;

    // ------------------------------------------------------------------ //
    //  Build a known TRolloutContext byte array
    // ------------------------------------------------------------------ //

    private static byte[] BuildRolloutContextBytes(
        bool truncated     = true,
        bool errorLimited  = false,
        int  minRolls      = 1296,
        int  maxRolls      = 5000,
        double errorLimit  = 0.01,
        int  gamesRolled   = 2500,
        float error1       = 0.005f,
        ushort verMaj      = 2,
        ushort verMin      = 10)
    {
        var b = new BinaryBuilder();

        // --- inputs ---
        b.Bool(truncated)
         .Bool(errorLimited);
        // Truncate: int AlignTo4
        b.Int32(7)
         .Int32(minRolls)
         .Double(errorLimit)   // AlignTo8
         .Int32(maxRolls)
         .Int32(1)             // Level1
         .Int32(3)             // Level2
         .Int32(8)             // LevelCut
         .Bool(true)           // Variance
         .Bool(false)          // Cubeless
         .Bool(false)          // Time
         // Level1C: int AlignTo4
         .Int32(1)             // Level1C
         .Int32(3)             // Level2C
         .UInt32(60)           // TimeLimit (Dword)
         .Int32(0)             // TruncateBO
         .Int32(12345)         // RandomSeed
         .Int32(42)            // RandomSeedI
         .Bool(true)           // RollBoth
         // searchinterval: float AlignTo4
         .Float(1.0f)
         .Int32(0)             // met (unused)
         .Bool(false)          // FirstRoll
         .Bool(true)           // DoDouble
         .Bool(false);         // Extent

        // --- outputs ---
        // Rolled: int AlignTo4
        b.Int32(gamesRolled)
         .Bool(false);         // DoubleFirst

        // Arrays: 37 doubles each (AlignTo8 on first)
        for (int a = 0; a < 4; a++)
            for (int i = 0; i < 37; i++) b.Double(i * 0.001);

        // stdev1, stdev2 (37 doubles each)
        for (int a = 0; a < 2; a++)
            for (int i = 0; i < 37; i++) b.Double(0.0002);

        // RolledD: 37 ints
        for (int i = 0; i < 37; i++) b.Int32(gamesRolled / 36);

        // Error1, Error2: float AlignTo4
        b.Float(error1).Float(0.005f);

        // Result1, Result2: 7 floats each
        for (int i = 0; i < 14; i++) b.Float(i * 0.1f);

        b.Float(0.52f)  // Mwc1
         .Float(0.48f); // Mwc2

        b.Int32(3);             // PrevLevel
        for (int i = 0; i < 7; i++) b.Float(0f); // PrevEval
        b.Float(0.1f)           // PrevND
         .Float(0.08f)          // PrevD
         .Float(12.5f);         // Duration

        b.Int32(7)              // LevelTrunc
         .Int32(gamesRolled);   // Rolled2

        b.Int32(1296)           // MultipleMin
         .Bool(false)           // MultipleStopAll
         .Bool(false);          // MultipleStopOne
        // MultipleStopAllValue: float AlignTo4
        b.Float(99.9f)
         .Float(0.01f)          // MultipleStopOneValue
         .Bool(false);          // AsTake
        // Rotation: int AlignTo4
        b.Int32(0)
         .Bool(false);          // UserInterrupted
        // VerMaj, VerMin: Word AlignTo2
        b.UInt16(verMaj)
         .UInt16(verMin)
         .Int32(0)              // Fixed
         .Int32(0);             // Filler[1]

        b.PadTo(RecordSize);
        return b.ToArray();
    }

    // ------------------------------------------------------------------ //
    //  Tests
    // ------------------------------------------------------------------ //

    [Fact]
    public void RolloutContext_RecordSizeIs2184Bytes()
    {
        BuildRolloutContextBytes().Length.Should().Be(RecordSize);
    }

    [Fact]
    public void RolloutContext_TruncatedParsed()
    {
        var ctx = ParseOne(BuildRolloutContextBytes(truncated: true));
        ctx.Truncated.Should().BeTrue();
    }

    [Fact]
    public void RolloutContext_ErrorLimitedParsed()
    {
        var ctx = ParseOne(BuildRolloutContextBytes(errorLimited: false));
        ctx.ErrorLimited.Should().BeFalse();
    }

    [Fact]
    public void RolloutContext_MinRollsParsed()
    {
        var ctx = ParseOne(BuildRolloutContextBytes(minRolls: 648));
        ctx.MinRolls.Should().Be(648);
    }

    [Fact]
    public void RolloutContext_MaxRollsParsed()
    {
        var ctx = ParseOne(BuildRolloutContextBytes(maxRolls: 10000));
        ctx.MaxRolls.Should().Be(10000);
    }

    [Fact]
    public void RolloutContext_ErrorLimitParsed()
    {
        var ctx = ParseOne(BuildRolloutContextBytes(errorLimit: 0.025));
        ctx.ErrorLimit.Should().BeApproximately(0.025, 1e-10);
    }

    [Fact]
    public void RolloutContext_GamesRolledParsed()
    {
        var ctx = ParseOne(BuildRolloutContextBytes(gamesRolled: 3600));
        ctx.GamesRolled.Should().Be(3600);
    }

    [Fact]
    public void RolloutContext_Error1Parsed()
    {
        var ctx = ParseOne(BuildRolloutContextBytes(error1: 0.007f));
        ctx.Error1.Should().BeApproximately(0.007f, 1e-6f);
    }

    [Fact]
    public void RolloutContext_VersionMajorParsed()
    {
        var ctx = ParseOne(BuildRolloutContextBytes(verMaj: 2));
        ctx.VersionMajor.Should().Be(2);
    }

    [Fact]
    public void RolloutContext_VersionMinorParsed()
    {
        var ctx = ParseOne(BuildRolloutContextBytes(verMin: 10));
        ctx.VersionMinor.Should().Be(10);
    }

    [Fact]
    public void RolloutContext_Sum1ArrayHas37Elements()
    {
        var ctx = ParseOne(BuildRolloutContextBytes());
        ctx.Sum1.Should().HaveCount(37);
    }

    [Fact]
    public void RolloutContext_Result1ArrayHas7Elements()
    {
        var ctx = ParseOne(BuildRolloutContextBytes());
        ctx.Result1.Should().HaveCount(7);
    }

    [Fact]
    public void RolloutContext_RolledPerDiceArrayHas37Elements()
    {
        var ctx = ParseOne(BuildRolloutContextBytes());
        ctx.RolledPerDice.Should().HaveCount(37);
    }

    [Fact]
    public void RolloutContextParser_ReadAll_ParsesMultipleRecords()
    {
        byte[] two = [.. BuildRolloutContextBytes(gamesRolled: 1000),
                      .. BuildRolloutContextBytes(gamesRolled: 2000)];
        var contexts = RolloutContextParser.ReadAll(new MemoryStream(two));
        contexts.Should().HaveCount(2);
        contexts[0].GamesRolled.Should().Be(1000);
        contexts[1].GamesRolled.Should().Be(2000);
    }

    // ------------------------------------------------------------------ //
    //  Helpers
    // ------------------------------------------------------------------ //

    private static RolloutContext ParseOne(byte[] bytes)
    {
        var list = RolloutContextParser.ReadAll(new MemoryStream(bytes));
        list.Should().HaveCount(1, "expected exactly one rollout context record");
        return list[0];
    }
}
