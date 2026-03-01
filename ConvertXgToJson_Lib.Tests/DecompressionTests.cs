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
    /// <summary>
    /// Compresses a known byte array as a single ZLib stream and verifies
    /// that DecompressOneStream (via Decompress) round-trips it correctly.
    /// </summary>
    [Fact]
    public void Decompress_RoundTripsGameRecordStream()
    {
        // Build 2 save records (header + footer) = 5120 bytes
        byte[] xg  = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];
        byte[] xgr = [];
        byte[] xgc = "Test comment\r\n"u8.ToArray();

        byte[] compressed = CompressAll(xg, xgi, xgr, xgc);

        using var streams = XgDecompressor.Decompress(new MemoryStream(compressed));

        streams.GameRecords.Length.Should().Be(xg.Length);
        streams.IndexRecords.Length.Should().Be(xgi.Length);
        // xgr is empty so its ZLib stream decompresses to 0 bytes
        streams.RolloutContexts.Length.Should().Be(0);
        streams.Comments.Length.Should().Be(xgc.Length);
    }

    [Fact]
    public void Decompress_GameRecordsBytesMatchOriginal()
    {
        byte[] xg  = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];
        byte[] compressed = CompressAll(xg, xgi, [], []);

        using var streams = XgDecompressor.Decompress(new MemoryStream(compressed));

        byte[] result = new byte[streams.GameRecords.Length];
        streams.GameRecords.ReadExactly(result);
        result.Should().Equal(xg);
    }

    [Fact]
    public void Decompress_CommentStreamMatchesOriginal()
    {
        byte[] xg  = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];
        byte[] xgc = "Comment with embedded\x01\x02newline\r\n"u8.ToArray();
        byte[] compressed = CompressAll(xg, xgi, [], xgc);

        using var streams = XgDecompressor.Decompress(new MemoryStream(compressed));

        byte[] result = new byte[streams.Comments.Length];
        streams.Comments.ReadExactly(result);
        result.Should().Equal(xgc);
    }

    [Fact]
    public void Decompress_AllStreamsPositionedAtZero()
    {
        byte[] xg  = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];
        byte[] compressed = CompressAll(xg, xgi, [], []);

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

    private static byte[] CompressAll(params byte[][] sections)
    {
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            foreach (var section in sections)
                z.Write(section);
        }
        ms.Position = 0;
        return ms.ToArray();
    }
}
