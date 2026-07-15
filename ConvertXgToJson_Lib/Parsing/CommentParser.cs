namespace ConvertXgToJson_Lib.Parsing;

/// <summary>
/// Parses temp.xgc – a plain text file where each record's comment is one line,
/// separated by CRLF.  The spec says that embedded CRLFs inside a comment are
/// stored as the two-byte sequence #1#2 (bytes 0x01 0x02), which must be
/// replaced with real CRLF (0x0D 0x0A) after reading.
/// </summary>
internal static class CommentParser
{
    public static List<string> ReadAll(Stream stream)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.Latin1, leaveOpen: true);
        string raw = reader.ReadToEnd();

        // Split on CRLF line separators
        string[] lines = raw.Split("\r\n", StringSplitOptions.None);

        // The final CRLF yields one empty trailing segment — a split
        // artifact, not a comment. An *interior* empty segment is a real
        // (empty) comment: dropping one would shift every later entry and
        // desync the records' CommentIndex references.
        int count = lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;

        // Replace the embedded CRLF escape (#1#2 = 0x01 0x02) with real CRLF
        var result = new List<string>(count);
        for (int i = 0; i < count; i++)
            result.Add(lines[i].Replace("\x01\x02", "\r\n"));
        return result;
    }
}
