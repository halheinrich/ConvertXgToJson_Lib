namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// <see cref="XgContainerLayout"/> codec tests: the reader parses exactly
/// what the writer writes (the SSOT round-trip that keeps
/// <see cref="Writing.XgContainerWriter"/> and
/// <see cref="Parsing.XgDecompressor"/> agreeing on the byte layout), and
/// the Try-read methods reject blocks whose constant fields deviate from
/// the documented shape.
/// </summary>
public class XgContainerLayoutTests
{
    // -----------------------------------------------------------------------
    //  Round-trip: reader parses what the writer writes
    // -----------------------------------------------------------------------

    [Fact]
    public void ManifestEntry_RoundTripsThroughWriteAndTryRead()
    {
        var original = new XgContainerLayout.ManifestEntry(
            Name: "temp.xgr",
            UncompressedSize: 2184 * 3,
            CompressedSize: 917,
            Offset: 12345,
            Crc32: 0xDEADBEEF);

        byte[] buffer = new byte[XgContainerLayout.ManifestEntrySize];
        XgContainerLayout.WriteManifestEntry(buffer, original);

        XgContainerLayout.TryReadManifestEntry(buffer, out var reRead).Should().BeTrue();
        reRead.Should().Be(original);
    }

    [Fact]
    public void EndRecord_RoundTripsThroughWriteAndTryRead()
    {
        var original = new XgContainerLayout.EndRecord(
            BodyCrc32: 0xCAFEF00D,
            DataStreamCount: 4,
            ManifestCompressedSize: 88,
            ManifestOffset: 54321);

        byte[] buffer = new byte[XgContainerLayout.EndRecordSize];
        XgContainerLayout.WriteEndRecord(buffer, original);

        XgContainerLayout.TryReadEndRecord(buffer, out var reRead).Should().BeTrue();
        reRead.Should().Be(original);
    }

    // -----------------------------------------------------------------------
    //  Shape validation — deviating constants mean "not this layout"
    // -----------------------------------------------------------------------

    [Fact]
    public void TryReadManifestEntry_RejectsWrongTrailingConstant()
    {
        byte[] buffer = new byte[XgContainerLayout.ManifestEntrySize];
        XgContainerLayout.WriteManifestEntry(buffer,
            new XgContainerLayout.ManifestEntry("temp.xg", 2560, 100, 0, 1));
        buffer[528] ^= 0xFF;

        XgContainerLayout.TryReadManifestEntry(buffer, out _).Should().BeFalse();
    }

    [Fact]
    public void TryReadManifestEntry_RejectsWrongLength()
    {
        XgContainerLayout.TryReadManifestEntry(
            new byte[XgContainerLayout.ManifestEntrySize - 1], out _).Should().BeFalse();
        XgContainerLayout.TryReadManifestEntry(
            new byte[XgContainerLayout.ManifestEntrySize + 1], out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(8, "the constant-1 field at [8..11]")]
    [InlineData(20, "the constant-1 field at [20..23]")]
    [InlineData(24, "the zero tail at [24..35]")]
    [InlineData(35, "the zero tail at [24..35]")]
    public void TryReadEndRecord_RejectsDeviatingConstantBytes(int corruptAt, string because)
    {
        byte[] buffer = new byte[XgContainerLayout.EndRecordSize];
        XgContainerLayout.WriteEndRecord(buffer,
            new XgContainerLayout.EndRecord(1, 2, 3, 4));
        buffer[corruptAt] ^= 0xFF;

        XgContainerLayout.TryReadEndRecord(buffer, out _).Should().BeFalse(because);
    }

    [Fact]
    public void TryReadEndRecord_RejectsWrongLength()
    {
        XgContainerLayout.TryReadEndRecord(
            new byte[XgContainerLayout.EndRecordSize - 1], out _).Should().BeFalse();
        XgContainerLayout.TryReadEndRecord(
            new byte[XgContainerLayout.EndRecordSize + 1], out _).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    //  Write-side argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void WriteManifestEntry_ThrowsOnWrongSizedDestination()
    {
        var entry = new XgContainerLayout.ManifestEntry("temp.xg", 1, 1, 0, 0);
        var act = () => XgContainerLayout.WriteManifestEntry(
            new byte[XgContainerLayout.ManifestEntrySize + 1], entry);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WriteManifestEntry_ThrowsWhenNameExceedsPascalLengthPrefix()
    {
        var entry = new XgContainerLayout.ManifestEntry(new string('x', 256), 1, 1, 0, 0);
        var act = () => XgContainerLayout.WriteManifestEntry(
            new byte[XgContainerLayout.ManifestEntrySize], entry);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WriteEndRecord_ThrowsOnWrongSizedDestination()
    {
        var act = () => XgContainerLayout.WriteEndRecord(
            new byte[XgContainerLayout.EndRecordSize - 1],
            new XgContainerLayout.EndRecord(1, 2, 3, 4));
        act.Should().Throw<ArgumentException>();
    }
}
