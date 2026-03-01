using FluentAssertions;
using ConvertXgToJson_Lib.Parsing;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for PascalBinaryReader: verifies that every Pascal alignment rule
/// is applied correctly and that all primitive read methods return the right
/// values from a known byte sequence.
/// </summary>
public class PascalBinaryReaderTests
{
    // ------------------------------------------------------------------ //
    //  Integers
    // ------------------------------------------------------------------ //

    [Fact]
    public void ReadByte_ReturnsCorrectValue()
    {
        var stream = new MemoryStream([0xAB]);
        using var r = new PascalBinaryReader(stream);
        r.ReadByte().Should().Be(0xAB);
    }

    [Fact]
    public void ReadShortInt_ReturnsNegativeValue()
    {
        var stream = new MemoryStream([unchecked((byte)-42)]);
        using var r = new PascalBinaryReader(stream);
        r.ReadShortInt().Should().Be(-42);
    }

    [Fact]
    public void ReadSmallInt_AlignsTo2Bytes()
    {
        // 1 byte unaligned, then SmallInt should skip 1 padding byte
        var stream = new MemoryStream([0x01, 0xFF, 0x2C, 0x01]);
        //                              ^ byte  ^ pad  ^--- SmallInt 300 (0x012C) LE
        using var r = new PascalBinaryReader(stream);
        _ = r.ReadByte();              // pos = 1
        r.ReadSmallInt().Should().Be(300); // aligns to 2, reads at pos 2
    }

    [Fact]
    public void ReadInteger_AlignsTo4Bytes()
    {
        // Write 3 filler bytes then a 4-byte int
        var bytes = new byte[8];
        bytes[0] = 0xFF; bytes[1] = 0xFF; bytes[2] = 0xFF; // filler
        // pos 3 → align to 4 → skip 1 → pos 4
        BitConverter.GetBytes(12345678).CopyTo(bytes, 4);

        using var r = new PascalBinaryReader(new MemoryStream(bytes));
        r.ReadByte(); r.ReadByte(); r.ReadByte(); // pos = 3
        r.ReadInteger().Should().Be(12345678);
    }

    [Fact]
    public void ReadDouble_AlignsTo8Bytes()
    {
        var b = new BinaryBuilder()
            .Byte(0x01)           // pos 1
            .Double(Math.PI);     // align to 8 → 7 padding bytes → double at pos 8

        using var r = new PascalBinaryReader(b.ToStream());
        _ = r.ReadByte();
        r.ReadDouble().Should().BeApproximately(Math.PI, 1e-15);
    }

    [Fact]
    public void ReadSingle_AlignsTo4Bytes()
    {
        var b = new BinaryBuilder()
            .Byte(0x01).Byte(0x02).Byte(0x03) // 3 bytes
            .Float(1.5f);                      // align to 4 → pad 1 → float at pos 4

        using var r = new PascalBinaryReader(b.ToStream());
        r.ReadByte(); r.ReadByte(); r.ReadByte();
        r.ReadSingle().Should().BeApproximately(1.5f, 1e-7f);
    }

    // ------------------------------------------------------------------ //
    //  No-align types
    // ------------------------------------------------------------------ //

    [Fact]
    public void ReadBoolean_DoesNotAlign()
    {
        // Boolean immediately follows a byte with no padding
        var stream = new MemoryStream([0x05, 0x01]);
        using var r = new PascalBinaryReader(stream);
        _ = r.ReadByte();
        r.ReadBoolean().Should().BeTrue();
    }

    [Fact]
    public void ReadBoolean_TreatsZeroAsFalseAndNonZeroAsTrue()
    {
        var stream = new MemoryStream([0x00, 0x01, 0xFF]);
        using var r = new PascalBinaryReader(stream);
        r.ReadBoolean().Should().BeFalse();
        r.ReadBoolean().Should().BeTrue();
        r.ReadBoolean().Should().BeTrue(); // 0xFF is non-zero → true
    }

    // ------------------------------------------------------------------ //
    //  Pascal string[N]
    // ------------------------------------------------------------------ //

    [Fact]
    public void ReadPascalAnsiString_ReadsLengthPrefixedString()
    {
        var b = new BinaryBuilder().PascalAnsiString("Hello", 40);
        using var r = new PascalBinaryReader(b.ToStream());
        r.ReadPascalAnsiString(40).Should().Be("Hello");
    }

    [Fact]
    public void ReadPascalAnsiString_HandlesEmptyString()
    {
        var b = new BinaryBuilder().PascalAnsiString("", 40);
        using var r = new PascalBinaryReader(b.ToStream());
        r.ReadPascalAnsiString(40).Should().BeEmpty();
    }

    [Fact]
    public void ReadPascalAnsiString_TruncatesAtMaxLen()
    {
        // Write a "length" byte larger than maxLen to simulate corrupt data
        // The reader should clamp it.
        byte[] buf = new byte[6]; buf[0] = 10; // claims 10 chars but maxLen=5
        System.Text.Encoding.Latin1.GetBytes("Hello").CopyTo(buf, 1);
        using var r = new PascalBinaryReader(new MemoryStream(buf));
        r.ReadPascalAnsiString(5).Should().Be("Hello");
    }

    [Fact]
    public void ReadPascalAnsiString_AlwaysConsumesMaxLenPlusOneByte()
    {
        // string[2] = 3 bytes on disk regardless of actual length
        var b = new BinaryBuilder()
            .PascalAnsiString("A", 2)
            .Byte(0x7F); // sentinel
        using var r = new PascalBinaryReader(b.ToStream());
        _ = r.ReadPascalAnsiString(2);             // consumes 3 bytes
        r.ReadByte().Should().Be(0x7F);            // sentinel is still reachable
    }

    // ------------------------------------------------------------------ //
    //  WideChar array
    // ------------------------------------------------------------------ //

    [Fact]
    public void ReadWideCharArray_ReadsUnicodeString()
    {
        var b = new BinaryBuilder().WideCharArray("Héllo", 20);
        using var r = new PascalBinaryReader(b.ToStream());
        r.ReadWideCharArray(20).Should().Be("Héllo");
    }

    [Fact]
    public void ReadWideCharArray_StopsAtNullTerminator()
    {
        var b = new BinaryBuilder().WideCharArray("Hi", 10);
        using var r = new PascalBinaryReader(b.ToStream());
        r.ReadWideCharArray(10).Should().Be("Hi");
    }

    [Fact]
    public void ReadWideCharArray_AlignsTo2Bytes()
    {
        var b = new BinaryBuilder()
            .Byte(0x01)            // pos 1 → next WideCharArray must align to 2
            .WideCharArray("X", 4);

        using var r = new PascalBinaryReader(b.ToStream());
        _ = r.ReadByte();
        r.ReadWideCharArray(4).Should().Be("X");
    }

    // ------------------------------------------------------------------ //
    //  TShortUnicodeString
    // ------------------------------------------------------------------ //

    [Fact]
    public void ReadShortUnicodeString_Reads129WideChars()
    {
        var b = new BinaryBuilder().ShortUnicodeString("Unicode Test 🎲");
        b.ToArray().Should().HaveCount(258); // 129 WideChars × 2 bytes
        using var r = new PascalBinaryReader(b.ToStream());
        r.ReadShortUnicodeString().Should().Be("Unicode Test 🎲");
    }

    // ------------------------------------------------------------------ //
    //  TDateTime
    // ------------------------------------------------------------------ //

    [Fact]
    public void ReadTDateTime_ConvertsFromPascalEpoch()
    {
        // January 1, 1996 = 35065 days after Dec 30 1899
        // The spec example: 35065.541667 = Jan 1, 1996 1:00 PM
        var b = new BinaryBuilder().Double(35065.541667);
        using var r = new PascalBinaryReader(b.ToStream());
        var dt = r.ReadTDateTime();
        dt.Year.Should().Be(1996);
        dt.Month.Should().Be(1);
        dt.Day.Should().Be(1);
        dt.Hour.Should().Be(13); // 1 PM
    }

    [Fact]
    public void ReadTDateTime_ZeroReturnsEpoch()
    {
        var b = new BinaryBuilder().Double(0.0);
        using var r = new PascalBinaryReader(b.ToStream());
        r.ReadTDateTime().Should().Be(new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc));
    }

    // ------------------------------------------------------------------ //
    //  GUID
    // ------------------------------------------------------------------ //

    [Fact]
    public void ReadGuid_RoundTrips()
    {
        var expected = Guid.NewGuid();
        var b = new BinaryBuilder().Guid(expected);
        using var r = new PascalBinaryReader(b.ToStream());
        r.ReadGuid().Should().Be(expected);
    }

    // ------------------------------------------------------------------ //
    //  AlignTo / Skip
    // ------------------------------------------------------------------ //

    [Fact]
    public void AlignTo_IsIdempotentWhenAlreadyAligned()
    {
        // Position 0 is already aligned to everything
        var b = new BinaryBuilder().Int32(99);
        using var r = new PascalBinaryReader(b.ToStream());
        r.ReadInteger().Should().Be(99); // reads from pos 0, no pad needed
    }

    [Fact]
    public void Skip_AdvancesPosition()
    {
        var stream = new MemoryStream([0x01, 0x02, 0x03, 0x04, 0x05]);
        using var r = new PascalBinaryReader(stream);
        r.Skip(3);
        r.ReadByte().Should().Be(0x04);
    }

    // ------------------------------------------------------------------ //
    //  Alignment interaction: Bool then Double
    // ------------------------------------------------------------------ //

    [Fact]
    public void BoolFollowedByDouble_Aligns7BytesOfPadding()
    {
        // Classic Pascal: Boolean at some position, then Double needs 8-byte boundary
        // e.g. bool at pos 0 → pos 1 → align to 8 → 7 bytes padding → double at 8
        var b = new BinaryBuilder()
            .Bool(true)
            .Double(2.718281828);

        var bytes = b.ToArray();
        bytes.Length.Should().Be(16); // 1 bool + 7 pad + 8 double

        using var r = new PascalBinaryReader(b.ToStream());
        r.ReadBoolean().Should().BeTrue();
        r.ReadDouble().Should().BeApproximately(2.718281828, 1e-9);
    }
}
