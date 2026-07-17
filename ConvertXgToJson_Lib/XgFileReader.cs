using System.Text.Json;
using ConvertXgToJson_Lib.Json;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;

namespace ConvertXgToJson_Lib;

/// <summary>
/// The primary entry point for reading XG backgammon files.
///
/// Usage:
///   var xgFile = XgFileReader.ReadFile("mymatch.xg");
///   string json = XgFileReader.ToJson(xgFile);
/// </summary>
public static class XgFileReader
{
    // ------------------------------------------------------------------ //
    //  XG-format file discovery
    // ------------------------------------------------------------------ //

    /// <summary>
    /// The file extensions this library recognizes as XG-format input:
    /// <c>.xg</c> (match files) and <c>.xgp</c> (position files), in that
    /// order. Single source of truth for <see cref="IsXgFormatFile"/> and
    /// both <c>EnumerateXgFormatFiles</c> overloads — the order here is the
    /// enumeration order the single-argument overload observes (the
    /// <see cref="SearchOption"/> overload sorts by full path instead).
    /// </summary>
    public static IReadOnlyList<string> XgFormatExtensions { get; } = [".xg", ".xgp"];

    /// <summary>
    /// Returns <c>true</c> when <paramref name="path"/>'s extension is an
    /// XG-format extension (<c>.xg</c> or <c>.xgp</c>), matched
    /// case-insensitively against <see cref="XgFormatExtensions"/>. Pure path
    /// inspection: the file need not exist and its contents are never read.
    /// </summary>
    /// <param name="path">A file path or name; only its extension is examined.</param>
    public static bool IsXgFormatFile(string path)
    {
        string ext = Path.GetExtension(path);
        foreach (string formatExt in XgFormatExtensions)
            if (string.Equals(ext, formatExt, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Enumerates every XG-format file in <paramref name="directory"/>: both
    /// <c>*.xg</c> match files and <c>*.xgp</c> position files. Order is all
    /// <c>.xg</c> files first, then all <c>.xgp</c> files; within each
    /// extension, filesystem order (non-deterministic per OS) — callers that
    /// need a deterministic order use the
    /// <see cref="EnumerateXgFormatFiles(string, SearchOption)"/> overload,
    /// whose sorted contract deliberately differs from this historical
    /// extension-major order. Implemented as
    /// one <see cref="Directory.EnumerateFiles(string, string)"/> pass per
    /// entry in <see cref="XgFormatExtensions"/> rather than a single
    /// <c>*.xg*</c> glob — the broader pattern would also match hypothetical
    /// <c>.xgz</c> / <c>.xgr</c> files, which are not assumed to be XG-format.
    /// </summary>
    /// <param name="directory">Directory to scan for .xg and/or .xgp files.</param>
    public static IEnumerable<string> EnumerateXgFormatFiles(string directory)
    {
        foreach (string ext in XgFormatExtensions)
            foreach (string path in Directory.EnumerateFiles(directory, "*" + ext))
                yield return path;
    }

    /// <summary>
    /// Enumerates every XG-format file in <paramref name="directory"/> —
    /// <c>*.xg</c> and <c>*.xgp</c>, matched case-insensitively via
    /// <see cref="IsXgFormatFile"/> — descending into subdirectories when
    /// <paramref name="searchOption"/> is
    /// <see cref="SearchOption.AllDirectories"/>.
    ///
    /// <para><b>Enumeration order is deterministic and part of the
    /// contract:</b> ascending full path, compared with
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> plus a
    /// <see cref="StringComparer.Ordinal"/> tiebreak for paths differing
    /// only by case (possible on case-sensitive filesystems). The order is
    /// independent of the filesystem's directory-walk order and of the
    /// current culture, so consumers may pin user-visible sequencing (e.g.
    /// export numbering) to it. Extensions interleave by path — this
    /// deliberately differs from the single-argument overload's historical
    /// extension-major order.</para>
    ///
    /// <para>Sorting materializes the matching paths on first enumeration;
    /// like the single-argument overload, directory-access errors are
    /// deferred to that point.</para>
    /// </summary>
    /// <param name="directory">Directory to scan for .xg and/or .xgp files.</param>
    /// <param name="searchOption">
    /// <see cref="SearchOption.AllDirectories"/> to include all
    /// subdirectories; <see cref="SearchOption.TopDirectoryOnly"/> for the
    /// top directory alone.
    /// </param>
    public static IEnumerable<string> EnumerateXgFormatFiles(string directory, SearchOption searchOption)
    {
        foreach (string path in Directory.EnumerateFiles(directory, "*", searchOption)
                     .Where(IsXgFormatFile)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(p => p, StringComparer.Ordinal))
        {
            yield return path;
        }
    }

    // ------------------------------------------------------------------ //
    //  Public API
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Reads a .XG file from disk and parses all sections into a structured
    /// <see cref="XgFile"/> object.
    /// </summary>
    /// <param name="path">Full path to the .XG file.</param>
    public static XgFile ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadStream(stream);
    }

    /// <summary>
    /// Reads a .XG file from an open <see cref="Stream"/>.
    /// The stream must be positioned at the very beginning of the file.
    /// </summary>
    public static XgFile ReadStream(Stream stream)
    {
        // Step 1: Strip the RichGameFormat outer header
        var (header, contentOffset) = RichGameHeaderParser.Read(stream);

        // Seek to the start of the compressed payload
        stream.Position = contentOffset;

        // Step 2: Decompress the payload into the four sub-streams
        using var decompressed = XgDecompressor.Decompress(stream);
        // Step 3: Parse each sub-stream
        var records  = SaveRecordParser.ReadAll(decompressed.GameRecords);
        var rollouts = RolloutContextParser.ReadAll(decompressed.RolloutContexts);
        var comments = CommentParser.ReadAll(decompressed.Comments);

        return new XgFile
        {
            Header   = header,
            Records  = records,
            Rollouts = rollouts,
            Comments = comments,
        };
    }

    /// <summary>
    /// Serializes an <see cref="XgFile"/> to a JSON string using the
    /// built-in System.Text.Json serializer with XG-appropriate options.
    /// </summary>
    public static string ToJson(XgFile file, JsonSerializerOptions? options = null)
        => JsonSerializer.Serialize(file, options ?? XgJsonOptions.Default);

    /// <summary>
    /// Writes the JSON representation of an <see cref="XgFile"/> to a file.
    /// </summary>
    public static async Task WriteJsonAsync(
        XgFile file,
        string outputPath,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var fs = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(fs, file, options ?? XgJsonOptions.Default, cancellationToken);
    }

    public static XgFile ReadJson(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<XgFile>(json, XgJsonOptions.Default)
               ?? throw new InvalidDataException($"Failed to deserialise XgFile from {path}");
    }
    /// <summary>
    /// Reads only the match header from a .xg file without fully parsing
    /// the file. Use when only player names and match length are needed.
    /// Significantly faster than <see cref="ReadFile"/> for corpus-wide scans.
    /// </summary>
    /// <param name="path">Full path to the .xg file.</param>
    /// <returns>
    /// An <see cref="XgMatchInfo"/> populated from the first
    /// <see cref="MatchHeaderRecord"/>, or <c>null</c> if the file's first
    /// zlib stream is unreadable, undersized, or does not begin with a
    /// match header record. Callers that previously relied on a
    /// default-constructed return (empty strings, <c>MatchLength = 0</c>)
    /// should treat <c>null</c> as "skip this file" rather than as a
    /// zero-length money match.
    /// </returns>
    public static XgMatchInfo? ReadMatchInfo(string path)
    {
        // The MatchHeaderRecord is always the first record of the first stream.
        byte[]? firstStream = ReadFirstDecompressedStream(path);
        if (firstStream == null || firstStream.Length < SaveRecordParser.RecordSize)
            return null;

        // The first record must be RecordType.HeaderMatch (0).
        // Byte 8 of the record is EntryType.
        if (firstStream[8] != (byte)RecordType.HeaderMatch)
            return null;

        return XgMatchInfo.From(SaveRecordParser.ReadMatchHeaderRecord(firstStream));
    }

    /// <summary>
    /// Opens <paramref name="path"/>, strips the RichGameFormat outer header,
    /// and decompresses only the first zlib stream — the xg game-records
    /// sub-stream that begins with the match header. Returns <c>null</c> when
    /// that stream is unreadable.
    /// </summary>
    private static byte[]? ReadFirstDecompressedStream(string path)
    {
        using var stream = File.OpenRead(path);
        var (_, contentOffset) = RichGameHeaderParser.Read(stream);
        stream.Position = contentOffset;
        return XgDecompressor.DecompressFirstStream(XgDecompressor.ReadAllBytes(stream));
    }

    /// <summary>
    /// Streaming overload of <see cref="ReadGameHeaders(string)"/>.
    /// Yields one <see cref="XgGameInfo"/> per game in the file, populating
    /// <see cref="XgIteratorState.MatchInfo"/> before the first yield.
    /// To stop iteration early, the caller breaks out of the consuming
    /// <c>foreach</c> — disposing the enumerator stops further yields.
    /// </summary>
    /// <param name="path">Full path to the .xg file.</param>
    /// <param name="state">
    /// Iterator state. <see cref="XgIteratorState.MatchInfo"/> is reset to null
    /// at the start of each file and populated before the first yield.
    /// </param>
    public static IEnumerable<XgGameInfo> ReadGameHeaders(string path, XgIteratorState state)
    {
        state.MatchInfo = null;

        byte[]? data = ReadFirstDecompressedStream(path);
        if (data == null || data.Length < SaveRecordParser.RecordSize)
            yield break;

        // Parse MatchInfo from the first record if it is a MatchHeaderRecord.
        int matchLength = 0;
        if (data[8] == (byte)RecordType.HeaderMatch)
        {
            state.MatchInfo = XgMatchInfo.From(SaveRecordParser.ReadMatchHeaderRecord(data));
            matchLength = state.MatchInfo.MatchLength;
        }

        int stride = SaveRecordParser.RecordSize;

        for (int offset = 0; offset + stride <= data.Length; offset += stride)
        {
            var entryType = (RecordType)data[offset + 8];

            if (entryType == RecordType.FooterMatch)
                yield break;

            if (entryType != RecordType.HeaderGame)
                continue;

            state.GameInfo = null;
            var gameInfo = XgGameInfo.From(SaveRecordParser.ReadGameHeaderRecord(data, offset), matchLength);
            state.GameInfo = gameInfo;
            yield return gameInfo;
        }
    }
}
