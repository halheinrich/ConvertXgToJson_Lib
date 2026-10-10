using System.Buffers.Binary;
using System.Text;

namespace ConvertXgToJson_Lib;

/// <summary>
/// The inner files an XG container can hold, each named in its manifest
/// (<see cref="XgContainerLayout.NameOf"/>). The reader selects each by
/// name; the writer names each it writes.
/// </summary>
internal enum XgInnerFile
{
    /// <summary><c>temp.xg</c>: the game's save records (2560 bytes each). Every container holds it.</summary>
    GameRecords,

    /// <summary><c>temp.xgi</c>: the first and last save records, as an index.</summary>
    IndexRecords,

    /// <summary><c>temp.xgr</c>: the rollout contexts (2184 bytes each), when any exist.</summary>
    RolloutContexts,

    /// <summary><c>temp.xgc</c>: the comment table, when any comment exists.</summary>
    Comments,
}

/// <summary>
/// Single source of truth for the XG container's directory layout: the
/// manifest that names and locates each inner file, the 36-byte end-record
/// XG seeks to from EOF to locate that manifest, and the inner files'
/// names. Consumed by <see cref="Writing.XgContainerWriter"/> (emission) and
/// <see cref="Parsing.XgDecompressor"/> (reading) so the field offsets are
/// encoded exactly once. This type is a codec: it states where each field
/// lies and what XG writes there; which values the reader accepts, and how
/// it refuses the rest, is <see cref="Parsing.XgDecompressor"/>'s contract.
///
/// <para><b>Evidence.</b> Decoded from the fixture corpus — 434 files,
/// <c>TestData/FixtureFiles/*.xg*</c> and <c>TestData/xg/*.xg</c>, read
/// byte by byte on 2026-10-05 (halheinrich/backgammon#343) — and
/// corroborated by an independent reader, xgdatatools' <c>xgzarc.py</c>
/// (https://github.com/oysteijo/xgdatatools/blob/6b6e60667a8d98a3178039ae669c1f117dc5107d/xgzarc.py),
/// whose names for the fields are given below. This is corroborated reverse
/// engineering, not an XG specification.</para>
///
/// <para><b>Manifest</b> (xgdatatools' <c>FileRecord</c>): one 532-byte entry
/// per inner file, the manifest itself unlisted. Every corpus file lists its
/// entries in body order, their extents tiling the body up to the manifest;
/// the reader relies on neither.</para>
/// <code>
///   [0..255]   name: a Delphi short string — length byte, then up to 255
///              Latin-1 bytes, zero padded
///   [256..511] path: a second Delphi short string; empty in every corpus
///              entry, never read
///   [512..515] uncompressed size
///   [516..519] compressed size: the entry's length in the body — its zlib
///              stream's length when compressed, its raw length (equal to the
///              uncompressed size) when stored, as a ZIP stored member's
///              compressed size is
///   [520..523] offset of the entry's bytes, relative to content start
///   [524..527] CRC32 (IEEE) of the uncompressed bytes
///   [528]      storage flag (xgdatatools' compressed, true when the byte is
///              0): 0 = a zlib stream, 1 = raw bytes; see EntryStorage
///   [529]      compression level: 2 in every compressed corpus entry, 0 in
///              every stored one
///   [530..531] padding: zero in every corpus entry, never read
/// </code>
/// <para>In the corpus 1018 entries read flag 0 with level 2, and their zlib
/// stream fills exactly their compressed size; 3 read flag 1 with level 0,
/// their two sizes equal: <c>temp.xgc</c> holding two bytes (CR LF) in
/// one <c>.xgp</c>, and a zero-length <c>temp.xgr</c> and <c>temp.xgc</c>
/// in one <c>.xg</c>, the latter lying exactly at the manifest's offset.
/// When XG chooses to store an entry is not established; that every
/// compressed corpus entry is smaller than its content is an observation,
/// not a rule.</para>
///
/// <para><b>End-record</b> (xgdatatools' <c>ArchiveRecord</c>): 36 bytes,
/// uncompressed, six little-endian int32s then twelve reserved bytes. Real
/// XG locates the manifest by seeking from EOF through it, so a container
/// without it is unloadable even though every other structure validates.</para>
/// <code>
///   [0..3]   crc: CRC32 (IEEE) of the entire body — every inner file's
///            bytes and the manifest stream
///   [4..7]   filecount: count of inner files (the manifest excluded)
///   [8..11]  version: 1 in every corpus file
///   [12..15] registrysize: the manifest stream's compressed size
///   [16..19] archivesize: the manifest's offset from content start (= the
///            length of the inner files' region before it)
///   [20..23] compressedregistry: 1 in every corpus file — the manifest is a
///            zlib stream
///   [24..35] reserved: zero in every corpus file
/// </code>
/// <para><see cref="TryReadEndRecord"/> treats the version, the
/// compressed-registry flag and the reserved bytes as the end-record's
/// shape: a block whose fields hold other values is not an end-record this
/// library understands.</para>
/// </summary>
internal static class XgContainerLayout
{
    internal const int ManifestEntrySize = 532;
    internal const int EndRecordSize = 36;

    /// <summary>The one end-record version the corpus evidences.</summary>
    internal const uint EndRecordVersion = 1;

    /// <summary>The compressed-registry value the corpus evidences: the manifest is a zlib stream.</summary>
    internal const uint EndRecordCompressedRegistry = 1;

    /// <summary>
    /// The compression level every compressed corpus entry records — the
    /// value <see cref="Writing.XgContainerWriter"/> writes. Never a gate on
    /// read: a zlib stream describes its own compression.
    /// </summary>
    internal const byte CompressedEntryLevel = 2;

    /// <summary>
    /// The compression level every stored corpus entry records — the value
    /// <see cref="Writing.XgContainerWriter"/> writes for the one entry it
    /// stores, an empty inner file. Never a gate on read.
    /// </summary>
    internal const byte StoredEntryLevel = 0;

    // Manifest entry field offsets.
    private const int NameFieldSize = 256;
    private const int UncompressedSizeOffset = 512;
    private const int CompressedSizeOffset = 516;
    private const int EntryOffsetOffset = 520;
    private const int Crc32Offset = 524;
    private const int StorageFlagOffset = 528;
    private const int CompressionLevelOffset = 529;

    // End-record field offsets.
    private const int BodyCrc32Offset = 0;
    private const int InnerFileCountOffset = 4;
    private const int VersionOffset = 8;
    private const int ManifestCompressedSizeOffset = 12;
    private const int ManifestOffsetOffset = 16;
    private const int CompressedRegistryOffset = 20;
    private const int ReservedOffset = 24;

    /// <summary>
    /// How a manifest entry's bytes lie in the body, as its storage flag
    /// (byte 528) states it. The two members are the two values the corpus
    /// evidences; a decoded entry may carry any other byte, which the reader
    /// refuses.
    /// </summary>
    internal enum EntryStorage : byte
    {
        /// <summary>Flag 0: the entry is one zlib stream filling its compressed size.</summary>
        Compressed = 0,

        /// <summary>Flag 1: the entry is its raw bytes; its two sizes are equal.</summary>
        Stored = 1,
    }

    /// <summary>
    /// One manifest entry, its path field and padding aside (never read;
    /// written empty). Offsets are relative to content start (the first
    /// byte after the RichGameHeader and any thumbnail).
    /// </summary>
    /// <param name="Name">The inner file's name, decoded as Latin-1.</param>
    /// <param name="UncompressedSize">The inner file's length.</param>
    /// <param name="CompressedSize">The entry's length in the body (see the layout).</param>
    /// <param name="Offset">Where the entry's bytes begin in the body.</param>
    /// <param name="Crc32">CRC32 (IEEE) of the inner file's bytes.</param>
    /// <param name="Storage">The storage flag, as decoded; not necessarily a defined member.</param>
    /// <param name="CompressionLevel">The compression level, as decoded.</param>
    internal readonly record struct ManifestEntry(
        string Name,
        uint UncompressedSize,
        uint CompressedSize,
        uint Offset,
        uint Crc32,
        EntryStorage Storage,
        byte CompressionLevel)
    {
        /// <summary>The body offset one past the entry's last byte, computed without overflow.</summary>
        internal long End => (long)Offset + CompressedSize;
    }

    /// <summary>
    /// The end-record's four variable fields; the version, the
    /// compressed-registry flag and the reserved bytes are its shape,
    /// written and recognized by the codec methods.
    /// </summary>
    /// <param name="BodyCrc32">CRC32 (IEEE) of the body.</param>
    /// <param name="InnerFileCount">Count of inner files, the manifest excluded.</param>
    /// <param name="ManifestCompressedSize">The manifest stream's compressed size.</param>
    /// <param name="ManifestOffset">The manifest's offset from content start.</param>
    internal readonly record struct EndRecord(
        uint BodyCrc32,
        uint InnerFileCount,
        uint ManifestCompressedSize,
        uint ManifestOffset);

    /// <summary>The name a container's manifest gives <paramref name="file"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="file"/> is not a defined member.</exception>
    internal static string NameOf(XgInnerFile file) => file switch
    {
        XgInnerFile.GameRecords => "temp.xg",
        XgInnerFile.IndexRecords => "temp.xgi",
        XgInnerFile.RolloutContexts => "temp.xgr",
        XgInnerFile.Comments => "temp.xgc",
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "Not an XG inner file."),
    };

    /// <summary>
    /// The inner file a manifest name names, matched case-insensitively (the
    /// names are Windows file names); <c>false</c> for any other name.
    /// </summary>
    internal static bool TryParseInnerFileName(string name, out XgInnerFile file)
    {
        foreach (XgInnerFile candidate in Enum.GetValues<XgInnerFile>())
        {
            if (string.Equals(name, NameOf(candidate), StringComparison.OrdinalIgnoreCase))
            {
                file = candidate;
                return true;
            }
        }
        file = default;
        return false;
    }

    /// <summary>
    /// Encodes <paramref name="entry"/> into <paramref name="destination"/>,
    /// which must be exactly <see cref="ManifestEntrySize"/> bytes. The path
    /// field and the padding are written zero.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The destination is not entry-sized, the name's Latin-1 encoding
    /// exceeds the 255 bytes a Delphi short string can hold, or the storage
    /// is not a defined <see cref="EntryStorage"/> member.
    /// </exception>
    internal static void WriteManifestEntry(Span<byte> destination, in ManifestEntry entry)
    {
        if (destination.Length != ManifestEntrySize)
            throw new ArgumentException($"Manifest entry destination must be exactly {ManifestEntrySize} bytes.", nameof(destination));
        byte[] name = Encoding.Latin1.GetBytes(entry.Name);
        if (name.Length >= NameFieldSize)
            throw new ArgumentException($"Inner file name '{entry.Name}' exceeds the 255-byte Delphi short string.", nameof(entry));
        if (!Enum.IsDefined(entry.Storage))
            throw new ArgumentException($"Storage flag {(byte)entry.Storage} is not a known entry storage.", nameof(entry));

        destination.Clear();
        destination[0] = (byte)name.Length;
        name.CopyTo(destination[1..]);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[UncompressedSizeOffset..], entry.UncompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[CompressedSizeOffset..], entry.CompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[EntryOffsetOffset..], entry.Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[Crc32Offset..], entry.Crc32);
        destination[StorageFlagOffset] = (byte)entry.Storage;
        destination[CompressionLevelOffset] = entry.CompressionLevel;
        // [256..511] (path) and [530..531] (padding) left zero.
    }

    /// <summary>
    /// Decodes one manifest entry, every field as its bytes state it: the
    /// storage flag may be a value <see cref="EntryStorage"/> does not
    /// define, and the path field and padding are not read.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="source"/> is not entry-sized.</exception>
    internal static ManifestEntry ReadManifestEntry(ReadOnlySpan<byte> source)
    {
        if (source.Length != ManifestEntrySize)
            throw new ArgumentException($"A manifest entry is exactly {ManifestEntrySize} bytes.", nameof(source));

        return new ManifestEntry(
            Encoding.Latin1.GetString(source.Slice(1, source[0])),
            BinaryPrimitives.ReadUInt32LittleEndian(source[UncompressedSizeOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[CompressedSizeOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[EntryOffsetOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[Crc32Offset..]),
            (EntryStorage)source[StorageFlagOffset],
            source[CompressionLevelOffset]);
    }

    /// <summary>
    /// Encodes <paramref name="endRecord"/> (plus the layout's version,
    /// compressed-registry flag and zero reserved bytes) into
    /// <paramref name="destination"/>, which must be exactly
    /// <see cref="EndRecordSize"/> bytes.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The destination is not end-record-sized.
    /// </exception>
    internal static void WriteEndRecord(Span<byte> destination, in EndRecord endRecord)
    {
        if (destination.Length != EndRecordSize)
            throw new ArgumentException($"End-record destination must be exactly {EndRecordSize} bytes.", nameof(destination));

        destination.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(destination[BodyCrc32Offset..], endRecord.BodyCrc32);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[InnerFileCountOffset..], endRecord.InnerFileCount);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[VersionOffset..], EndRecordVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[ManifestCompressedSizeOffset..], endRecord.ManifestCompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[ManifestOffsetOffset..], endRecord.ManifestOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[CompressedRegistryOffset..], EndRecordCompressedRegistry);
        // [24..35] (reserved) left zero.
    }

    /// <summary>
    /// Decodes the end-record. Returns <c>false</c> when
    /// <paramref name="source"/> is not end-record-sized or its shape
    /// deviates — a version other than <see cref="EndRecordVersion"/>, a
    /// compressed-registry flag other than
    /// <see cref="EndRecordCompressedRegistry"/>, or a non-zero reserved
    /// byte: the block is then not an end-record this library understands.
    /// </summary>
    internal static bool TryReadEndRecord(ReadOnlySpan<byte> source, out EndRecord endRecord)
    {
        endRecord = default;
        if (source.Length != EndRecordSize)
            return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(source[VersionOffset..]) != EndRecordVersion ||
            BinaryPrimitives.ReadUInt32LittleEndian(source[CompressedRegistryOffset..]) != EndRecordCompressedRegistry ||
            source[ReservedOffset..].ContainsAnyExcept((byte)0))
        {
            return false;
        }

        endRecord = new EndRecord(
            BinaryPrimitives.ReadUInt32LittleEndian(source[BodyCrc32Offset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[InnerFileCountOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[ManifestCompressedSizeOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[ManifestOffsetOffset..]));
        return true;
    }
}
