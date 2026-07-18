using System.Buffers.Binary;
using System.Text;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Parsing;

/// <summary>
/// Parses XG's opening-book database (<c>OpeningBookV2.ob</c>): a flat file
/// of 256-byte blocks with no compression. Byte layout decoded empirically
/// against XG's own tooltip rendering of book entries (the display oracle);
/// see the subproject INSTRUCTIONS for the field map and its verification.
///
/// <para>
/// Block 0 is the header (magic <c>"OBDB"</c> at offset 4, format version,
/// creation date, a version text, and a length-prefixed UTF-16 title).
/// Every subsequent block starts with an int32 kind: 1 = description
/// continuation (80 UTF-16 chars of a long description, terminated by the
/// first NUL across the concatenation), 2 = entry. Unknown kinds are
/// skipped. Blocks are memory-dumped fixed records — bytes beyond a
/// block's live fields are stale heap garbage, so parsing must never read
/// past the documented field extents.
/// </para>
/// </summary>
internal static class OpeningBookParser
{
    internal const int BlockSize = 256;

    private const int KindHeader = 0;
    private const int KindDescription = 1;
    private const int KindEntry = 2;

    // "OBDB" at header offset 4.
    private static ReadOnlySpan<byte> Magic => "OBDB"u8;

    /// <summary>
    /// Parses a complete opening-book image. Throws
    /// <see cref="InvalidDataException"/> when the image is not a whole
    /// number of blocks, is empty, or does not start with a valid header
    /// block.
    /// </summary>
    public static OpeningBookDocument Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < BlockSize || data.Length % BlockSize != 0)
            throw new InvalidDataException(
                $"Opening book image length {data.Length} is not a positive multiple of {BlockSize} bytes.");
        if (BinaryPrimitives.ReadInt32LittleEndian(data) != KindHeader ||
            !data.Slice(4, 4).SequenceEqual(Magic))
            throw new InvalidDataException(
                "Opening book header not recognised: expected block kind 0 with magic \"OBDB\" at offset 4.");

        ReadOnlySpan<byte> header = data[..BlockSize];
        int formatVersion = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
        DateTime createdOn = PascalBinaryReader.FromTDateTime(
            BinaryPrimitives.ReadDoubleLittleEndian(header[24..]));
        string versionText = ReadShortAnsiString(header[32..], maxLen: 8);
        string title = ReadBytePrefixedUtf16(header[41..]);

        var descriptionParts = new StringBuilder();
        var entries = new List<OpeningBookEntry>();
        for (int offset = BlockSize; offset < data.Length; offset += BlockSize)
        {
            ReadOnlySpan<byte> block = data.Slice(offset, BlockSize);
            switch (BinaryPrimitives.ReadInt32LittleEndian(block))
            {
                case KindDescription:
                    // 80 UTF-16 chars at +4; the rest of the block is garbage.
                    descriptionParts.Append(Encoding.Unicode.GetString(block.Slice(4, 160)));
                    break;
                case KindEntry:
                    entries.Add(ParseEntry(block));
                    break;
                // Unknown kinds (including a stray header) are skipped:
                // forward compatibility over failing an otherwise valid file.
            }
        }

        string description = descriptionParts.ToString();
        int nul = description.IndexOf('\0', StringComparison.Ordinal);
        if (nul >= 0)
            description = description[..nul];

        return new OpeningBookDocument
        {
            FormatVersion = formatVersion,
            CreatedOn = createdOn,
            VersionText = versionText,
            Title = title,
            Description = description,
            Entries = entries,
        };
    }

    /// <summary>
    /// Decodes one kind-2 block into an <see cref="OpeningBookEntry"/>.
    /// Offsets are relative to the block start; all integers little-endian.
    /// </summary>
    private static OpeningBookEntry ParseEntry(ReadOnlySpan<byte> block)
    {
        // +4: contributor, array[0..31] of WideChar, NUL-terminated.
        string contributor = PascalBinaryReader.DecodeWideCharArray(
            block.Slice(4, 64).ToArray(), elementCount: 32);

        // +68: the keyed position, 26 signed bytes (on-roll-after-the-play
        // perspective).
        var points = new sbyte[26];
        for (int i = 0; i < 26; i++)
            points[i] = unchecked((sbyte)block[68 + i]);

        // +96: context block — XG's canonical position context: cube value,
        // cube owner, away pair (−1/−1 = money), Jacoby, Beaver, Crawford.
        int cubeValue = ReadInt32(block, 96);
        int cubeOwnerSign = ReadInt32(block, 100);
        int onRollAway = ReadInt32(block, 104);
        int opponentAway = ReadInt32(block, 108);
        bool jacoby = ReadInt32(block, 112) != 0;
        bool beaver = ReadInt32(block, 116) != 0;
        bool crawford = ReadInt32(block, 120) != 0;

        // +124: seven singles in EvalResult order
        // [loseBG, loseG, loseS, winS, winG, winBG, equity].
        var evaluation = new EvalResult
        {
            LoseBackgammon = ReadSingle(block, 124),
            LoseGammon     = ReadSingle(block, 128),
            LoseSingle     = ReadSingle(block, 132),
            WinSingle      = ReadSingle(block, 136),
            WinGammon      = ReadSingle(block, 140),
            WinBackgammon  = ReadSingle(block, 144),
            Equity         = ReadSingle(block, 148),
        };

        // +152: entry level; +160/+164: engine version pair; +168: trials;
        // +172: per-game equity std dev; +176/+180: rollout sub-levels;
        // +188: dice seed; +196: duration seconds; +200/+208: TDateTimes.
        // (+156 is always zero; +184 and +192 hold small unidentified values
        // on ~2% of rollout entries and are deliberately not surfaced.)
        return new OpeningBookEntry
        {
            Contributor = contributor,
            Position = new PositionEngine { Points = points },
            CubeValue = cubeValue,
            CubeOwnerSign = cubeOwnerSign,
            OnRollAway = onRollAway,
            OpponentAway = opponentAway,
            Jacoby = jacoby,
            Beaver = beaver,
            Crawford = crawford,
            Evaluation = evaluation,
            Level = ReadInt32(block, 152),
            EngineVersionMajor = ReadInt32(block, 160),
            EngineVersionMinor = ReadInt32(block, 164),
            Trials = ReadInt32(block, 168),
            EquityStandardDeviation = ReadSingle(block, 172),
            RolloutMovesLevel = ReadInt32(block, 176),
            RolloutCubeLevel = ReadInt32(block, 180),
            Seed = ReadInt32(block, 188),
            Duration = TimeSpan.FromSeconds(ReadSingle(block, 196)),
            AddedOn = PascalBinaryReader.FromTDateTime(ReadDouble(block, 200)),
            AnalyzedOn = PascalBinaryReader.FromTDateTime(ReadDouble(block, 208)),
        };
    }

    private static int ReadInt32(ReadOnlySpan<byte> block, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(block[offset..]);

    private static float ReadSingle(ReadOnlySpan<byte> block, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(block[offset..]);

    private static double ReadDouble(ReadOnlySpan<byte> block, int offset) =>
        BinaryPrimitives.ReadDoubleLittleEndian(block[offset..]);

    /// <summary>
    /// Reads a Pascal ShortString (1-byte length prefix, ANSI body) from the
    /// start of <paramref name="span"/>, clamping the length to
    /// <paramref name="maxLen"/>.
    /// </summary>
    private static string ReadShortAnsiString(ReadOnlySpan<byte> span, int maxLen)
    {
        int len = Math.Min(span[0], maxLen);
        return Encoding.Latin1.GetString(span.Slice(1, len));
    }

    /// <summary>
    /// Reads the header's title: a 1-byte character-count prefix followed by
    /// UTF-16LE text. The count is clamped to the bytes remaining in the
    /// block.
    /// </summary>
    private static string ReadBytePrefixedUtf16(ReadOnlySpan<byte> span)
    {
        int len = Math.Min(span[0], (span.Length - 1) / 2);
        return Encoding.Unicode.GetString(span.Slice(1, len * 2));
    }
}

/// <summary>
/// The parse result of an opening-book image: header metadata plus the
/// entries in file order (file order is meaningful — the selection policy's
/// final tiebreak treats later entries as overriding earlier ones, matching
/// the book's import-append history).
/// </summary>
internal sealed class OpeningBookDocument
{
    /// <summary>Header format-version int (observed: 1).</summary>
    public int FormatVersion { get; init; }

    /// <summary>Header creation TDateTime.</summary>
    public DateTime CreatedOn { get; init; }

    /// <summary>Header ShortString version text (observed: "3.70").</summary>
    public string VersionText { get; init; } = string.Empty;

    /// <summary>Header title (length-prefixed UTF-16).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Long description assembled from the kind-1 blocks.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>All entries, in file order.</summary>
    public List<OpeningBookEntry> Entries { get; init; } = [];
}
