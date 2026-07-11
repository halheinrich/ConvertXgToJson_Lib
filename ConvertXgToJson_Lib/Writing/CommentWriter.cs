using System.Text;

namespace ConvertXgToJson_Lib.Writing;

/// <summary>
/// Serializes the comment table into temp.xgc bytes — the mirror of
/// <see cref="Parsing.CommentParser"/>. One comment per CRLF-terminated
/// line; embedded CRLFs inside a comment are escaped as the two-byte
/// sequence #1#2 (0x01 0x02) per the XG spec.
/// </summary>
internal static class CommentWriter
{
    internal static byte[] WriteAll(IReadOnlyList<string> comments)
    {
        var sb = new StringBuilder();
        foreach (string comment in comments)
        {
            sb.Append(comment.Replace("\r\n", "\x01\x02"));
            sb.Append("\r\n");
        }
        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}
