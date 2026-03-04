using System.IO.Compression;
using FluentAssertions;
using ConvertXgToJson_Lib.Parsing;
using ConvertXgToJson_Lib.Tests.Helpers;
using Xunit;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for XgDecompressor: verifies that each of the four sub-streams
/// is correctly separated from the compressed payload.
/// </summary>
public class DecompressionTests
{
    [Fact]
    public void Decompress_RoundTripsGameRecordStream()
    {
        byte[] xg = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];
        byte[] xgr = [];
        byte[] xgc = "Test comment\r\n"u8.ToArray();

        byte[] compressed = CompressAll(xg, xgr, xgi, xgc);

        using var streams = XgDecompressor.Decompress(new MemoryStream(compressed));

        streams.GameRecords.Length.Should().Be(xg.Length);
        streams.IndexRecords.Length.Should().Be(xgi.Length);
        streams.RolloutContexts.Length.Should().Be(0);
        streams.Comments.Length.Should().Be(xgc.Length);
    }

    [Fact]
    public void Decompress_GameRecordsBytesMatchOriginal()
    {
        byte[] xg = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];

        byte[] compressed = CompressAll(xg, [], xgi, []);

        using var streams = XgDecompressor.Decompress(new MemoryStream(compressed));

        byte[] result = new byte[streams.GameRecords.Length];
        streams.GameRecords.ReadExactly(result);
        result.Should().Equal(xg);
    }

    [Fact]
    public void Decompress_CommentStreamMatchesOriginal()
    {
        byte[] xg = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];
        byte[] xgc = "Comment with embedded\x01\x02newline\r\n"u8.ToArray();

        byte[] compressed = CompressAll(xg, [], xgi, xgc);

        using var streams = XgDecompressor.Decompress(new MemoryStream(compressed));

        byte[] result = new byte[streams.Comments.Length];
        streams.Comments.ReadExactly(result);
        result.Should().Equal(xgc);
    }

    [Fact]
    public void Decompress_AllStreamsPositionedAtZero()
    {
        byte[] xg = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];

        byte[] compressed = CompressAll(xg, [], xgi, []);

        using var streams = XgDecompressor.Decompress(new MemoryStream(compressed));

        streams.GameRecords.Position.Should().Be(0);
        streams.IndexRecords.Position.Should().Be(0);
        streams.RolloutContexts.Position.Should().Be(0);
        streams.Comments.Position.Should().Be(0);
    }

    // ------------------------------------------------------------------ //
    //  Helpers
    // ------------------------------------------------------------------ //

    private static byte[] BuildTwoRecordXg()
    {
        byte[] rec0 = XgFileBuilder.BuildMatchHeaderRecord("Alice", "Bob", 7,
            new DateTime(2024, 1, 15, 14, 30, 0, DateTimeKind.Utc));
        byte[] rec1 = XgFileBuilder.BuildMatchFooterRecord(7);
        return [.. rec0, .. rec1];
    }

    /// <summary>
    /// Compresses each section as its own ZLib stream and concatenates them —
    /// matching the ZlibArchive multi-stream format used by XG.
    /// Stream order must be: xg, xgr, xgi, xgc.
    /// Empty sections produce a valid empty zlib stream so the stream count
    /// stays predictable; the decompressor skips zero-length results.
    /// </summary>
    private static byte[] CompressAll(byte[] xg, byte[] xgr, byte[] xgi, byte[] xgc)
    {
        // Each non-empty section gets its own zlib stream, in order: xg, xgr, xgi, xgc.
        // Empty sections are omitted — the size-based classifier does not need placeholders.
        var ms = new MemoryStream();
        foreach (var section in new[] { xg, xgr, xgi, xgc })
        {
            if (section.Length == 0) continue;
            using var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true);
            z.Write(section);
        }
        return ms.ToArray();
    }
}