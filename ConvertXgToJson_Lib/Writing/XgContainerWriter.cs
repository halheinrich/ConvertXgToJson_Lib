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
///
/// Every inner file that holds bytes is written compressed, its entry
/// recording <see cref="XgContainerLayout.CompressedEntryLevel"/> as XG
/// does. An empty inner file is written as XG writes one: a stored entry
/// with no bytes in the body (storage flag 1,
/// <see cref="XgContainerLayout.StoredEntryLevel"/>, both sizes 0) — the
/// corpus's only zero-length entries, two in 627 files read 2026-10-10,
/// are stored that way, and none is compressed. Compressed, it would be no
/// entry at all: <see cref="ZLibStream"/> writes no bytes for an empty
/// input, and the reader refuses a zero-byte extent as not a zlib stream
/// (halheinrich/backgammon#357). When XG chooses to store an entry that
/// holds bytes is not established, so this writer stores no other.
/// </summary>
internal static class XgContainerWriter
{
    /// <summary>
    /// One inner file of the container: its temp-file name and uncompressed
    /// payload. Order of appearance = physical stream order.
    /// </summary>
    internal readonly record struct InnerFile(string Name, byte[] Data);

    /// <summary>
    /// An inner file as it lies in the body: its bytes there, and how its
    /// entry says they are stored.
    /// </summary>
    private readonly record struct BodyEntry(
        byte[] Bytes, XgContainerLayout.EntryStorage Storage, byte CompressionLevel);

    /// <summary>
    /// Writes each inner file into the body — compressed, or stored when
    /// empty — appends the compressed manifest and the trailing end-record,
    /// and writes the whole payload to <paramref name="output"/>.
    /// </summary>
    internal static void Write(Stream output, IReadOnlyList<InnerFile> files)
    {
        var body = new BodyEntry[files.Count];
        for (int i = 0; i < files.Count; i++)
            body[i] = ToBody(files[i].Data);

        byte[] manifest = BuildManifest(files, body);
        byte[] compressedManifest = Compress(manifest);

        // Manifest offset = sum of the entries' lengths in the body; the
        // body CRC covers every entry's bytes, manifest included.
        uint manifestOffset = 0;
        var bodyCrc = new Crc32();
        foreach (var entry in body)
        {
            manifestOffset += (uint)entry.Bytes.Length;
            bodyCrc.Append(entry.Bytes);
        }
        bodyCrc.Append(compressedManifest);

        foreach (var entry in body)
            output.Write(entry.Bytes);
        output.Write(compressedManifest);

        Span<byte> endRecord = stackalloc byte[XgContainerLayout.EndRecordSize];
        XgContainerLayout.WriteEndRecord(endRecord, new XgContainerLayout.EndRecord(
            BodyCrc32: bodyCrc.GetCurrentHashAsUInt32(),
            InnerFileCount: (uint)files.Count,
            ManifestCompressedSize: (uint)compressedManifest.Length,
            ManifestOffset: manifestOffset));
        output.Write(endRecord);
    }

    /// <summary>
    /// How <paramref name="data"/> lies in the body: one zlib stream, or —
    /// for an empty inner file — nothing, stored.
    /// </summary>
    private static BodyEntry ToBody(byte[] data) => data.Length == 0
        ? new BodyEntry([], XgContainerLayout.EntryStorage.Stored, XgContainerLayout.StoredEntryLevel)
        : new BodyEntry(Compress(data), XgContainerLayout.EntryStorage.Compressed, XgContainerLayout.CompressedEntryLevel);

    private static byte[] BuildManifest(IReadOnlyList<InnerFile> files, BodyEntry[] body)
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
                    CompressedSize: (uint)body[i].Bytes.Length,
                    Offset: offset,
                    Crc32: Crc32.HashToUInt32(files[i].Data),
                    Storage: body[i].Storage,
                    CompressionLevel: body[i].CompressionLevel));

            offset += (uint)body[i].Bytes.Length;
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
