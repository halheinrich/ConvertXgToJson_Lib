using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ConvertXgToJson_Lib.Tests.Helpers;

/// <summary>
/// Builds XG container payloads byte by byte for the reader's directory
/// tests (halheinrich/backgammon#343). It spells the evidenced layout on its
/// own, independently of <see cref="XgContainerLayout"/>, so a mistake in the
/// production codec fails these tests instead of being shared by them; and
/// it can write what the production writer never does — stored entries, any
/// compression level, any storage flag. Every field can be overridden, so a
/// refusal control breaks exactly the check it names: the body CRC and the
/// end-record are computed from whatever was built unless overridden too.
/// </summary>
internal static class RawXgContainer
{
    // Manifest entry (xgdatatools' FileRecord), 532 bytes.
    private const int EntrySize = 532;
    private const int PathField = 256;
    private const int UncompressedSizeField = 512;
    private const int CompressedSizeField = 516;
    private const int OffsetField = 520;
    private const int Crc32Field = 524;
    private const int FlagField = 528;
    private const int LevelField = 529;
    private const int PaddingField = 530;

    // End-record (xgdatatools' ArchiveRecord), 36 bytes.
    internal const int EndRecordSize = 36;

    /// <summary>
    /// One inner file: its manifest name, its content, and how its entry and
    /// body bytes are written. By default it is compressed (flag 0, level 2)
    /// and every field states the truth about the bytes written.
    /// </summary>
    internal sealed record Entry(string Name, byte[] Content)
    {
        /// <summary>Stored as raw bytes (flag 1, level 0) instead of compressed.</summary>
        public bool Stored { get; init; }

        /// <summary>Overrides the storage flag byte.</summary>
        public byte? Flag { get; init; }

        /// <summary>Overrides the compression level byte.</summary>
        public byte? Level { get; init; }

        /// <summary>The bytes placed in the body; by default zlib of the content, or the content when stored.</summary>
        public byte[]? Body { get; init; }

        /// <summary>Overrides the declared uncompressed size (default: the content's length).</summary>
        public uint? UncompressedSize { get; init; }

        /// <summary>Overrides the declared compressed size (default: the body bytes' length).</summary>
        public uint? CompressedSize { get; init; }

        /// <summary>Overrides the declared offset (default: where the body bytes were written).</summary>
        public uint? Offset { get; init; }

        /// <summary>Overrides the declared CRC32 (default: the content's).</summary>
        public uint? Crc32 { get; init; }

        /// <summary>A path written into the entry's path field (default: none).</summary>
        public string Path { get; init; } = "";

        /// <summary>The two padding bytes (default: zero).</summary>
        public ushort Padding { get; init; }
    }

    /// <summary>Overrides of the end-record's fields; each <c>null</c> keeps the computed value.</summary>
    internal sealed record Trailer
    {
        public uint? Crc { get; init; }
        public uint? FileCount { get; init; }
        public uint? Version { get; init; }
        public uint? RegistrySize { get; init; }
        public uint? ArchiveSize { get; init; }
        public uint? CompressedRegistry { get; init; }

        /// <summary>A byte written at the end of the reserved field (default: zero).</summary>
        public byte LastReservedByte { get; init; }
    }

    /// <summary>
    /// The payload: each entry's body bytes in order, the manifest stream,
    /// then the end-record. <paramref name="encodeManifest"/> turns the raw
    /// manifest into the bytes placed in the body (default: one zlib stream).
    /// </summary>
    public static byte[] Payload(
        IReadOnlyList<Entry> entries,
        Trailer? trailer = null,
        Func<byte[], byte[]>? encodeManifest = null)
    {
        trailer ??= new Trailer();
        using var body = new MemoryStream();
        byte[] manifest = new byte[entries.Count * EntrySize];

        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            byte[] bytes = e.Body ?? (e.Stored ? e.Content : Zlib(e.Content));
            uint offset = e.Offset ?? (uint)body.Length;
            body.Write(bytes);

            Span<byte> entry = manifest.AsSpan(i * EntrySize, EntrySize);
            WriteShortString(entry, e.Name);
            WriteShortString(entry[PathField..], e.Path);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[UncompressedSizeField..], e.UncompressedSize ?? (uint)e.Content.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[CompressedSizeField..], e.CompressedSize ?? (uint)bytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[OffsetField..], offset);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[Crc32Field..], e.Crc32 ?? System.IO.Hashing.Crc32.HashToUInt32(e.Content));
            entry[FlagField] = e.Flag ?? (e.Stored ? (byte)1 : (byte)0);
            entry[LevelField] = e.Level ?? (e.Stored ? (byte)0 : (byte)2);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[PaddingField..], e.Padding);
        }

        uint archiveSize = (uint)body.Length;
        byte[] manifestBytes = encodeManifest is null ? Zlib(manifest) : encodeManifest(manifest);
        body.Write(manifestBytes);
        byte[] bodyBytes = body.ToArray();

        byte[] end = new byte[EndRecordSize];
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(0), trailer.Crc ?? System.IO.Hashing.Crc32.HashToUInt32(bodyBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(4), trailer.FileCount ?? (uint)entries.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(8), trailer.Version ?? 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(12), trailer.RegistrySize ?? (uint)manifestBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(16), trailer.ArchiveSize ?? archiveSize);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(20), trailer.CompressedRegistry ?? 1u);
        end[^1] = trailer.LastReservedByte;

        return [.. bodyBytes, .. end];
    }

    /// <summary>A whole file: a synthetic RichGameHeader, then <paramref name="payload"/>.</summary>
    public static byte[] File(byte[] payload) => [.. XgBytesBuilder.BuildRichGameHeader(), .. payload];

    /// <summary>
    /// One complete zlib stream of <paramref name="data"/>. An empty input
    /// gets the eight-byte stream of no data, which <see cref="ZLibStream"/>
    /// does not emit when nothing is written to it.
    /// </summary>
    public static byte[] Zlib(byte[] data) => Zlib(data, CompressionLevel.Optimal);

    /// <inheritdoc cref="Zlib(byte[])"/>
    public static byte[] Zlib(byte[] data, CompressionLevel level)
    {
        if (data.Length == 0)
            return [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, level, leaveOpen: true))
            z.Write(data);
        return ms.ToArray();
    }

    private static void WriteShortString(Span<byte> field, string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        field[0] = (byte)bytes.Length;
        bytes.CopyTo(field[1..]);
    }
}

/// <summary>
/// A file written under the temp directory for the path-taking read
/// surfaces, deleted on dispose.
/// </summary>
internal sealed class TempXgFile : IDisposable
{
    public TempXgFile(byte[] bytes)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.xg");
        System.IO.File.WriteAllBytes(Path, bytes);
    }

    public string Path { get; }

    public void Dispose() => System.IO.File.Delete(Path);
}
