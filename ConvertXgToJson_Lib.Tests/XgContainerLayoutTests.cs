using System.Buffers.Binary;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// <see cref="XgContainerLayout"/> codec tests: the reader parses exactly
/// what the writer writes (the SSOT round-trip that keeps
/// <see cref="Writing.XgContainerWriter"/> and
/// <see cref="Parsing.XgDecompressor"/> agreeing on the byte layout); each
/// field lies at the byte the evidence places it (halheinrich/backgammon#343),
/// asserted on raw bytes so a round trip cannot share a misplacement; an
/// entry is decoded as its bytes state it, leaving acceptance to the reader;
/// and <see cref="XgContainerLayout.TryReadEndRecord"/> rejects a block whose
/// shape deviates.
/// </summary>
public class XgContainerLayoutTests
{
    private static XgContainerLayout.ManifestEntry SampleEntry(
        XgContainerLayout.EntryStorage storage = XgContainerLayout.EntryStorage.Compressed, byte level = 2) =>
        new(Name: "temp.xgr",
            UncompressedSize: 2184 * 3,
            CompressedSize: 917,
            Offset: 12345,
            Crc32: 0xDEADBEEF,
            Storage: storage,
            CompressionLevel: level);

    // -----------------------------------------------------------------------
    //  Round-trip: reader parses what the writer writes
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData((byte)0, (byte)2)]   // compressed, XG's level
    [InlineData((byte)1, (byte)0)]   // stored
    [InlineData((byte)0, (byte)9)]   // compressed, another level
    public void ManifestEntry_RoundTripsThroughWriteAndRead(byte storage, byte level)
    {
        var original = SampleEntry((XgContainerLayout.EntryStorage)storage, level);

        byte[] buffer = new byte[XgContainerLayout.ManifestEntrySize];
        XgContainerLayout.WriteManifestEntry(buffer, original);

        XgContainerLayout.ReadManifestEntry(buffer).Should().Be(original);
    }

    [Fact]
    public void EndRecord_RoundTripsThroughWriteAndTryRead()
    {
        var original = new XgContainerLayout.EndRecord(
            BodyCrc32: 0xCAFEF00D,
            InnerFileCount: 4,
            ManifestCompressedSize: 88,
            ManifestOffset: 54321);

        byte[] buffer = new byte[XgContainerLayout.EndRecordSize];
        XgContainerLayout.WriteEndRecord(buffer, original);

        XgContainerLayout.TryReadEndRecord(buffer, out var reRead).Should().BeTrue();
        reRead.Should().Be(original);
    }

    // -----------------------------------------------------------------------
    //  Raw placement — each field where the evidence puts it
    // -----------------------------------------------------------------------

    [Fact]
    public void WriteManifestEntry_PlacesEachFieldAtItsEvidencedByte()
    {
        byte[] b = new byte[XgContainerLayout.ManifestEntrySize];
        XgContainerLayout.WriteManifestEntry(b, SampleEntry(XgContainerLayout.EntryStorage.Stored, level: 7));

        b[0].Should().Be(8, "the name is a Delphi short string: its length byte first");
        b[1..9].Should().Equal("temp.xgr"u8.ToArray());
        b[9..256].Should().OnlyContain(x => x == 0, "the 256-byte name field is zero padded");
        b[256..512].Should().OnlyContain(x => x == 0, "the path field is written empty");
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(512)).Should().Be(2184u * 3, "uncompressed size");
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(516)).Should().Be(917u, "compressed size");
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(520)).Should().Be(12345u, "offset");
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(524)).Should().Be(0xDEADBEEFu, "CRC32");
        b[528].Should().Be(1, "the storage flag: 1 = stored");
        b[529].Should().Be(7, "the compression level");
        b[530..532].Should().Equal([0, 0], "two padding bytes");
    }

    [Fact]
    public void WriteEndRecord_PlacesEachFieldAtItsEvidencedByte()
    {
        byte[] b = new byte[XgContainerLayout.EndRecordSize];
        XgContainerLayout.WriteEndRecord(b, new XgContainerLayout.EndRecord(0xCAFEF00D, 4, 88, 54321));

        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(0)).Should().Be(0xCAFEF00Du, "crc");
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4)).Should().Be(4u, "filecount");
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(8)).Should().Be(1u, "version");
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(12)).Should().Be(88u, "registrysize");
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(16)).Should().Be(54321u, "archivesize");
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(20)).Should().Be(1u, "compressedregistry");
        b[24..].Should().OnlyContain(x => x == 0, "reserved");
    }

    // -----------------------------------------------------------------------
    //  Decoding — every field as its bytes state it
    // -----------------------------------------------------------------------

    [Fact]
    public void ReadManifestEntry_DecodesAnUnknownStorageFlagAsIs_LeavingItToTheReader()
    {
        byte[] buffer = new byte[XgContainerLayout.ManifestEntrySize];
        XgContainerLayout.WriteManifestEntry(buffer, SampleEntry());
        buffer[528] = 7;

        var entry = XgContainerLayout.ReadManifestEntry(buffer);

        ((byte)entry.Storage).Should().Be(7);
        Enum.IsDefined(entry.Storage).Should().BeFalse("7 is neither evidenced value");
    }

    [Fact]
    public void ReadManifestEntry_IgnoresThePathFieldAndThePadding()
    {
        byte[] buffer = new byte[XgContainerLayout.ManifestEntrySize];
        XgContainerLayout.WriteManifestEntry(buffer, SampleEntry());
        buffer[256] = 3;
        "C:/"u8.CopyTo(buffer.AsSpan(257));
        buffer[530] = 0xBE;
        buffer[531] = 0xEF;

        XgContainerLayout.ReadManifestEntry(buffer).Should().Be(SampleEntry());
    }

    [Fact]
    public void ManifestEntry_End_IsComputedWithoutOverflow()
    {
        var entry = SampleEntry() with { Offset = uint.MaxValue, CompressedSize = uint.MaxValue };

        entry.End.Should().Be(2L * uint.MaxValue);
    }

    [Fact]
    public void ReadManifestEntry_ThrowsOnWrongLength()
    {
        FluentActions.Invoking(() => XgContainerLayout.ReadManifestEntry(
            new byte[XgContainerLayout.ManifestEntrySize - 1])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => XgContainerLayout.ReadManifestEntry(
            new byte[XgContainerLayout.ManifestEntrySize + 1])).Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    //  End-record shape — deviating fields mean "not this layout"
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(8, "the version field at [8..11]")]
    [InlineData(20, "the compressed-registry field at [20..23]")]
    [InlineData(24, "the reserved bytes at [24..35]")]
    [InlineData(35, "the reserved bytes at [24..35]")]
    public void TryReadEndRecord_RejectsADeviatingShapeByte(int corruptAt, string because)
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
    //  Inner-file names — the one table the reader and writer share
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(nameof(XgInnerFile.GameRecords), "temp.xg")]
    [InlineData(nameof(XgInnerFile.IndexRecords), "temp.xgi")]
    [InlineData(nameof(XgInnerFile.RolloutContexts), "temp.xgr")]
    [InlineData(nameof(XgInnerFile.Comments), "temp.xgc")]
    public void InnerFileNames_RoundTrip_CaseInsensitively(string member, string name)
    {
        var file = Enum.Parse<XgInnerFile>(member);
        XgContainerLayout.NameOf(file).Should().Be(name);
        XgContainerLayout.TryParseInnerFileName(name, out var parsed).Should().BeTrue();
        parsed.Should().Be(file);
        XgContainerLayout.TryParseInnerFileName(name.ToUpperInvariant(), out parsed).Should().BeTrue();
        parsed.Should().Be(file);
    }

    [Theory]
    [InlineData("temp.xgz")]
    [InlineData("temp.xg ")]
    [InlineData("")]
    public void TryParseInnerFileName_RejectsAnyOtherName(string name) =>
        XgContainerLayout.TryParseInnerFileName(name, out _).Should().BeFalse();

    // -----------------------------------------------------------------------
    //  Write-side argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void WriteManifestEntry_ThrowsOnWrongSizedDestination()
    {
        var act = () => XgContainerLayout.WriteManifestEntry(
            new byte[XgContainerLayout.ManifestEntrySize + 1], SampleEntry());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WriteManifestEntry_ThrowsWhenNameExceedsTheShortString()
    {
        var entry = SampleEntry() with { Name = new string('x', 256) };
        var act = () => XgContainerLayout.WriteManifestEntry(
            new byte[XgContainerLayout.ManifestEntrySize], entry);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WriteManifestEntry_ThrowsOnAnUndefinedStorage()
    {
        var entry = SampleEntry((XgContainerLayout.EntryStorage)2);
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
