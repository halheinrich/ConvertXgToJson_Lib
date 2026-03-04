using System.IO.Compression;

namespace ConvertXgToJson_Lib.Parsing;

/// <summary>
/// Decompresses the XG payload, which is stored as multiple concatenated ZLib
/// streams (one per sub-file), and splits them into the four constituent
/// sub-streams.
///
///   temp.xg   - N x 2560 bytes  (TSaveRec records)
///   temp.xgi  - 2 x 2560 bytes  (first + last TSaveRec, always 5120 bytes)
///   temp.xgr  - M x 2184 bytes  (TRolloutContext records, may be absent)
///   temp.xgc  - variable         (RTF comments, may be absent)
///
/// Real XG files emit streams in order: xg, xgr, xgi, xgc.
/// Among the SaveRec-sized streams, xg always comes FIRST.
/// </summary>
internal static class XgDecompressor
{
    private const int SaveRecordSize = 2560;
    private const int XgiSize = 2 * SaveRecordSize;  // 5120
    private const int RolloutRecordSize = 2184;

    public static XgDecompressedStreams Decompress(Stream compressedStream)
    {
        byte[] raw = ReadAllBytes(compressedStream);
        var streams = DecompressAllStreams(raw);

        byte[]? xgData = null;
        byte[]? xgiData = null;
        byte[]? xgrData = null;
        byte[]? xgcData = null;

        foreach (byte[] s in streams)
        {
            int len = s.Length;
            if (len == 0) continue;

            bool isSaveRecMultiple = len % SaveRecordSize == 0;
            bool isRolloutMultiple = len % RolloutRecordSize == 0;

            if (isSaveRecMultiple)
            {
                // First SaveRec-sized stream = xg, second = xgi
                if (xgData == null)
                    xgData = s;
                else if (xgiData == null)
                    xgiData = s;
            }
            else if (isRolloutMultiple && xgrData == null)
            {
                xgrData = s;
            }
            else if (xgcData == null)
            {
                xgcData = s;
            }
        }

        // Fallback: single-stream old format — split xgi off the end of xg
        if (xgiData == null && xgData != null && xgData.Length > XgiSize)
        {
            int xgEnd = xgData.Length - XgiSize;
            xgiData = xgData[xgEnd..];
            xgData = xgData[..xgEnd];
        }

        return new XgDecompressedStreams(
            ToStream(xgData),
            ToStream(xgiData),
            ToStream(xgrData),
            ToStream(xgcData));
    }

    // -----------------------------------------------------------------------

    private static byte[] ReadAllBytes(Stream source)
    {
        using var ms = new MemoryStream();
        source.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Finds every concatenated zlib stream in <paramref name="raw"/> and
    /// returns the decompressed bytes of each one in order.
    ///
    /// Boundary detection: after successfully decompressing a stream starting
    /// at <c>pos</c>, we scan forward from <c>pos + 2</c> for the next valid
    /// zlib header rather than relying on <c>MemoryStream.Position</c> (which
    /// .NET's <see cref="ZLibStream"/> may not update accurately when
    /// <c>leaveOpen</c> is true due to internal read-ahead buffering).
    /// </summary>
    internal static List<byte[]> DecompressAllStreams(byte[] raw)
    {
        var results = new List<byte[]>();
        int pos = 0;

        while (pos < raw.Length - 1)
        {
            if (!IsZlibHeader(raw[pos], raw[pos + 1]))
            {
                pos++;
                continue;
            }

            byte[]? decompressed = TryDecompress(raw, pos);
            if (decompressed == null)
            {
                pos++;
                continue;
            }

            results.Add(decompressed);

            // Find the next zlib header after pos+2 to advance correctly.
            // This is reliable regardless of how many bytes ZLibStream buffered.
            int next = FindNextZlibHeader(raw, pos + 2);
            pos = next >= 0 ? next : raw.Length;
        }

        return results;
    }

    /// <summary>
    /// Attempts to decompress a zlib stream starting at <paramref name="offset"/>.
    /// Returns null if decompression fails.
    /// </summary>
    private static byte[]? TryDecompress(byte[] raw, int offset)
    {
        try
        {
            using var input = new MemoryStream(raw, offset, raw.Length - offset);
            using var output = new MemoryStream();
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Scans <paramref name="raw"/> starting at <paramref name="fromPos"/> and
    /// returns the index of the next valid zlib header, or -1 if none found.
    /// </summary>
    private static int FindNextZlibHeader(byte[] raw, int fromPos)
    {
        for (int i = fromPos; i < raw.Length - 1; i++)
        {
            if (IsZlibHeader(raw[i], raw[i + 1]))
                return i;
        }
        return -1;
    }

    private static bool IsZlibHeader(byte b0, byte b1) =>
        b0 == 0x78 && b1 is 0x01 or 0x5E or 0x9C or 0xDA;

    private static MemoryStream ToStream(byte[]? data)
    {
        if (data == null || data.Length == 0) return new MemoryStream(0);
        var ms = new MemoryStream(data.Length);
        ms.Write(data, 0, data.Length);
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