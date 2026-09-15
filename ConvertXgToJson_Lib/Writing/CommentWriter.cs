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

    /// <exception cref="XgUnrepresentableValueException">
    /// A comment cannot be carried faithfully; the exception carries its
    /// index, and the offending character when there is one.
    /// </exception>
    internal static byte[] WriteAll(IReadOnlyList<string> comments)
    {
        using var output = new MemoryStream();
        for (int i = 0; i < comments.Count; i++)
        {
            string comment = comments[i];
            if (comment.Contains(CrlfEscape, StringComparison.Ordinal))
                throw new XgUnrepresentableValueException(
                    $"Comment {i} contains the character pair U+0001 U+0002, the comment table's escape for an " +
                    "embedded CRLF; it would read back as a CRLF.",
                    commentIndex: i, character: null);

            try
            {
                output.Write(Wire.GetBytes(comment.Replace(Crlf, CrlfEscape) + Crlf));
            }
            catch (EncoderFallbackException ex)
            {
                Rune? character = Unencodable(ex);
                string what = character is { } c
                    ? $"U+{c.Value:X4}"
                    : $"the unpaired surrogate U+{(int)ex.CharUnknown:X4}";
                throw new XgUnrepresentableValueException(
                    $"Comment {i} contains {what}, which the comment table's encoding ({Wire.WebName}) " +
                    "cannot represent; it would be written as '?'.",
                    commentIndex: i, character: character, innerException: ex);
            }
        }
        return output.ToArray();
    }

    /// <summary>
    /// The character the fallback refused, or null for an unpaired
    /// surrogate — a lone UTF-16 code unit, which no <see cref="Rune"/> holds.
    /// </summary>
    private static Rune? Unencodable(EncoderFallbackException ex) =>
        ex.IsUnknownSurrogate()
            ? new Rune(ex.CharUnknownHigh, ex.CharUnknownLow)
            : Rune.TryCreate(ex.CharUnknown, out Rune rune) ? rune : null;
}
