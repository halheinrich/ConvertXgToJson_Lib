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

        // Replace the embedded CRLF escape (#1#2 = 0x01 0x02) with real CRLF
        var result = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            if (line.Length == 0) continue;  // skip empty trailing line
            result.Add(line.Replace("\x01\x02", "\r\n"));
        }
        return result;
    }
}
