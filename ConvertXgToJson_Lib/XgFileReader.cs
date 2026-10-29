using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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
    /// <see cref="XgFile"/> object. A decode primitive: a file that cannot be
    /// opened or decoded throws. For a read that returns a failed source
    /// instead, use <see cref="XgSourceReader.ReadFile"/>.
    /// </summary>
    /// <param name="path">Full path to the .XG file.</param>
    /// <exception cref="InvalidDataException">
    /// As <see cref="ReadStream"/>: the file's payload is unreadable.
    /// </exception>
    public static XgFile ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadStream(stream);
    }

    /// <summary>
    /// Reads a .XG file from an open <see cref="Stream"/>.
    /// The stream must be positioned at the very beginning of the file.
    /// The binary decoder: <see cref="ReadFile"/> and
    /// <see cref="XgSourceReader"/> decode through it. The stream is
    /// borrowed — sought to the payload, read, and left open.
    /// </summary>
    /// <remarks>
    /// The payload after the outer header is a container read through its
    /// directory when that directory validates, each of the four inner files
    /// this library reads (temp.xg, temp.xgi, temp.xgr, temp.xgc) that it
    /// names extracted and checked against its manifest entry; an entry
    /// naming none of them passes the directory's checks and is set aside,
    /// never extracted (halheinrich/backgammon#419). Otherwise it is the
    /// legacy format when one zlib stream fills it exactly. A payload that
    /// is neither is refused, never guessed at: a multi-stream payload whose
    /// directory is absent or fails a check is not read.
    /// </remarks>
    /// <exception cref="InvalidDataException">
    /// The payload is not one zlib stream and its container directory cannot
    /// be used, or an inner file contradicts its manifest entry; the message
    /// names the check that failed.
    /// </exception>
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
        => JsonSerializer.Serialize(file, TypeInfo(options));

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
        await JsonSerializer.SerializeAsync(fs, file, TypeInfo(options), cancellationToken);
    }

    /// <summary>
    /// Reads an <see cref="XgFile"/> back from a JSON export produced by
    /// <see cref="ToJson"/> / <see cref="WriteJsonAsync"/> — the read half of
    /// the JSON round-trip. Load-bearing: XgFilter_Lib's JSON directory walk
    /// parses every export through it, and <see cref="XgSourceReader"/>
    /// decodes <see cref="XgSourceFormat.Json"/> through the same decoder.
    /// For a read that returns a failed source instead of throwing, use
    /// <see cref="XgSourceReader.ReadFile"/>.
    /// </summary>
    /// <param name="path">Full path to the .json export.</param>
    /// <exception cref="InvalidDataException">
    /// Thrown when the file deserializes to <see langword="null"/> (e.g. the
    /// literal JSON <c>null</c>); malformed JSON throws
    /// <see cref="JsonException"/> from the serializer instead.
    /// </exception>
    public static XgFile ReadJson(string path)
    {
        using var stream = File.OpenRead(path);
        return DecodeJson(stream, path);
    }

    /// <summary>
    /// The JSON decoder: reads <paramref name="stream"/> to its end as text —
    /// UTF-8 unless a byte-order mark says otherwise, as
    /// <see cref="File.ReadAllText(string)"/> reads a file — and deserializes
    /// the document. The stream is borrowed and left open.
    /// </summary>
    /// <param name="stream">The document, read from its current position.</param>
    /// <param name="sourceName">Names the source in the refusal's message.</param>
    /// <exception cref="InvalidDataException">The document is the literal <c>null</c>.</exception>
    /// <exception cref="JsonException">The document is not this library's JSON.</exception>
    internal static XgFile DecodeJson(Stream stream, string sourceName)
    {
        string json;
        using (var reader = new StreamReader(
                   stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
            json = reader.ReadToEnd();
        return JsonSerializer.Deserialize(json, TypeInfo(options: null))
               ?? throw new InvalidDataException($"Failed to deserialise XgFile from {sourceName}");
    }

    /// <summary>
    /// Resolves <see cref="XgFile"/>'s metadata from the options that will
    /// serialize it. Going through the resolver instead of the reflection
    /// overloads is what makes this library's JSON surface trim-safe
    /// (halheinrich/backgammon#129 leg 2), and it must hold for the
    /// published <c>options</c> parameter too, not just for the default.
    ///
    /// <para>
    /// A caller's options that carries no resolver of its own is given
    /// this library's (<see cref="XgJsonOptions.Resolver"/>). That is the
    /// like-for-like substitution, not a behaviour change: the reflection
    /// overload this replaces installed <c>DefaultJsonTypeInfoResolver</c>
    /// on the very same options object and froze it, and the two resolvers
    /// describe the <see cref="XgFile"/> closure identically — the
    /// invariant of the whole halheinrich/backgammon#129 arc. The caller's
    /// options keeps deciding everything it decided before (indentation,
    /// naming, converters); only the mechanism that supplies the metadata
    /// changes. A caller who brings a resolver keeps it untouched.
    /// </para>
    /// </summary>
    private static JsonTypeInfo<XgFile> TypeInfo(JsonSerializerOptions? options)
    {
        var effective = options ?? XgJsonOptions.Default;
        effective.TypeInfoResolver ??= XgJsonOptions.Resolver;
        return (JsonTypeInfo<XgFile>)effective.GetTypeInfo(typeof(XgFile));
    }

    /// <summary>
    /// Reads only the match header from a .xg file without fully parsing
    /// the file. Use when only player names and match length are needed.
    /// Significantly faster than <see cref="ReadFile"/> for corpus-wide scans.
    /// </summary>
    /// <remarks>
    /// The payload is opened by the same rule and directory validation as
    /// <see cref="ReadStream"/>, and temp.xg — the game records, selected by
    /// its manifest name — is the only inner file extracted. A result
    /// therefore says nothing of the inner files this read never extracts
    /// (the index, rollouts and comments): <see cref="ReadFile"/> can still
    /// refuse a file whose match info reads here.
    /// </remarks>
    /// <param name="path">Full path to the .xg file.</param>
    /// <returns>
    /// An <see cref="XgMatchInfo"/> populated from the first
    /// <see cref="MatchHeaderRecord"/>, or <c>null</c> if temp.xg is shorter
    /// than one record or does not begin with a match header record. Callers
    /// that previously relied on a default-constructed return (empty strings,
    /// <c>MatchLength = 0</c>) should treat <c>null</c> as "skip this file"
    /// rather than as a zero-length money match.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// The payload is not one zlib stream and its container directory cannot
    /// be used, or temp.xg contradicts its manifest entry — the same refusal,
    /// with the same message, that <see cref="ReadStream"/> throws for it.
    /// </exception>
    public static XgMatchInfo? ReadMatchInfo(string path)
    {
        // The MatchHeaderRecord is always the first record of temp.xg.
        byte[] gameRecords = ReadGameRecords(path);
        if (gameRecords.Length < SaveRecordParser.RecordSize)
            return null;

        // The first record must be RecordType.HeaderMatch (0).
        // Byte 8 of the record is EntryType.
        if (gameRecords[8] != (byte)RecordType.HeaderMatch)
            return null;

        return XgMatchInfo.From(SaveRecordParser.ReadMatchHeaderRecord(gameRecords));
    }

    /// <summary>
    /// Opens <paramref name="path"/>, strips the RichGameFormat outer header,
    /// and extracts temp.xg alone through <see cref="XgDecompressor"/> — the
    /// game records that begin with the match header.
    /// </summary>
    /// <exception cref="InvalidDataException">The payload or temp.xg is refused.</exception>
    private static byte[] ReadGameRecords(string path)
    {
        using var stream = File.OpenRead(path);
        var (_, contentOffset) = RichGameHeaderParser.Read(stream);
        stream.Position = contentOffset;
        return XgDecompressor.DecompressGameRecords(stream);
    }

    /// <summary>
    /// Fast path over temp.xg only.
    /// Yields one <see cref="XgGameInfo"/> per game in the file, populating
    /// <see cref="XgIteratorState.MatchInfo"/> before the first yield.
    /// To stop iteration early, the caller breaks out of the consuming
    /// <c>foreach</c> — disposing the enumerator stops further yields.
    /// </summary>
    /// <remarks>
    /// The payload is opened by the same rule and directory validation as
    /// <see cref="ReadStream"/>, and temp.xg is the only inner file
    /// extracted, so a full enumeration says nothing of the inner files it
    /// never extracts: <see cref="ReadFile"/> can still refuse the file.
    /// Like every failure here, the refusal is deferred: it is thrown by the
    /// enumeration's first <c>MoveNext</c>, not by this call.
    /// </remarks>
    /// <param name="path">Full path to the .xg file.</param>
    /// <param name="state">
    /// Iterator state. <see cref="XgIteratorState.MatchInfo"/> is reset to null
    /// at the start of each file and populated before the first yield.
    /// </param>
    /// <returns>
    /// The games' headers in file order; none when temp.xg is shorter than
    /// one record.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown on enumeration when the payload is not one zlib stream and its
    /// container directory cannot be used, or temp.xg contradicts its
    /// manifest entry — the same refusal, with the same message, that
    /// <see cref="ReadStream"/> throws for it; or when temp.xg does not
    /// begin with a match header.
    /// </exception>
    public static IEnumerable<XgGameInfo> ReadGameHeaders(string path, XgIteratorState state)
    {
        state.MatchInfo = null;

        byte[] data = ReadGameRecords(path);
        if (data.Length < SaveRecordParser.RecordSize)
            yield break;

        // The match header is the first record; each game's standing is read
        // against its terms, so a file without one has no game to read.
        if (data[8] != (byte)RecordType.HeaderMatch)
            throw new InvalidDataException(
                $"XG file '{path}' does not begin with a match header — cannot read its games' standings.");
        var matchInfo = XgMatchInfo.From(SaveRecordParser.ReadMatchHeaderRecord(data));
        state.MatchInfo = matchInfo;

        int stride = SaveRecordParser.RecordSize;

        for (int offset = 0; offset + stride <= data.Length; offset += stride)
        {
            var entryType = (RecordType)data[offset + 8];

            if (entryType == RecordType.FooterMatch)
                yield break;

            if (entryType != RecordType.HeaderGame)
                continue;

            state.GameInfo = null;
            var gameInfo = XgGameInfo.From(SaveRecordParser.ReadGameHeaderRecord(data, offset), matchInfo.Terms);
            state.GameInfo = gameInfo;
            yield return gameInfo;
        }
    }
}
