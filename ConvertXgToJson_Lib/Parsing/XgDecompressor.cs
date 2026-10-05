using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.IO.Hashing;

namespace ConvertXgToJson_Lib.Parsing;

/// <summary>
/// Reads an XG payload — every byte after the RichGameHeader and any
/// thumbnail — into its inner files (<see cref="XgInnerFile"/>):
///
///   temp.xg   - N x 2560 bytes  (TSaveRec records)
///   temp.xgi  - 2 x 2560 bytes  (first + last TSaveRec, always 5120 bytes)
///   temp.xgr  - M x 2184 bytes  (TRolloutContext records, may be absent)
///   temp.xgc  - variable         (RTF comments, may be absent)
///
/// <para><b>Which format: the directory decides first.</b> A payload whose
/// directory — the end-record and manifest <see cref="XgContainerLayout"/>
/// describes — passes every check below is a container, read through that
/// directory with each inner file assigned by its manifest name, even if
/// the payload might also parse as one zlib stream. Only when the directory
/// fails a check is the payload tested for the legacy single-stream format:
/// one zlib stream that begins at its first byte and whose compressed
/// extent ends at the payload's last byte. That stream holds temp.xg with
/// temp.xgi's two records at its tail, which are split off when the stream
/// is longer than them. A payload that is neither is refused with
/// <see cref="InvalidDataException"/> whose message names the directory
/// check that failed; nothing is assigned by guesswork. So reading a
/// container inflates nothing but its manifest, bounded by the size its
/// entry count implies, and the inner files a surface asks for, each
/// bounded by its declared size. One limit follows: a container cut off
/// exactly at the end of its first stream has no directory and is one
/// complete zlib stream, so it cannot be told from a legacy file and reads
/// as one.</para>
///
/// <para><b>The directory</b> is validated in full before any inner file
/// is extracted, the same for every read surface: the payload holds at
/// least an end-record; its last 36 bytes are one (version 1, compressed
/// registry 1, reserved bytes zero); it counts at least one inner file; the
/// manifest it locates ends where the end-record begins; the body's CRC32
/// matches it; the manifest is one zlib stream filling exactly its extent
/// and inflates to exactly one 532-byte entry per counted file. Then each
/// entry: it names an inner file (case-insensitively), and no other entry names the
/// same one; its storage flag is 0 (compressed) or 1 (stored), and no other
/// value is read as either; a stored entry's two sizes are equal; and its
/// extent lies wholly before the manifest, computed without overflow, so a
/// zero-length entry lying exactly at the manifest's offset is valid.
/// Finally the manifest names temp.xg. The compression level is not a gate,
/// and the path field and padding are never read.</para>
///
/// <para><b>Extraction</b> applies one rule to each inner file a surface
/// asks for, and to no other: a stored entry is its raw bytes; a compressed
/// entry is one zlib stream read within its compressed extent, which it
/// must fill exactly and must not inflate past its declared size. Either
/// way the result must hold exactly the entry's uncompressed size, and its
/// CRC32 must match the entry's. The CRC check verifies the reading, where
/// the body CRC verifies the bytes: the bytes can be intact and still be
/// taken from the wrong extent or by the wrong storage, which is the
/// mistake halheinrich/backgammon#343 corrected. An inner file the manifest
/// does not name reads as empty. Success certifies only the inner files
/// extracted: a metadata read of temp.xg says nothing of temp.xgc.</para>
/// </summary>
internal static class XgDecompressor
{
    // Record sizes are owned by the parsers; referenced here for stream sizing.
    private const int XgiSize = 2 * SaveRecordParser.RecordSize;  // 5120

    /// <summary>
    /// The full read: opens the payload and extracts all four inner files,
    /// an absent one as an empty stream.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The payload is not one zlib stream and its directory cannot be used,
    /// or an inner file fails extraction.
    /// </exception>
    public static XgDecompressedStreams Decompress(Stream compressedStream)
    {
        Payload payload = Payload.Open(ReadAllBytes(compressedStream));
        return new XgDecompressedStreams(
            ToStream(payload.Read(XgInnerFile.GameRecords)),
            ToStream(payload.Read(XgInnerFile.IndexRecords)),
            ToStream(payload.Read(XgInnerFile.RolloutContexts)),
            ToStream(payload.Read(XgInnerFile.Comments)));
    }

    /// <summary>
    /// The metadata read: opens the payload by the same rule as
    /// <see cref="Decompress"/> and extracts temp.xg alone; no other inner
    /// file is inflated, extracted or checked.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The payload is not one zlib stream and its directory cannot be used,
    /// or temp.xg fails extraction.
    /// </exception>
    public static byte[] DecompressGameRecords(Stream compressedStream) =>
        Payload.Open(ReadAllBytes(compressedStream)).Read(XgInnerFile.GameRecords);

    // -----------------------------------------------------------------------
    //  The payload — the one rule every read surface opens it by
    // -----------------------------------------------------------------------

    /// <summary>
    /// An opened payload, its format decided and (for a container) its
    /// directory validated; <see cref="Read"/> extracts one inner file.
    /// </summary>
    private abstract class Payload
    {
        /// <summary>
        /// Decides the payload's format by <see cref="XgDecompressor"/>'s
        /// rule — the directory first, the single stream only when the
        /// directory fails — extracting no inner file.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// Neither holds; the message names the directory check that failed.
        /// </exception>
        public static Payload Open(byte[] raw)
        {
            if (ContainerPayload.TryReadDirectory(raw, out var container, out string? failedCheck))
                return container;
            if (Inflate(raw, 0, raw.Length, long.MaxValue, out byte[] single) == InflateOutcome.Complete)
                return new SingleStreamPayload(single);
            throw DirectoryRefused(failedCheck);
        }

        /// <summary>The bytes of <paramref name="file"/>; empty when the payload holds none.</summary>
        public abstract byte[] Read(XgInnerFile file);
    }

    /// <summary>The legacy format: one zlib stream, temp.xg then temp.xgi.</summary>
    private sealed class SingleStreamPayload : Payload
    {
        private readonly byte[] _gameRecords;
        private readonly byte[] _indexRecords;

        public SingleStreamPayload(byte[] stream)
        {
            if (stream.Length > XgiSize)
            {
                _gameRecords = stream[..^XgiSize];
                _indexRecords = stream[^XgiSize..];
            }
            else
            {
                _gameRecords = stream;
                _indexRecords = [];
            }
        }

        public override byte[] Read(XgInnerFile file) => file switch
        {
            XgInnerFile.GameRecords => _gameRecords,
            XgInnerFile.IndexRecords => _indexRecords,
            XgInnerFile.RolloutContexts or XgInnerFile.Comments => [],
            _ => throw new ArgumentOutOfRangeException(nameof(file), file, "Not an XG inner file."),
        };
    }

    /// <summary>A container, read through its validated directory.</summary>
    private sealed class ContainerPayload : Payload
    {
        private readonly byte[] _raw;
        private readonly Dictionary<XgInnerFile, XgContainerLayout.ManifestEntry> _entries;

        private ContainerPayload(byte[] raw, Dictionary<XgInnerFile, XgContainerLayout.ManifestEntry> entries)
        {
            _raw = raw;
            _entries = entries;
        }

        /// <summary>
        /// Reads and validates the directory, extracting no inner file:
        /// <c>true</c> with the container when every check holds, otherwise
        /// <c>false</c> with the first check that failed, phrased for the
        /// refusal message.
        /// </summary>
        public static bool TryReadDirectory(
            byte[] raw,
            [NotNullWhen(true)] out ContainerPayload? payload,
            [NotNullWhen(false)] out string? failedCheck)
        {
            var entries = new Dictionary<XgInnerFile, XgContainerLayout.ManifestEntry>();
            failedCheck = FirstFailedCheck(raw, entries);
            if (failedCheck is not null)
            {
                payload = null;
                return false;
            }
            payload = new ContainerPayload(raw, entries);
            return true;
        }

        /// <summary>
        /// Runs the directory's checks in the order the class doc states,
        /// filling <paramref name="entries"/>; returns the first check that
        /// fails, or <c>null</c> when all hold.
        /// </summary>
        private static string? FirstFailedCheck(
            byte[] raw, Dictionary<XgInnerFile, XgContainerLayout.ManifestEntry> entries)
        {
            const int endRecordSize = XgContainerLayout.EndRecordSize;
            if (raw.Length < endRecordSize)
                return $"the payload holds {raw.Length} bytes, too few for the {endRecordSize}-byte end-record";
            if (!XgContainerLayout.TryReadEndRecord(raw.AsSpan(raw.Length - endRecordSize), out var trailer))
                return
                    $"its last {endRecordSize} bytes are not an end-record (version {XgContainerLayout.EndRecordVersion}, " +
                    $"compressed registry {XgContainerLayout.EndRecordCompressedRegistry}, reserved bytes zero)";

            int bodyLength = raw.Length - endRecordSize;
            if (trailer.InnerFileCount == 0)
                return "the end-record counts no inner files";
            long manifestEnd = (long)trailer.ManifestOffset + trailer.ManifestCompressedSize;
            if (manifestEnd != bodyLength)
                return
                    $"the end-record places the manifest at bytes {trailer.ManifestOffset} to {manifestEnd} of the body, " +
                    $"which does not end where the end-record begins, at byte {bodyLength}";
            uint bodyCrc = Crc32.HashToUInt32(raw.AsSpan(0, bodyLength));
            if (bodyCrc != trailer.BodyCrc32)
                return $"the body's CRC32 is 0x{bodyCrc:X8}, but the end-record states 0x{trailer.BodyCrc32:X8}";

            long manifestLength = (long)trailer.InnerFileCount * XgContainerLayout.ManifestEntrySize;
            string entriesNeed = $"the end-record's {trailer.InnerFileCount} entries need {manifestLength}";
            switch (Inflate(raw, (int)trailer.ManifestOffset, (int)trailer.ManifestCompressedSize, manifestLength, out byte[] manifest))
            {
                case InflateOutcome.Complete:
                    break;
                case InflateOutcome.ExceedsLimit:
                    return $"the manifest inflates to more bytes than {entriesNeed}";
                case var fault:
                    return $"the manifest's compressed bytes {StreamFault(fault, trailer.ManifestCompressedSize)}";
            }
            if (manifest.Length != manifestLength)
                return $"the manifest inflates to {manifest.Length} bytes, but {entriesNeed}";

            for (int pos = 0; pos < manifest.Length; pos += XgContainerLayout.ManifestEntrySize)
            {
                var entry = XgContainerLayout.ReadManifestEntry(
                    manifest.AsSpan(pos, XgContainerLayout.ManifestEntrySize));
                string entryName = $"manifest entry '{entry.Name}'";

                if (!XgContainerLayout.TryParseInnerFileName(entry.Name, out XgInnerFile file))
                    return $"{entryName} does not name an XG inner file";
                if (entries.ContainsKey(file))
                    return $"the manifest names {XgContainerLayout.NameOf(file)} more than once";
                if (!Enum.IsDefined(entry.Storage))
                    return
                        $"{entryName} has storage flag {(byte)entry.Storage}, where only 0 (compressed) and 1 (stored) are known";
                if (entry.Storage == XgContainerLayout.EntryStorage.Stored && entry.CompressedSize != entry.UncompressedSize)
                    return
                        $"{entryName} is stored, but its stored size ({entry.CompressedSize}) differs from its " +
                        $"uncompressed size ({entry.UncompressedSize})";
                if (entry.End > trailer.ManifestOffset)
                    return
                        $"{entryName} spans bytes {entry.Offset} to {entry.End} of the body, past the manifest at byte {trailer.ManifestOffset}";

                entries.Add(file, entry);
            }
            if (!entries.ContainsKey(XgInnerFile.GameRecords))
                return $"the manifest names no {XgContainerLayout.NameOf(XgInnerFile.GameRecords)}";

            return null;
        }

        /// <summary>Extracts <paramref name="file"/> by the class doc's one extraction rule.</summary>
        public override byte[] Read(XgInnerFile file)
        {
            if (!_entries.TryGetValue(file, out var entry))
                return [];

            // The directory bounded every extent within the body, so each
            // fits an int.
            int offset = (int)entry.Offset;
            int length = (int)entry.CompressedSize;
            byte[] data;
            if (entry.Storage == XgContainerLayout.EntryStorage.Stored)
            {
                data = _raw.AsSpan(offset, length).ToArray();
            }
            else
            {
                switch (Inflate(_raw, offset, length, entry.UncompressedSize, out data))
                {
                    case InflateOutcome.Complete:
                        break;
                    case InflateOutcome.ExceedsLimit:
                        throw InnerFileRefused(entry.Name,
                            $"it inflates to more than the {entry.UncompressedSize} bytes its manifest entry declares");
                    case var fault:
                        throw InnerFileRefused(entry.Name, $"its compressed bytes {StreamFault(fault, entry.CompressedSize)}");
                }
            }

            if (data.Length != entry.UncompressedSize)
                throw InnerFileRefused(entry.Name,
                    $"it inflates to {data.Length} bytes, but its manifest entry declares {entry.UncompressedSize}");
            uint crc = Crc32.HashToUInt32(data);
            if (crc != entry.Crc32)
                throw InnerFileRefused(entry.Name,
                    $"its CRC32 is 0x{crc:X8}, but its manifest entry states 0x{entry.Crc32:X8}");
            return data;
        }
    }

    private static InvalidDataException DirectoryRefused(string check) =>
        new($"The XG payload is not a single zlib stream, and its container directory cannot be used: {check}.");

    private static InvalidDataException InnerFileRefused(string name, string check) =>
        new($"The XG container's inner file '{name}' cannot be read: {check}.");

    // -----------------------------------------------------------------------
    //  Exact-extent inflation
    // -----------------------------------------------------------------------

    /// <summary>How an extent's bytes read as one zlib stream.</summary>
    private enum InflateOutcome
    {
        /// <summary>One zlib stream fills the extent exactly.</summary>
        Complete,

        /// <summary>The bytes are not a valid zlib stream.</summary>
        NotZlib,

        /// <summary>The stream needs bytes beyond the extent's end.</summary>
        RunsPastExtent,

        /// <summary>The stream ends before the extent's last byte.</summary>
        EndsWithinExtent,

        /// <summary>The stream inflates past the caller's limit; inflation stopped there.</summary>
        ExceedsLimit,
    }

    /// <summary>The phrase naming a stream-shape outcome, for an extent of <paramref name="extent"/> bytes.</summary>
    private static string StreamFault(InflateOutcome outcome, uint extent) => outcome switch
    {
        InflateOutcome.NotZlib => "are not a valid zlib stream",
        InflateOutcome.RunsPastExtent => $"are a zlib stream that runs past its {extent}-byte extent",
        InflateOutcome.EndsWithinExtent => $"are a zlib stream that ends before its {extent}-byte extent does",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a stream-shape fault."),
    };

    /// <summary>
    /// Inflates <paramref name="raw"/>[<paramref name="offset"/>, +<paramref name="length"/>)
    /// as one zlib stream that must fill that extent exactly, stopping as
    /// soon as the output passes <paramref name="limit"/> bytes.
    /// <paramref name="data"/> is the output when the outcome is
    /// <see cref="InflateOutcome.Complete"/>, and empty otherwise.
    /// </summary>
    private static InflateOutcome Inflate(byte[] raw, int offset, int length, long limit, out byte[] data)
    {
        data = [];
        var feed = new ExtentFeed(raw, offset, length);
        using var output = new MemoryStream();
        try
        {
            using var zlib = new ZLibStream(feed, CompressionMode.Decompress);
            byte[] buffer = new byte[16 * 1024];
            int read;
            // Read, never CopyTo: DeflateStream.CopyTo pumps its whole source
            // through the inflater, so it would read past any stream's end.
            while ((read = zlib.Read(buffer)) > 0)
            {
                if (output.Length + read > limit)
                    return InflateOutcome.ExceedsLimit;
                output.Write(buffer, 0, read);
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            // Malformed deflate data raises InvalidDataException; a header
            // asking for a preset dictionary raises ZLibException, an
            // IOException outside the public surface. The feed is in memory,
            // so no other IOException can arise here.
            return InflateOutcome.NotZlib;
        }

        if (feed.ReadPastEnd)
            return InflateOutcome.RunsPastExtent;
        if (!feed.EndServed)
            return InflateOutcome.EndsWithinExtent;
        data = output.ToArray();
        return InflateOutcome.Complete;
    }

    /// <summary>
    /// Serves one extent of a byte array to a <see cref="ZLibStream"/> so
    /// that its caller learns whether the zlib stream ended exactly at the
    /// extent's last byte — which <see cref="ZLibStream"/> does not report:
    /// it reads a stream cut short by the end of its input without
    /// complaint, and never says where a complete one ended.
    ///
    /// <para>The extent is served whole but for its last byte, which is
    /// served alone on a later read; any read after that is recorded as
    /// running past the extent. <c>DeflateStream.Read</c> (.NET 10) takes
    /// input from its source only while the inflater is unfinished and has
    /// consumed all it was given, so a stream ending exactly at the extent's
    /// end takes the last byte and stops (<see cref="EndServed"/>, not
    /// <see cref="ReadPastEnd"/>); one ending earlier never asks for the last
    /// byte; one needing more than the extent asks again
    /// (<see cref="ReadPastEnd"/>). The extent tests in
    /// <c>XgContainerDirectoryTests</c> pin all three against that
    /// behaviour.</para>
    /// </summary>
    private sealed class ExtentFeed(byte[] source, int offset, int length) : Stream
    {
        private int _served;

        /// <summary>Whether the extent's last byte was served.</summary>
        public bool EndServed => length > 0 && _served == length;

        /// <summary>Whether a read asked for input after the extent's last byte.</summary>
        public bool ReadPastEnd { get; private set; }

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
                return 0;
            int last = length - 1;
            if (_served < last)
            {
                int count = Math.Min(buffer.Length, last - _served);
                source.AsSpan(offset + _served, count).CopyTo(buffer);
                _served += count;
                return count;
            }
            if (_served == last)
            {
                buffer[0] = source[offset + last];
                _served++;
                return 1;
            }
            ReadPastEnd = true;
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // -----------------------------------------------------------------------

    private static byte[] ReadAllBytes(Stream source)
    {
        using var ms = new MemoryStream();
        source.CopyTo(ms);
        return ms.ToArray();
    }

    private static MemoryStream ToStream(byte[] data)
    {
        if (data.Length == 0) return new MemoryStream(0);
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
