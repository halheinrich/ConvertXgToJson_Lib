using System.IO.Compression;
using System.IO.Hashing;

namespace ConvertXgToJson_Lib.Writing;

/// <summary>
/// Writes the compressed XG payload: the concatenated zlib streams that
/// follow the RichGameHeader, then the manifest stream, then a 36-byte
/// uncompressed end-record. Mirror of <see cref="Parsing.XgDecompressor"/>'s
/// container knowledge; the manifest and end-record byte layout both sides
/// share lives in <see cref="XgContainerLayout"/>.
///
/// Stream order matches real XG files: temp.xg, temp.xgr (when rollouts
/// exist), temp.xgi, temp.xgc (when comments exist), then the manifest,
/// then the end-record.
/// </summary>
internal static class XgContainerWriter
{
    /// <summary>
    /// One inner file of the container: its temp-file name and uncompressed
    /// payload. Order of appearance = physical stream order.
    /// </summary>
    internal readonly record struct InnerFile(string Name, byte[] Data);

    /// <summary>
    /// Compresses each inner file into a zlib stream, appends the compressed
    /// manifest and the trailing end-record, and writes the whole payload to
    /// <paramref name="output"/>.
    /// </summary>
    internal static void Write(Stream output, IReadOnlyList<InnerFile> files)
    {
        var compressed = new byte[files.Count][];
        for (int i = 0; i < files.Count; i++)
            compressed[i] = Compress(files[i].Data);

        byte[] manifest = BuildManifest(files, compressed);
        byte[] compressedManifest = Compress(manifest);

        // Manifest offset = sum of the data streams' compressed sizes; the
        // body CRC covers every compressed stream, manifest included.
        uint manifestOffset = 0;
        var bodyCrc = new Crc32();
        foreach (byte[] stream in compressed)
        {
            manifestOffset += (uint)stream.Length;
            bodyCrc.Append(stream);
        }
        bodyCrc.Append(compressedManifest);

        foreach (byte[] stream in compressed)
            output.Write(stream);
        output.Write(compressedManifest);

        Span<byte> endRecord = stackalloc byte[XgContainerLayout.EndRecordSize];
        XgContainerLayout.WriteEndRecord(endRecord, new XgContainerLayout.EndRecord(
            BodyCrc32: bodyCrc.GetCurrentHashAsUInt32(),
            DataStreamCount: (uint)files.Count,
            ManifestCompressedSize: (uint)compressedManifest.Length,
            ManifestOffset: manifestOffset));
        output.Write(endRecord);
    }

    private static byte[] BuildManifest(IReadOnlyList<InnerFile> files, byte[][] compressed)
    {
        byte[] manifest = new byte[files.Count * XgContainerLayout.ManifestEntrySize];
        uint offset = 0;

        for (int i = 0; i < files.Count; i++)
        {
            XgContainerLayout.WriteManifestEntry(
                manifest.AsSpan(i * XgContainerLayout.ManifestEntrySize, XgContainerLayout.ManifestEntrySize),
                new XgContainerLayout.ManifestEntry(
                    Name: files[i].Name,
                    UncompressedSize: (uint)files[i].Data.Length,
                    CompressedSize: (uint)compressed[i].Length,
                    Offset: offset,
                    Crc32: Crc32.HashToUInt32(files[i].Data)));

            offset += (uint)compressed[i].Length;
        }

        return manifest;
    }

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);
        return ms.ToArray();
    }
}
