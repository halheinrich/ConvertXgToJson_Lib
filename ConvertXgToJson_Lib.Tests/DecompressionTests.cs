using AwesomeAssertions;
using ConvertXgToJson_Lib.Parsing;
using ConvertXgToJson_Lib.Tests.Helpers;
using ConvertXgToJson_Lib.Writing;
using Xunit;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for XgDecompressor over containers the production
/// <see cref="XgContainerWriter"/> writes: each of the four sub-streams is
/// separated by its manifest name and arrives byte for byte. The directory
/// contract itself — the legacy single stream, every check and its
/// refusal, stored entries — is <see cref="XgContainerDirectoryTests"/>'.
/// </summary>
public class DecompressionTests
{
    [Fact]
    public void Decompress_RoundTripsGameRecordStream()
    {
        byte[] xg = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];
        byte[] xgc = "Test comment\r\n"u8.ToArray();

        using var streams = XgDecompressor.Decompress(Container(xg, [], xgi, xgc));

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

        using var streams = XgDecompressor.Decompress(Container(xg, [], xgi, []));

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

        using var streams = XgDecompressor.Decompress(Container(xg, [], xgi, xgc));

        byte[] result = new byte[streams.Comments.Length];
        streams.Comments.ReadExactly(result);
        result.Should().Equal(xgc);
    }

    [Fact]
    public void Decompress_AllStreamsPositionedAtZero()
    {
        byte[] xg = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];

        using var streams = XgDecompressor.Decompress(Container(xg, [], xgi, []));

        streams.GameRecords.Position.Should().Be(0);
        streams.IndexRecords.Position.Should().Be(0);
        streams.RolloutContexts.Position.Should().Be(0);
        streams.Comments.Position.Should().Be(0);
    }

    // ------------------------------------------------------------------ //
    //  Manifest-directed assignment — containers are split by inner-file
    //  name, so a commentless container can never grow a comment stream.
    // ------------------------------------------------------------------ //

    [Fact]
    public void Decompress_CommentlessManifestContainer_LeavesCommentsEmpty()
    {
        // The manifest names no temp.xgc, and the manifest stream itself is
        // never an inner file: nothing may land in the comment slot.
        byte[] xg = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];

        using var streams = XgDecompressor.Decompress(Container(xg, [], xgi, []));

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

        using var streams = XgDecompressor.Decompress(Container(xg, xgr, xgi, xgc));

        streams.GameRecords.Length.Should().Be(xg.Length);
        streams.IndexRecords.Length.Should().Be(xgi.Length);
        streams.RolloutContexts.Length.Should().Be(xgr.Length);
        byte[] comments = new byte[streams.Comments.Length];
        streams.Comments.ReadExactly(comments);
        comments.Should().Equal(xgc);
    }

    [Fact]
    public void Decompress_CorruptTrailer_IsRefused()
    {
        // Hal's ruling on halheinrich/backgammon#343, "Refuse with clear
        // error": a container whose directory cannot be used is refused,
        // never read by guessing which stream is which.
        byte[] xg = BuildTwoRecordXg();
        byte[] xgi = [.. xg[..2560], .. xg[^2560..]];
        byte[] bytes = Container(xg, [], xgi, []).ToArray();
        bytes[^1] ^= 0xFF; // trailer's reserved bytes no longer zero → not an end-record

        FluentActions.Invoking(() => XgDecompressor.Decompress(new MemoryStream(bytes)))
            .Should().Throw<InvalidDataException>()
            .WithMessage("*its last 36 bytes are not an end-record*");
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
    /// The container payload <see cref="XgContainerWriter"/> writes for the
    /// four sections, in real XG's stream order (xg, xgr, xgi, xgc); an
    /// empty section is left out, as the file writer leaves it out.
    /// </summary>
    private static MemoryStream Container(byte[] xg, byte[] xgr, byte[] xgi, byte[] xgc)
    {
        var files = new List<XgContainerWriter.InnerFile> { new("temp.xg", xg) };
        if (xgr.Length > 0) files.Add(new("temp.xgr", xgr));
        files.Add(new("temp.xgi", xgi));
        if (xgc.Length > 0) files.Add(new("temp.xgc", xgc));

        var container = new MemoryStream();
        XgContainerWriter.Write(container, files);
        container.Position = 0;
        return container;
    }
}
