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
///
/// Or in a single step:
///   string json = XgFileReader.ReadFileAsJson("mymatch.xg");
/// </summary>
public static class XgFileReader
{
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
    /// Convenience method: reads a .XG file from disk and returns its JSON
    /// representation in a single call.
    /// </summary>
    public static string ReadFileAsJson(string path, JsonSerializerOptions? options = null)
        => ToJson(ReadFile(path), options);

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

    /// <summary>
    /// Reads a .XG file from disk and writes its JSON representation directly
    /// to <paramref name="outputPath"/> without buffering the entire JSON string
    /// in memory – preferred for large files.
    /// </summary>
    public static async Task ReadFileToJsonFileAsync(
        string inputPath,
        string outputPath,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var xgFile = ReadFile(inputPath);
        await WriteJsonAsync(xgFile, outputPath, options, cancellationToken);
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
        using var stream = File.OpenRead(path);

        // Strip the RichGameFormat outer header and seek to compressed payload.
        var (_, contentOffset) = RichGameHeaderParser.Read(stream);
        stream.Position = contentOffset;

        // Read only the first zlib stream (the xg game-records sub-stream).
        // The MatchHeaderRecord is always the first record in that stream.
        byte[] raw = ReadAllCompressedBytes(stream);
        byte[]? firstStream = XgDecompressor.DecompressFirstStream(raw);
        if (firstStream == null || firstStream.Length < SaveRecordParser.RecordSize)
            return null;

        // The first record must be RecordType.HeaderMatch (0).
        // Byte 8 of the record is EntryType.
        if (firstStream[8] != (byte)RecordType.HeaderMatch)
            return null;

        return XgMatchInfo.From(SaveRecordParser.ReadMatchHeaderRecord(firstStream));
    }

    private static byte[] ReadAllCompressedBytes(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
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

        using var stream = File.OpenRead(path);

        var (_, contentOffset) = RichGameHeaderParser.Read(stream);
        stream.Position = contentOffset;

        byte[] raw = ReadAllCompressedBytes(stream);
        byte[]? data = XgDecompressor.DecompressFirstStream(raw);
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
