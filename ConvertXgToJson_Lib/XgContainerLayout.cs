using System.Buffers.Binary;
using System.Text;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Single source of truth for the XG container's directory layout: the
/// manifest that names each compressed inner file and the 36-byte end-record
/// XG seeks to from EOF to locate that manifest. Consumed by
/// <see cref="Writing.XgContainerWriter"/> (emission) and
/// <see cref="Parsing.XgDecompressor"/> (stream assignment) so the field
/// offsets are encoded exactly once.
///
/// Manifest layout (decoded from the fixture corpus; one 532-byte entry per
/// inner file, in stream order, the manifest itself unlisted):
///   [0..511]   Pascal ANSI filename (1-byte length + body, zero padded)
///   [512..515] uncompressed size
///   [516..519] compressed size
///   [520..523] offset of the compressed stream, relative to content start
///   [524..527] CRC32 (IEEE) of the uncompressed bytes
///   [528..531] constant 0x200
///
/// End-record layout (36 bytes, uncompressed; nine little-endian int32s,
/// verified against XG-authored .xgp / .xg files). Real XG locates the
/// manifest by seeking from EOF through this trailer, so its absence makes
/// the file unloadable even though every other structure validates:
///   [0..3]   CRC32 (IEEE) of the entire compressed body (every data
///            stream and the manifest stream)
///   [4..7]   count of data streams (inner files, manifest excluded)
///   [8..11]  constant 1
///   [12..15] compressed size of the manifest stream
///   [16..19] offset of the manifest stream from content start
///            (= sum of the data streams' compressed sizes)
///   [20..23] constant 1
///   [24..35] zero
///
/// The Try-read methods treat the constant fields as part of the shape: a
/// block whose constants differ is not a layout this library understands,
/// and the reader falls back to heuristic stream assignment rather than
/// trusting it.
/// </summary>
internal static class XgContainerLayout
{
    internal const int ManifestEntrySize = 532;
    internal const int ManifestNameFieldSize = 512;
    internal const uint ManifestEntryConstant = 0x200;
    internal const int EndRecordSize = 36;

    /// <summary>
    /// One manifest entry, sans the layout-owned trailing constant. Offsets
    /// are relative to content start (the first byte after the
    /// RichGameHeader and any thumbnail).
    /// </summary>
    internal readonly record struct ManifestEntry(
        string Name,
        uint UncompressedSize,
        uint CompressedSize,
        uint Offset,
        uint Crc32);

    /// <summary>
    /// The end-record's four variable fields; the constant fields are
    /// layout-owned and written / validated by the codec methods.
    /// </summary>
    internal readonly record struct EndRecord(
        uint BodyCrc32,
        uint DataStreamCount,
        uint ManifestCompressedSize,
        uint ManifestOffset);

    /// <summary>
    /// Encodes <paramref name="entry"/> into <paramref name="destination"/>,
    /// which must be exactly <see cref="ManifestEntrySize"/> bytes.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The destination is not entry-sized, or the name's Latin-1 encoding
    /// exceeds the 255 bytes a Pascal length prefix can express.
    /// </exception>
    internal static void WriteManifestEntry(Span<byte> destination, in ManifestEntry entry)
    {
        if (destination.Length != ManifestEntrySize)
            throw new ArgumentException($"Manifest entry destination must be exactly {ManifestEntrySize} bytes.", nameof(destination));
        byte[] name = Encoding.Latin1.GetBytes(entry.Name);
        if (name.Length > byte.MaxValue)
            throw new ArgumentException($"Inner file name '{entry.Name}' exceeds the 255-byte Pascal length prefix.", nameof(entry));

        destination.Clear();
        destination[0] = (byte)name.Length;
        name.CopyTo(destination[1..]);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[512..], entry.UncompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[516..], entry.CompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[520..], entry.Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[524..], entry.Crc32);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[528..], ManifestEntryConstant);
    }

    /// <summary>
    /// Decodes one manifest entry. Returns <c>false</c> when
    /// <paramref name="source"/> is not entry-sized or its trailing constant
    /// is not <see cref="ManifestEntryConstant"/> — the block is then not a
    /// manifest entry this library understands.
    /// </summary>
    internal static bool TryReadManifestEntry(ReadOnlySpan<byte> source, out ManifestEntry entry)
    {
        entry = default;
        if (source.Length != ManifestEntrySize)
            return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(source[528..]) != ManifestEntryConstant)
            return false;

        entry = new ManifestEntry(
            Encoding.Latin1.GetString(source.Slice(1, source[0])),
            BinaryPrimitives.ReadUInt32LittleEndian(source[512..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[516..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[520..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[524..]));
        return true;
    }

    /// <summary>
    /// Encodes <paramref name="endRecord"/> (plus the layout's constant
    /// fields) into <paramref name="destination"/>, which must be exactly
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
        BinaryPrimitives.WriteUInt32LittleEndian(destination[0..], endRecord.BodyCrc32);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], endRecord.DataStreamCount);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], endRecord.ManifestCompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], endRecord.ManifestOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[20..], 1u);
        // [24..35] left zero (fields 7–9).
    }

    /// <summary>
    /// Decodes the end-record. Returns <c>false</c> when
    /// <paramref name="source"/> is not end-record-sized or any constant
    /// field deviates from the documented shape — the block is then not a
    /// trailer this library understands.
    /// </summary>
    internal static bool TryReadEndRecord(ReadOnlySpan<byte> source, out EndRecord endRecord)
    {
        endRecord = default;
        if (source.Length != EndRecordSize)
            return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(source[8..]) != 1u ||
            BinaryPrimitives.ReadUInt32LittleEndian(source[20..]) != 1u ||
            source[24..].ContainsAnyExcept((byte)0))
        {
            return false;
        }

        endRecord = new EndRecord(
            BinaryPrimitives.ReadUInt32LittleEndian(source[0..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[12..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[16..]));
        return true;
    }
}
