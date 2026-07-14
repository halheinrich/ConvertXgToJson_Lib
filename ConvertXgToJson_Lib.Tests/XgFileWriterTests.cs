using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// <see cref="XgFileWriter"/> round-trip and container-shape tests.
///
/// <para>
/// Round-trip oracle: parse a real file, re-write it, re-parse, and compare
/// the two models through the canonical JSON serialization. Byte identity is
/// deliberately not the goal (pointer preambles, unparsed heap noise, and
/// compression level all differ); model equality after a full parse is the
/// semantic contract.
/// </para>
///
/// <para>
/// Container invariants (stream order, manifest sizes / offsets / CRCs,
/// xgi = first + last record) are asserted directly against the written
/// bytes, because <see cref="XgFileReader"/> ignores the manifest entirely —
/// a reader-level round-trip would pass even with a corrupt manifest, and
/// real XG is presumed to consume it.
/// </para>
/// </summary>
[Collection("FileIO")]
public class XgFileWriterTests
{
    // -----------------------------------------------------------------------
    //  Round-trip oracle
    // -----------------------------------------------------------------------

    /// <summary>
    /// The writer normalizes the outer header: no thumbnail (the model does
    /// not carry its bytes), canonical size, version defaulted to 1.
    /// Expected-side headers get the same normalization before comparing.
    /// </summary>
    private static XgFile WithNormalizedHeader(XgFile file) => new()
    {
        Header = new RichGameHeader
        {
            MagicNumber = 0x484D4752,
            HeaderVersion = file.Header.HeaderVersion != 0 ? file.Header.HeaderVersion : 1,
            HeaderSize = 8232,
            ThumbnailOffset = 0,
            ThumbnailSize = 0,
            GameId = file.Header.GameId,
            GameName = file.Header.GameName,
            SaveName = file.Header.SaveName,
            LevelName = file.Header.LevelName,
            Comments = file.Header.Comments,
        },
        Records = file.Records,
        Rollouts = file.Rollouts,
        Comments = file.Comments,
    };

    private static void AssertRoundTrips(string path)
    {
        var original = XgFileReader.ReadFile(path);
        byte[] rewritten = XgFileWriter.ToBytes(original);
        using var ms = new MemoryStream(rewritten);
        var reparsed = XgFileReader.ReadStream(ms);

        // MagicNumber in the parsed model is guaranteed by the parser (it
        // throws otherwise); reparsed.Header.HeaderSize likewise. The JSON
        // comparison covers every parsed field of every record, rollout,
        // and comment in one canonical shape.
        XgFileReader.ToJson(reparsed).Should().Be(
            XgFileReader.ToJson(WithNormalizedHeader(original)),
            because: $"'{Path.GetFileName(path)}' should survive a write→read round-trip semantically");
    }

    [Fact]
    public void XgpFixtures_RoundTripSemantically()
    {
        // FixtureFiles is append-only, so enumerating it is stable; these
        // include every curated .xgp pane-state shape (unanalysed, cube-only,
        // play, both, rollout-bearing).
        var fixtures = Directory.EnumerateFiles(TestPaths.FixtureFilesDir, "*.xgp").ToList();
        fixtures.Should().NotBeEmpty("the curated .xgp fixtures are pinned in FixtureFiles");
        foreach (string path in fixtures)
            AssertRoundTrips(path);
    }

    [Fact]
    public void XgFixtures_RoundTripSemantically()
    {
        // Full .xg match files exercise the record types .xgp never carries
        // (game/match footers, many games, rollout tables, comment tables).
        var fixtures = Directory.EnumerateFiles(TestPaths.FixtureFilesDir, "*.xg").ToList();
        fixtures.Should().NotBeEmpty("pinned .xg fixtures exist in FixtureFiles");
        foreach (string path in fixtures)
            AssertRoundTrips(path);
    }

    [Fact]
    public void XgCorpus_RoundTripsSemantically()
    {
        // Fixture-agnostic corpus sweep: whatever .xg files exist, tolerate none.
        foreach (string path in TestPaths.XgFiles)
            AssertRoundTrips(path);
    }

    [Fact]
    public void XgpCorpus_RoundTripsSemantically()
    {
        foreach (string path in TestPaths.XgpFiles)
            AssertRoundTrips(path);
    }

    // -----------------------------------------------------------------------
    //  Container invariants — asserted on raw written bytes
    // -----------------------------------------------------------------------

    private const int ManifestEntrySize = 532;
    private const int EndRecordSize = 36;

    private sealed record ManifestEntry(string Name, uint UncompressedSize, uint CompressedSize, uint Offset, uint Crc, uint Constant);

    private static List<ManifestEntry> ParseManifest(byte[] manifest)
    {
        (manifest.Length % ManifestEntrySize).Should().Be(0, "the manifest is a whole number of 532-byte entries");
        var entries = new List<ManifestEntry>();
        for (int off = 0; off < manifest.Length; off += ManifestEntrySize)
        {
            int nameLen = manifest[off];
            entries.Add(new ManifestEntry(
                System.Text.Encoding.Latin1.GetString(manifest, off + 1, nameLen),
                BitConverter.ToUInt32(manifest, off + 512),
                BitConverter.ToUInt32(manifest, off + 516),
                BitConverter.ToUInt32(manifest, off + 520),
                BitConverter.ToUInt32(manifest, off + 524),
                BitConverter.ToUInt32(manifest, off + 528)));
        }
        return entries;
    }

    [Fact]
    public void WrittenContainer_HasRealXgShape()
    {
        // A fixture with rollouts and comments exercises all four inner files.
        string path = Path.Combine(TestPaths.FixtureFilesDir, "Opening 32 65 64 31 65.xgp");
        var file = XgFileReader.ReadFile(path);
        file.Rollouts.Should().NotBeEmpty("this fixture is pinned as rollout-bearing");
        file.Comments.Should().NotBeEmpty("this fixture is pinned as comment-bearing");

        byte[] bytes = XgFileWriter.ToBytes(file);

        // Content starts directly after the 8232-byte header (no thumbnail);
        // the compressed body is everything up to the 36-byte end-record.
        byte[] content = bytes[8232..];
        byte[] body = content[..^EndRecordSize];
        var streams = XgDecompressor.DecompressAllStreams(body);
        streams.Should().HaveCount(5, "xg, xgr, xgi, xgc, manifest");

        byte[] xg = streams[0], xgr = streams[1], xgi = streams[2], xgc = streams[3], manifest = streams[4];

        (xg.Length % SaveRecordParser.RecordSize).Should().Be(0);
        (xgr.Length % RolloutContextParser.RecordSize).Should().Be(0);
        xgi.Length.Should().Be(2 * SaveRecordParser.RecordSize, "temp.xgi always holds exactly two records");

        // xgi = byte-copies of the first and last records of the xg stream.
        xgi[..SaveRecordParser.RecordSize].Should().Equal(xg[..SaveRecordParser.RecordSize]);
        xgi[SaveRecordParser.RecordSize..].Should().Equal(xg[^SaveRecordParser.RecordSize..]);

        // Manifest: entry order = stream order, one entry per inner file
        // (the manifest itself unlisted), sizes/offsets/CRCs consistent.
        var entries = ParseManifest(manifest);
        entries.Select(e => e.Name).Should().Equal("temp.xg", "temp.xgr", "temp.xgi", "temp.xgc");

        byte[][] inner = [xg, xgr, xgi, xgc];
        uint expectedOffset = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            entries[i].UncompressedSize.Should().Be((uint)inner[i].Length, $"{entries[i].Name} uncompressed size");
            entries[i].Offset.Should().Be(expectedOffset, $"{entries[i].Name} offset accumulates compressed sizes");
            entries[i].Crc.Should().Be(System.IO.Hashing.Crc32.HashToUInt32(inner[i]), $"{entries[i].Name} CRC32 of uncompressed bytes");
            entries[i].Constant.Should().Be(0x200);
            expectedOffset += entries[i].CompressedSize;
        }

        // The compressed sizes must also account for the physical layout:
        // last manifest entry's offset + size = start of the manifest stream.
        long manifestStart = body.Length - CompressedLengthOfLastStream(body);
        expectedOffset.Should().Be((uint)manifestStart, "compressed sizes tile the payload exactly up to the manifest");

        // The 36-byte end-record's fields agree with the recomputed body.
        AssertTrailerMatchesRecomputed(content);
    }

    /// <summary>
    /// Length of the final zlib stream in <paramref name="content"/>, found
    /// by locating the last zlib header that decompresses successfully.
    /// </summary>
    private static long CompressedLengthOfLastStream(byte[] content)
    {
        for (int pos = content.Length - 2; pos >= 0; pos--)
        {
            if (content[pos] != 0x78) continue;
            if (content[pos + 1] is not (0x01 or 0x5E or 0x9C or 0xDA)) continue;
            try
            {
                using var input = new MemoryStream(content, pos, content.Length - pos);
                using var zlib = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
                zlib.CopyTo(Stream.Null);
                return content.Length - pos;
            }
            catch
            {
                // false positive header inside compressed data — keep scanning
            }
        }
        throw new InvalidDataException("No trailing zlib stream found.");
    }

    [Fact]
    public void WrittenContainer_OmitsAbsentInnerFiles()
    {
        // No rollouts, no comments → exactly xg, xgi, manifest.
        string path = Path.Combine(TestPaths.FixtureFilesDir, "NoAnalysis.xgp");
        var file = XgFileReader.ReadFile(path);
        var noExtras = new XgFile { Header = file.Header, Records = file.Records };

        byte[] bytes = XgFileWriter.ToBytes(noExtras);
        var streams = XgDecompressor.DecompressAllStreams(bytes[8232..^EndRecordSize]);
        streams.Should().HaveCount(3, "xg, xgi, manifest");

        ParseManifest(streams[2]).Select(e => e.Name).Should().Equal("temp.xg", "temp.xgi");
    }

    // -----------------------------------------------------------------------
    //  End-record (trailer) — the 36-byte block XG seeks to from EOF
    // -----------------------------------------------------------------------

    /// <summary>
    /// Recomputes the nine end-record fields from the compressed body and
    /// asserts the written trailer matches, byte for byte. <paramref name="content"/>
    /// is the payload from content-start (after header + any thumbnail) to EOF,
    /// trailer included.
    /// </summary>
    private static void AssertTrailerMatchesRecomputed(byte[] content)
    {
        content.Length.Should().BeGreaterThan(EndRecordSize, "a container carries at least one stream plus the trailer");
        byte[] body = content[..^EndRecordSize];
        byte[] trailer = content[^EndRecordSize..];

        long manifestLen = CompressedLengthOfLastStream(body);
        long manifestOffset = body.Length - manifestLen;
        int dataStreamCount = XgDecompressor.DecompressAllStreams(body).Count - 1;

        BitConverter.ToUInt32(trailer, 0).Should().Be(
            System.IO.Hashing.Crc32.HashToUInt32(body), "field 1: CRC32 of the whole compressed body");
        BitConverter.ToUInt32(trailer, 4).Should().Be((uint)dataStreamCount, "field 2: data stream count (manifest excluded)");
        BitConverter.ToUInt32(trailer, 8).Should().Be(1u, "field 3: constant 1");
        BitConverter.ToUInt32(trailer, 12).Should().Be((uint)manifestLen, "field 4: compressed size of the manifest stream");
        BitConverter.ToUInt32(trailer, 16).Should().Be((uint)manifestOffset, "field 5: manifest offset from content start");
        BitConverter.ToUInt32(trailer, 20).Should().Be(1u, "field 6: constant 1");
        BitConverter.ToUInt32(trailer, 24).Should().Be(0u, "field 7: zero");
        BitConverter.ToUInt32(trailer, 28).Should().Be(0u, "field 8: zero");
        BitConverter.ToUInt32(trailer, 32).Should().Be(0u, "field 9: zero");
    }

    [Fact]
    public void WrittenTrailer_MatchesRecomputedFields_WithRolloutsAndComments()
    {
        // Four data streams (xg, xgr, xgi, xgc) → the trailer's stream count
        // and manifest offset must reflect all four.
        string path = Path.Combine(TestPaths.FixtureFilesDir, "Opening 32 65 64 31 65.xgp");
        var file = XgFileReader.ReadFile(path);
        file.Rollouts.Should().NotBeEmpty("this fixture is pinned as rollout-bearing");

        byte[] bytes = XgFileWriter.ToBytes(file);
        AssertTrailerMatchesRecomputed(bytes[8232..]);
    }

    [Fact]
    public void WrittenTrailer_MatchesRecomputedFields_NoRolloutsOrComments()
    {
        // Two data streams (xg, xgi) → a different stream count and offset,
        // so the recomputation is genuinely re-exercised, not a constant.
        string path = Path.Combine(TestPaths.FixtureFilesDir, "NoAnalysis.xgp");
        var src = XgFileReader.ReadFile(path);
        var file = new XgFile { Header = src.Header, Records = src.Records };
        file.Rollouts.Should().BeEmpty();
        file.Comments.Should().BeEmpty();

        byte[] bytes = XgFileWriter.ToBytes(file);
        AssertTrailerMatchesRecomputed(bytes[8232..]);
    }

    [Fact]
    public void XgCorpus_EndRecord_MatchesRecomputedFields()
    {
        // Fixture-agnostic: pin our decoding of the end-record against every
        // XG-authored .xg file present, tolerating an empty corpus. Real XG
        // writes the trailer; recomputing its fields from the compressed body
        // and matching proves we read the format the way XG wrote it.
        foreach (string path in TestPaths.XgFiles)
        {
            byte[] fileBytes = File.ReadAllBytes(path);
            long contentOffset = ContentOffset(path);
            AssertTrailerMatchesRecomputed(fileBytes[(int)contentOffset..]);
        }
    }

    /// <summary>Content start (after header + any thumbnail) of an XG file.</summary>
    private static long ContentOffset(string path)
    {
        using var fs = File.OpenRead(path);
        return RichGameHeaderParser.Read(fs).ContentOffset;
    }

    // -----------------------------------------------------------------------
    //  Validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Write_Throws_OnEmptyRecords()
    {
        var act = () => XgFileWriter.ToBytes(new XgFile());
        act.Should().Throw<ArgumentException>().WithMessage("*no records*");
    }

    [Fact]
    public void Write_Throws_WhenFirstRecordIsNotMatchHeader()
    {
        var file = new XgFile { Records = [new GameHeaderRecord { EntryType = RecordType.HeaderGame }] };
        var act = () => XgFileWriter.ToBytes(file);
        act.Should().Throw<ArgumentException>().WithMessage("*MatchHeaderRecord*");
    }
}
