using System.IO.Compression;
using System.IO.Hashing;
using System.Text;

namespace ConvertXgToJson_Lib.Writing;

/// <summary>
/// Writes the compressed XG payload: the concatenated zlib streams that
/// follow the RichGameHeader, then the manifest stream, then a 36-byte
/// uncompressed end-record. Mirror of <see cref="Parsing.XgDecompressor"/>'s
/// container knowledge, extended with the manifest layout and end-record that
/// the reader never needed (it finds stream boundaries by scanning for zlib
/// headers; real XG writes — and this writer reproduces — an explicit
/// directory plus a trailer that XG seeks to from EOF to locate the manifest).
///
/// Stream order matches real XG files: temp.xg, temp.xgr (when rollouts
/// exist), temp.xgi, temp.xgc (when comments exist), then the manifest,
/// then the end-record.
///
/// Manifest layout (decoded from the fixture corpus; one 532-byte entry
/// per inner file, in stream order, the manifest itself unlisted):
///   [0..511]   Pascal ANSI filename (1-byte length + body, zero padded)
///   [512..515] uncompressed size
///   [516..519] compressed size
///   [520..523] offset of the compressed stream, relative to content start
///   [524..527] CRC32 (IEEE) of the uncompressed bytes
///   [528..531] constant 0x200
///
/// End-record layout (36 bytes, uncompressed; nine little-endian int32s,
/// verified against XG-authored .xgp / .xg files). Without it real XG
/// rejects the file — it locates the manifest by seeking from EOF through
/// this trailer, so the reader's forward zlib scan never needed it:
///   [0..3]   CRC32 (IEEE) of the entire compressed body (every data
///            stream and the manifest stream)
///   [4..7]   count of data streams (inner files, manifest excluded)
///   [8..11]  constant 1
///   [12..15] compressed size of the manifest stream
///   [16..19] offset of the manifest stream from content start
///            (= sum of the data streams' compressed sizes)
///   [20..23] constant 1
///   [24..35] zero
/// </summary>
internal static class XgContainerWriter
{
    private const int ManifestEntrySize = 532;
    private const int ManifestNameField = 512;
    private const uint ManifestConstant = 0x200;
    private const int EndRecordSize = 36;

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

        WriteEndRecord(output, bodyCrc.GetCurrentHashAsUInt32(),
            (uint)files.Count, (uint)compressedManifest.Length, manifestOffset);
    }

    /// <summary>
    /// Emits the 36-byte uncompressed end-record that lets XG locate the
    /// manifest by seeking back from EOF. See the class remarks for the field
    /// layout.
    /// </summary>
    private static void WriteEndRecord(
        Stream output, uint bodyCrc, uint dataStreamCount,
        uint manifestCompressedSize, uint manifestOffset)
    {
        Span<byte> record = stackalloc byte[EndRecordSize];
        record.Clear();
        BitConverter.TryWriteBytes(record[0..], bodyCrc);
        BitConverter.TryWriteBytes(record[4..], dataStreamCount);
        BitConverter.TryWriteBytes(record[8..], 1u);
        BitConverter.TryWriteBytes(record[12..], manifestCompressedSize);
        BitConverter.TryWriteBytes(record[16..], manifestOffset);
        BitConverter.TryWriteBytes(record[20..], 1u);
        // [24..35] left zero (fields 7–9).
        output.Write(record);
    }

    private static byte[] BuildManifest(IReadOnlyList<InnerFile> files, byte[][] compressed)
    {
        byte[] manifest = new byte[files.Count * ManifestEntrySize];
        uint offset = 0;

        for (int i = 0; i < files.Count; i++)
        {
            Span<byte> entry = manifest.AsSpan(i * ManifestEntrySize, ManifestEntrySize);

            byte[] name = Encoding.Latin1.GetBytes(files[i].Name);
            entry[0] = (byte)Math.Min(name.Length, ManifestNameField - 1);
            name.AsSpan(0, entry[0]).CopyTo(entry[1..]);

            BitConverter.TryWriteBytes(entry[512..], (uint)files[i].Data.Length);
            BitConverter.TryWriteBytes(entry[516..], (uint)compressed[i].Length);
            BitConverter.TryWriteBytes(entry[520..], offset);
            BitConverter.TryWriteBytes(entry[524..], Crc32.HashToUInt32(files[i].Data));
            BitConverter.TryWriteBytes(entry[528..], ManifestConstant);

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
