using System.IO.Compression;
using AwesomeAssertions;
using ConvertXgToJson_Lib.Parsing;
using ConvertXgToJson_Lib.Tests.Helpers;
using ConvertXgToJson_Lib.Writing;
using Xunit;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for XgDecompressor: verifies that each of the four sub-streams
/// is correctly separated from the compressed payload — by manifest name
/// when the container carries its directory (payloads built through
/// <see cref="XgContainerWriter"/>), and by the record-size heuristics
/// when it does not (the bare concatenated payloads
/// <see cref="CompressAll"/> builds).
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
    //  Manifest-directed assignment — containers with a directory are
    //  split by inner-file name, so a commentless container can never
    //  grow a phantom comment stream (the misclassification the
    //  record-size heuristics are prone to).
    // ------------------------------------------------------------------ //

    [Fact]
    public void Decompress_CommentlessManifestContainer_LeavesCommentsEmpty()
    {
        // Under the heuristics this container's trailing manifest (no
        // record-sized shape) would land in the xgc slot — the phantom-
        // comment bug this pin guards against.
        byte[] xg = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];
        using var container = new MemoryStream();
        XgContainerWriter.Write(container, [new("temp.xg", xg), new("temp.xgi", xgi)]);
        container.Position = 0;

        using var streams = XgDecompressor.Decompress(container);

        streams.GameRecords.Length.Should().Be(xg.Length);
        streams.IndexRecords.Length.Should().Be(xgi.Length);
        streams.RolloutContexts.Length.Should().Be(0);
        streams.Comments.Length.Should().Be(0,
            "the manifest names no temp.xgc, so nothing may be assigned to the comment slot");
    }

    [Fact]
    public void Decompress_FourStreamManifestContainer_AssignsEveryStreamByName()
    {
        byte[] xg = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];
        byte[] xgr = new byte[2184];
        byte[] xgc = "Real comment\r\n"u8.ToArray();
        using var container = new MemoryStream();
        XgContainerWriter.Write(container,
            [new("temp.xg", xg), new("temp.xgr", xgr), new("temp.xgi", xgi), new("temp.xgc", xgc)]);
        container.Position = 0;

        using var streams = XgDecompressor.Decompress(container);

        streams.GameRecords.Length.Should().Be(xg.Length);
        streams.IndexRecords.Length.Should().Be(xgi.Length);
        streams.RolloutContexts.Length.Should().Be(xgr.Length);
        byte[] comments = new byte[streams.Comments.Length];
        streams.Comments.ReadExactly(comments);
        comments.Should().Equal(xgc);
    }

    [Fact]
    public void Decompress_CorruptTrailer_FallsBackToRecordSizeHeuristics()
    {
        // An unvalidatable trailer must degrade to the heuristic path, not
        // fail the parse. The fallback's known limitation then applies: the
        // manifest stream lands in the comment slot of this commentless
        // container. Accepted robustness trade-off — pinned so a change is
        // a conscious decision, not an accident.
        byte[] xg = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];
        using var container = new MemoryStream();
        XgContainerWriter.Write(container, [new("temp.xg", xg), new("temp.xgi", xgi)]);
        byte[] bytes = container.ToArray();
        bytes[^1] ^= 0xFF; // trailer's zero tail no longer zero → shape rejected

        using var streams = XgDecompressor.Decompress(new MemoryStream(bytes));

        streams.GameRecords.Length.Should().Be(xg.Length);
        streams.IndexRecords.Length.Should().Be(xgi.Length);
        streams.Comments.Length.Should().BeGreaterThan(0,
            "the heuristic fallback cannot tell the manifest from a comment stream");
    }

    // ------------------------------------------------------------------ //
    //  Helpers
    // ------------------------------------------------------------------ //

    private static byte[] BuildTwoRecordXg()
    {
        byte[] rec0 = XgBytesBuilder.BuildMatchHeaderRecord("Alice", "Bob", 7,
            new DateTime(2024, 1, 15, 14, 30, 0, DateTimeKind.Utc));
        byte[] rec1 = XgBytesBuilder.BuildMatchFooterRecord(7);
        return [.. rec0, .. rec1];
    }

    /// <summary>
    /// Compresses each section as its own ZLib stream and concatenates them,
    /// with no manifest or end-record — the bare payload shape that exercises
    /// the decompressor's record-size-heuristic fallback.
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