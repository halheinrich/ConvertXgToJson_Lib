using FluentAssertions;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Targeted tests for Pascal record alignment edge cases.
/// Each test isolates a specific padding scenario from the spec.
/// </summary>
public class PascalAlignmentTests
{
    // ------------------------------------------------------------------ //
    //  The spec's own example: the TSaveRec field layout offsets
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Verifies: after Previous(4) + Next(4) + EntryType(1) = offset 9,
    /// then SPlayer1 string[40] = 41 bytes at offset 9,
    /// SPlayer2 string[40] = 41 bytes at offset 50,
    /// MatchLength integer needs AlignTo(4) from offset 91 → 1 pad byte → starts at 92.
    /// The spec shows MatchLength Start=92.
    /// </summary>
    [Fact]
    public void MatchHeaderRecord_MatchLengthStartsAt92()
    {
        byte[] bytes = XgFileBuilder.BuildMatchHeaderRecord("Alice", "Bob", 99, DateTime.UtcNow);
        // SPlayer1 at 9, length=41 → ends at 49 (inclusive) = offset 50
        // SPlayer2 at 50, length=41 → ends at 90 (inclusive) = offset 91
        // MatchLength: AlignTo(4) from 91 → pad 1 → starts at 92
        int matchLength = BitConverter.ToInt32(bytes, 92);
        matchLength.Should().Be(99);
    }

    /// <summary>
    /// Spec says: Elo1 Double starts at offset 104 (no alignment needed as 104 is multiple of 8).
    /// Previous 4 bool fields (Crawford..AutoDouble) end at offset 103.
    /// 104 is already a multiple of 8 so no padding is inserted.
    /// </summary>
    [Fact]
    public void MatchHeaderRecord_Elo1StartsAt104()
    {
        byte[] bytes = XgFileBuilder.BuildMatchHeaderRecord("X", "Y", 1, DateTime.UtcNow);
        // Offset 104 in the record (positions relative to record start byte 0)
        double elo1 = BitConverter.ToDouble(bytes, 104);
        elo1.Should().BeApproximately(1500.0, 1e-9);
    }

    /// <summary>
    /// After SEvent string[128] (129 bytes) ending at offset 265,
    /// GameId integer needs AlignTo(4) → 3 pad bytes → starts at 268.
    /// The spec explicitly notes this: "needs 3 bytes padding to start on a multiple of 4".
    /// </summary>
    [Fact]
    public void MatchHeaderRecord_GameIdStartsAt268()
    {
        byte[] bytes = XgFileBuilder.BuildMatchHeaderRecord("X", "Y", 1, DateTime.UtcNow);
        int gameId = BitConverter.ToInt32(bytes, 268);
        gameId.Should().Be(42);
    }

    // ------------------------------------------------------------------ //
    //  Boolean does not align
    // ------------------------------------------------------------------ //

    [Fact]
    public void BooleanFields_DoNotInsertPadding_CrawfordToAutoDouble()
    {
        // Crawford(101), Jacoby(102), Beaver(103), AutoDouble(104-1=103 then Elo1@104)
        // The four booleans should pack consecutively with no gaps.
        byte[] bytes = XgFileBuilder.BuildMatchHeaderRecord("A", "B", 7, DateTime.UtcNow);
        // Crawford=true, Jacoby=false, Beaver=false, AutoDouble=false
        bytes[100].Should().Be(1); // Crawford = true
        bytes[101].Should().Be(0); // Jacoby   = false
        bytes[102].Should().Be(0); // Beaver    = false
        bytes[103].Should().Be(0); // AutoDouble= false
    }

    // ------------------------------------------------------------------ //
    //  SmallInt aligns to 2 bytes
    // ------------------------------------------------------------------ //

    [Fact]
    public void SmallInt_AlignsTo2ByteBoundary()
    {
        // In TEvalLevel: Level SmallInt(2) + isDouble Boolean(1) + Fill1 byte(1) = 4 bytes
        // SmallInt must be at an even offset.
        var b = new BinaryBuilder()
            .Byte(0xAA)        // odd position (offset 1)
            .Int16(1234);      // AlignTo(2) → pad 1 → SmallInt at offset 2

        var bytes = b.ToArray();
        bytes.Length.Should().Be(4); // 1 + 1 pad + 2
        BitConverter.ToInt16(bytes, 2).Should().Be(1234);
    }

    // ------------------------------------------------------------------ //
    //  Integer aligns to 4 bytes
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData(1)]   // 1 byte before → 3 bytes padding
    [InlineData(2)]   // 2 bytes before → 2 bytes padding
    [InlineData(3)]   // 3 bytes before → 1 byte padding
    [InlineData(4)]   // 4 bytes before → 0 bytes padding (already aligned)
    public void Integer_AlignsTo4ByteBoundary_WithCorrectPadding(int prePadBytes)
    {
        var b = new BinaryBuilder();
        for (int i = 0; i < prePadBytes; i++) b.Byte(0xFF);
        b.Int32(9876);

        var bytes = b.ToArray();
        int expectedOffset = prePadBytes + ((4 - prePadBytes % 4) % 4);
        BitConverter.ToInt32(bytes, expectedOffset).Should().Be(9876);
    }

    // ------------------------------------------------------------------ //
    //  Double aligns to 8 bytes
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void Double_AlignsTo8ByteBoundary(int prePadBytes)
    {
        var b = new BinaryBuilder();
        for (int i = 0; i < prePadBytes; i++) b.Byte(0x00);
        b.Double(3.14159265358979);

        var bytes = b.ToArray();
        int expectedOffset = prePadBytes + ((8 - prePadBytes % 8) % 8);
        BitConverter.ToDouble(bytes, expectedOffset).Should().BeApproximately(3.14159265358979, 1e-14);
    }

    // ------------------------------------------------------------------ //
    //  WideChar array aligns to 2 bytes
    // ------------------------------------------------------------------ //

    [Fact]
    public void WideCharArray_AlignsTo2Bytes_FromOddOffset()
    {
        var b = new BinaryBuilder()
            .Byte(0x01)                // offset 1 (odd)
            .WideCharArray("Hi", 4);   // AlignTo(2) → pad 1 byte → starts at 2

        var bytes = b.ToArray();
        // After 1 byte + 1 pad, the UTF-16LE encoding of "Hi" starts at offset 2
        bytes[2].Should().Be((byte)'H');
        bytes[3].Should().Be(0x00); // high byte of 'H' in UTF-16LE
        bytes[4].Should().Be((byte)'i');
    }

    // ------------------------------------------------------------------ //
    //  String[N] does NOT align
    // ------------------------------------------------------------------ //

    [Fact]
    public void PascalString_DoesNotAlign_StartsImmediatelyAfterPreviousField()
    {
        // After a bool (offset 1), string[5] starts at offset 1 with no padding
        var b = new BinaryBuilder()
            .Bool(true)
            .PascalAnsiString("ABC", 5);

        var bytes = b.ToArray();
        // bool at 0, string length at 1, body at 2..6
        bytes[0].Should().Be(1);    // bool true
        bytes[1].Should().Be(3);    // string length = 3
        bytes[2].Should().Be((byte)'A');
    }

    // ------------------------------------------------------------------ //
    //  TDateTime round-trip via BinaryBuilder
    // ------------------------------------------------------------------ //

    [Fact]
    public void TDateTime_RoundTrip_SpecExample()
    {
        // Spec: 35065.541667 = January 1, 1996; 1:00 PM
        var b = new BinaryBuilder().Double(35065.541667);
        using var r = new Parsing.PascalBinaryReader(b.ToStream());
        var dt = r.ReadTDateTime();
        dt.Year.Should().Be(1996);
        dt.Month.Should().Be(1);
        dt.Day.Should().Be(1);
        dt.Hour.Should().Be(13);
    }

    [Fact]
    public void TDateTime_RoundTrip_ModernDate()
    {
        var expected = new DateTime(2024, 6, 15, 9, 30, 0, DateTimeKind.Utc);
        var b = new BinaryBuilder().TDateTime(expected);
        using var r = new Parsing.PascalBinaryReader(b.ToStream());
        var actual = r.ReadTDateTime();
        actual.Year.Should().Be(expected.Year);
        actual.Month.Should().Be(expected.Month);
        actual.Day.Should().Be(expected.Day);
        actual.Hour.Should().Be(expected.Hour);
        actual.Minute.Should().Be(expected.Minute);
    }

    // ------------------------------------------------------------------ //
    //  PositionEngine (26 signed bytes, no alignment)
    // ------------------------------------------------------------------ //

    [Fact]
    public void PositionEngine_26Bytes_NoAlignment()
    {
        sbyte[] pts = new sbyte[26];
        pts[1] = 2; pts[6] = -5; pts[8] = -3; pts[13] = 5; pts[24] = -2; pts[25] = 0;

        var b = new BinaryBuilder()
            .Bool(true)          // offset 0
            .WritePosition(pts); // offset 1 (no align! Position is array of ShortInt)

        var bytes = b.ToArray();
        bytes.Length.Should().Be(27); // 1 bool + 26 bytes, no padding
        ((sbyte)bytes[2]).Should().Be(2);   // pts[1] at byte offset 2
        ((sbyte)bytes[7]).Should().Be(-5);  // pts[6]
    }

    // ------------------------------------------------------------------ //
    //  Compound: the spec's documented example offsets
    // ------------------------------------------------------------------ //

    [Fact]
    public void SpecExample_MatchHeaderOffsets_AllCorrect()
    {
        // Verify all spec-documented offsets in a single sweep:
        // Previous(0-3) Next(4-7) EntryType(8) SPlayer1(9-49) SPlayer2(50-90)
        // pad(91) MatchLength(92-95) Variation(96-99)
        // Crawford(100) Jacoby(101) Beaver(102) AutoDouble(103)
        // Elo1(104-111) Elo2(112-119) exp1(120-123) exp2(124-127)
        // Date(128-135) SEvent(136-264) pad3(265-267) GameId(268-271)

        byte[] bytes = XgFileBuilder.BuildMatchHeaderRecord(
            "Alice", "Bob", 11, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        bytes[8].Should().Be(0, "EntryType=tsHeaderMatch(0) at offset 8");

        // MatchLength at 92
        BitConverter.ToInt32(bytes, 92).Should().Be(11, "MatchLength at offset 92");

        // Crawford at 100
        bytes[100].Should().Be(1, "Crawford=true at offset 100");

        // Elo1 at 104
        BitConverter.ToDouble(bytes, 104).Should().BeApproximately(1500.0, 1e-9, "Elo1 at offset 104");

        // Elo2 at 112
        BitConverter.ToDouble(bytes, 112).Should().BeApproximately(1450.0, 1e-9, "Elo2 at offset 112");

        // exp1 at 120, exp2 at 124
        BitConverter.ToInt32(bytes, 120).Should().Be(100, "exp1 at offset 120");
        BitConverter.ToInt32(bytes, 124).Should().Be(200, "exp2 at offset 124");

        // Date at 128
        double dateDouble = BitConverter.ToDouble(bytes, 128);
        dateDouble.Should().BeGreaterThan(0, "Date double at offset 128 should be non-zero");

        // GameId at 268
        BitConverter.ToInt32(bytes, 268).Should().Be(42, "GameId at offset 268");
    }
}
