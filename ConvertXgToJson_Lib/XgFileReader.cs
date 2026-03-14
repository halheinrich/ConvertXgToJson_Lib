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
        //var rolloutStreamLen = decompressed.RolloutContexts.Length;
        //if (rolloutStreamLen != 0)
        //    rolloutStreamLen += 0;
        // set a breakpoint here or log it
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
    /// <see cref="MatchHeaderRecord"/>. Returns a default instance
    /// (empty strings, MatchLength = 0) if no match header is found.
    /// </returns>
    public static XgMatchInfo ReadMatchInfo(string path)
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
            return new XgMatchInfo();

        // The first record must be RecordType.HeaderMatch (0).
        // Byte 8 of the record is EntryType.
        if (firstStream[8] != (byte)RecordType.HeaderMatch)
            return new XgMatchInfo();

        return ParseMatchInfoFromRecord(firstStream);
    }

    private static byte[] ReadAllCompressedBytes(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static XgMatchInfo ParseMatchInfoFromRecord(byte[] record)
    {
        using var ms = new MemoryStream(record);
        using var sub = new SubStream(ms, 0, record.Length, leaveOpen: true);
        using var r = new PascalBinaryReader(sub);

        // Skip preamble: Previous(4) + Next(4) + EntryType(1) = 9 bytes
        r.ReadDword();
        r.ReadDword();
        r.ReadByte();

        // Player1Ansi: string[40] = 41 bytes (offset 9)
        _ = r.ReadPascalAnsiString(40);  // offset 50
                                         // Player2Ansi: string[40] = 41 bytes (offset 50)
        _ = r.ReadPascalAnsiString(40);  // offset 91

        // MatchLength: integer (4-byte align, offset 91 → pad 1 → 92)
        int matchLength = r.ReadInteger();  // offset 96

        // Skip everything up to unicode Player1 at offset 1142.
        // Player1 unicode is at offset 1142 (after Event unicode at 884).
        // Fastest: seek directly.
        sub.Position = 1142;

        // Player1: TShortUnicodeString = array[0..128] of WideChar = 258 bytes
        string player1 = r.ReadShortUnicodeString();  // offset 1400
        string player2 = r.ReadShortUnicodeString();  // offset 1658

        return new XgMatchInfo
        {
            Player1 = player1,
            Player2 = player2,
            MatchLength = matchLength >= 99999 ? 0 : matchLength,
        };
    }
}
