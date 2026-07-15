using System.IO.Compression;
using System.IO.Hashing;

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
/// Stream assignment prefers the container's own directory: the trailing
/// 36-byte end-record (sought from EOF, exactly as real XG loads a file)
/// locates the compressed manifest, whose entries name every data stream —
/// see <see cref="XgContainerLayout"/> for both layouts. Assignment by name
/// makes misclassification impossible by construction. When the trailer or
/// manifest is absent or fails validation (the old single-stream format has
/// neither; a corrupt container may fail either), assignment falls back to
/// the historical record-size heuristics.
/// </summary>
internal static class XgDecompressor
{
    // Record sizes are owned by the parsers; referenced here for stream sizing.
    private const int XgiSize = 2 * SaveRecordParser.RecordSize;  // 5120

    public static XgDecompressedStreams Decompress(Stream compressedStream)
    {
        byte[] raw = ReadAllBytes(compressedStream);
        return TryDecompressViaManifest(raw) ?? DecompressByRecordSizeHeuristics(raw);
    }

    /// <summary>
    /// Manifest-directed decompression: reads the end-record from the tail of
    /// <paramref name="raw"/> (whose start is the container's content start,
    /// so all manifest offsets are directly usable as indices), validates it
    /// against the body, then decompresses and assigns each data stream by its
    /// manifest name. Returns <c>null</c> — deferring to the heuristic
    /// fallback — when any structural check fails: unrecognized trailer
    /// shape, manifest offset/size not tiling the body exactly, body CRC32
    /// mismatch, undecompressible or wrongly-sized manifest, an entry that
    /// does not parse, an unknown or duplicate inner-file name, a stream
    /// whose decompressed length contradicts its entry, or a manifest that
    /// never names temp.xg. Per-entry CRC32s are not re-verified: the body
    /// CRC already covers every compressed byte they describe.
    /// </summary>
    private static XgDecompressedStreams? TryDecompressViaManifest(byte[] raw)
    {
        if (raw.Length <= XgContainerLayout.EndRecordSize)
            return null;
        if (!XgContainerLayout.TryReadEndRecord(
                raw.AsSpan(raw.Length - XgContainerLayout.EndRecordSize), out var trailer))
            return null;

        int bodyLength = raw.Length - XgContainerLayout.EndRecordSize;
        if ((long)trailer.ManifestOffset + trailer.ManifestCompressedSize != bodyLength)
            return null;
        if (trailer.DataStreamCount == 0)
            return null;
        if (Crc32.HashToUInt32(raw.AsSpan(0, bodyLength)) != trailer.BodyCrc32)
            return null;

        byte[]? manifest = TryDecompress(raw, (int)trailer.ManifestOffset);
        if (manifest == null ||
            manifest.Length != (long)trailer.DataStreamCount * XgContainerLayout.ManifestEntrySize)
        {
            return null;
        }

        byte[]? xgData = null;
        byte[]? xgiData = null;
        byte[]? xgrData = null;
        byte[]? xgcData = null;

        for (int pos = 0; pos < manifest.Length; pos += XgContainerLayout.ManifestEntrySize)
        {
            if (!XgContainerLayout.TryReadManifestEntry(
                    manifest.AsSpan(pos, XgContainerLayout.ManifestEntrySize), out var entry))
                return null;
            if (entry.Offset >= trailer.ManifestOffset)
                return null;

            byte[]? data = TryDecompress(raw, (int)entry.Offset);
            if (data == null || (uint)data.Length != entry.UncompressedSize)
                return null;

            switch (entry.Name.ToLowerInvariant())
            {
                case "temp.xg":
                    if (xgData != null) return null;
                    xgData = data;
                    break;
                case "temp.xgi":
                    if (xgiData != null) return null;
                    xgiData = data;
                    break;
                case "temp.xgr":
                    if (xgrData != null) return null;
                    xgrData = data;
                    break;
                case "temp.xgc":
                    if (xgcData != null) return null;
                    xgcData = data;
                    break;
                default:
                    return null;
            }
        }

        if (xgData == null)
            return null;

        return new XgDecompressedStreams(
            ToStream(xgData),
            ToStream(xgiData),
            ToStream(xgrData),
            ToStream(xgcData));
    }

    /// <summary>
    /// Fallback assignment for payloads without a validatable manifest, by
    /// record-size heuristics: SaveRec-sized streams are xg then xgi, a
    /// rollout-sized stream is xgr, the first remaining stream is xgc. A
    /// single-stream payload (the old XG format) has its xgi split off the
    /// tail of xg. Note the known limitation that motivates the manifest
    /// path: a manifest stream matches no record size, so in a commentless
    /// multi-stream container it lands in the xgc slot.
    /// </summary>
    private static XgDecompressedStreams DecompressByRecordSizeHeuristics(byte[] raw)
    {
        var streams = DecompressAllStreams(raw);

        byte[]? xgData = null;
        byte[]? xgiData = null;
        byte[]? xgrData = null;
        byte[]? xgcData = null;

        foreach (byte[] s in streams)
        {
            int len = s.Length;
            if (len == 0) continue;

            bool isSaveRecMultiple = len % SaveRecordParser.RecordSize == 0;
            bool isRolloutMultiple = len % RolloutContextParser.RecordSize == 0;

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

    internal static byte[] ReadAllBytes(Stream source)
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
    /// <summary>
    /// Decompresses only the first zlib stream found in <paramref name="raw"/>.
    /// Used by <see cref="XgFileReader.ReadMatchInfo"/> to avoid decompressing
    /// the entire file.
    /// </summary>
    internal static byte[]? DecompressFirstStream(byte[] raw)
    {
        for (int pos = 0; pos < raw.Length - 1; pos++)
        {
            if (!IsZlibHeader(raw[pos], raw[pos + 1]))
                continue;

            byte[]? decompressed = TryDecompress(raw, pos);
            if (decompressed != null)
                return decompressed;
        }
        return null;
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
