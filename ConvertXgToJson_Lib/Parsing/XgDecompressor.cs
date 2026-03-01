using System.IO.Compression;

namespace ConvertXgToJson_Lib.Parsing;

/// <summary>
/// Decompresses the XG payload (single ZLib stream) and splits the result
/// into the four constituent sub-streams by known fixed record sizes.
///
///   temp.xg   - N x 2560 bytes  (TSaveRec records)
///   temp.xgi  - 2 x 2560 bytes  (first + last TSaveRec, always 5120 bytes)
///   temp.xgr  - M x 2184 bytes  (TRolloutContext records, often empty)
///   temp.xgc  - remainder        (RTF comments, variable length)
///
/// Section order in decompressed blob: xg | xgi | xgr | xgc
/// </summary>
internal static class XgDecompressor
{
    private const int SaveRecordSize = 2560;
    private const int XgiSize = 2 * SaveRecordSize;  // 5120
    private const int RolloutRecordSize = 2184;

    public static XgDecompressedStreams Decompress(Stream compressedStream)
    {
        byte[] data = DecompressAll(compressedStream);

        // xg+xgi together occupy the first multiple-of-2560 bytes.
        // xgr follows as M*2184 bytes. xgc is the remainder.
        int xgXgiEnd = FindXgXgiEnd(data);
        int xgiStart = xgXgiEnd - XgiSize;
        int xgrEnd = xgXgiEnd + ((data.Length - xgXgiEnd) / RolloutRecordSize) * RolloutRecordSize;

        return new XgDecompressedStreams(
            Slice(data, 0, xgiStart),
            Slice(data, xgiStart, XgiSize),
            Slice(data, xgXgiEnd, xgrEnd - xgXgiEnd),
            Slice(data, xgrEnd, data.Length - xgrEnd));
    }

    private static byte[] DecompressAll(Stream source)
    {
        var dest = new MemoryStream();
        using var zlib = new ZLibStream(source, CompressionMode.Decompress, leaveOpen: true);
        zlib.CopyTo(dest);
        return dest.ToArray();
    }

    private static int FindXgXgiEnd(byte[] data)
    {
        int candidate = (data.Length / SaveRecordSize) * SaveRecordSize;
        if (candidate < XgiSize)
            throw new InvalidDataException(
                $"Decompressed XG payload ({data.Length} bytes) is too small " +
                $"to contain the minimum xg+xgi block ({XgiSize} bytes).");
        return candidate;
    }

    private static MemoryStream Slice(byte[] data, int offset, int length)
    {
        var ms = new MemoryStream(length);
        if (length > 0) ms.Write(data, offset, length);
        ms.Position = 0;
        return ms;
    }
}

/// <summary>Holds the four decompressed XG sub-streams. Dispose when done.</summary>
internal sealed class XgDecompressedStreams(
    MemoryStream xg,
    MemoryStream xgi,
    MemoryStream xgr,
    MemoryStream xgc) : IDisposable
{
    public MemoryStream GameRecords { get; } = xg;
    public MemoryStream IndexRecords { get; } = xgi;
    public MemoryStream RolloutContexts { get; } = xgr;
    public MemoryStream Comments { get; } = xgc;

    public void Dispose()
    {
        GameRecords.Dispose();
        IndexRecords.Dispose();
        RolloutContexts.Dispose();
        Comments.Dispose();
    }
}
