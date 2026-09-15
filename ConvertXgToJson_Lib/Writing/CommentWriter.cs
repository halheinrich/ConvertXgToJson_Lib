using System.Text;

namespace ConvertXgToJson_Lib.Writing;

/// <summary>
/// Serializes the comment table into temp.xgc bytes — the mirror of
/// <see cref="Parsing.CommentParser"/>. One comment per CRLF-terminated
/// line, encoded Latin-1; embedded CRLFs inside a comment are escaped as
/// the two-byte sequence #1#2 (0x01 0x02) per the XG spec.
/// </summary>
/// <remarks>
/// That wire cannot carry every string, and this writer is the one place
/// the constraint is checked: a comment holding a character the encoding
/// cannot represent would be written as <c>'?'</c>, and one holding the
/// escape pair itself would read back with a CRLF in its place. Either is
/// rejected rather than written lossily. Text read from a real file never
/// hits either case; text handed to <see cref="XgFileBuilder"/> or loaded
/// from JSON can.
/// </remarks>
internal static class CommentWriter
{
    private const string Crlf = "\r\n";
    private const string CrlfEscape = "\x01\x02";

    /// <summary>
    /// The table's encoding, strict: an unrepresentable character throws
    /// instead of degrading to <c>'?'</c>, so the encoding itself decides
    /// what the wire can carry.
    /// </summary>
    private static readonly Encoding Wire = Encoding.GetEncoding(
        Encoding.Latin1.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    /// <exception cref="ArgumentException">
    /// A comment cannot be carried faithfully; the message names its index
    /// and the reason.
    /// </exception>
    internal static byte[] WriteAll(IReadOnlyList<string> comments)
    {
        using var output = new MemoryStream();
        for (int i = 0; i < comments.Count; i++)
        {
            string comment = comments[i];
            if (comment.Contains(CrlfEscape, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"Comment {i} contains the character pair U+0001 U+0002, the comment table's escape for an " +
                    "embedded CRLF; it would read back as a CRLF.",
                    nameof(comments));

            try
            {
                output.Write(Wire.GetBytes(comment.Replace(Crlf, CrlfEscape) + Crlf));
            }
            catch (EncoderFallbackException ex)
            {
                throw new ArgumentException(
                    $"Comment {i} contains {Describe(ex)}, which the comment table's encoding " +
                    $"({Wire.WebName}) cannot represent; it would be written as '?'.",
                    nameof(comments), ex);
            }
        }
        return output.ToArray();
    }

    private static string Describe(EncoderFallbackException ex) =>
        ex.IsUnknownSurrogate()
            ? $"U+{char.ConvertToUtf32(ex.CharUnknownHigh, ex.CharUnknownLow):X4}"
            : $"U+{(int)ex.CharUnknown:X4}";
}
