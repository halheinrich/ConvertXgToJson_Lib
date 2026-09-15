using System.Diagnostics;
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
    /// A comment cannot be carried faithfully; the exception carries the
    /// reason, the comment's index, and the offending character when there
    /// is one.
    /// </exception>
    internal static byte[] WriteAll(IReadOnlyList<string> comments)
    {
        using var output = new MemoryStream();
        for (int i = 0; i < comments.Count; i++)
        {
            string comment = comments[i];
            if (comment.Contains(CrlfEscape, StringComparison.Ordinal))
                throw Refusal(i, XgUnrepresentableValueReason.ReservedCrlfEscape, CodeUnits(CrlfEscape));

            try
            {
                output.Write(Wire.GetBytes(comment.Replace(Crlf, CrlfEscape) + Crlf));
            }
            catch (EncoderFallbackException ex)
            {
                throw Unencodable(i, ex);
            }
        }
        return output.ToArray();
    }

    /// <summary>
    /// The refusal for what the strict encoding would not encode: a
    /// character (a surrogate pair, or a lone code unit that is one), or an
    /// unpaired surrogate — a lone UTF-16 code unit no <see cref="Rune"/>
    /// holds, because it is not a character.
    /// </summary>
    private static XgUnrepresentableValueException Unencodable(int index, EncoderFallbackException ex)
    {
        Rune? character = ex.IsUnknownSurrogate()
            ? new Rune(ex.CharUnknownHigh, ex.CharUnknownLow)
            : Rune.TryCreate(ex.CharUnknown, out Rune rune) ? rune : null;
        return character is { } c
            ? Refusal(index, XgUnrepresentableValueReason.UnencodableCharacter, $"U+{c.Value:X4}", c, ex)
            : Refusal(index, XgUnrepresentableValueReason.UnpairedSurrogate, CodeUnits(ex.CharUnknown.ToString()),
                cause: ex);
    }

    /// <summary>
    /// The refusal of comment <paramref name="index"/>, its message composed
    /// from <paramref name="reason"/>; <paramref name="offender"/> spells
    /// what the comment holds.
    /// </summary>
    private static XgUnrepresentableValueException Refusal(
        int index, XgUnrepresentableValueReason reason, string offender,
        Rune? character = null, Exception? cause = null)
    {
        string holds = reason switch
        {
            XgUnrepresentableValueReason.UnencodableCharacter =>
                $"{offender}, which the comment table's encoding ({Wire.WebName}) cannot represent; " +
                "it would be written as '?'",
            XgUnrepresentableValueReason.UnpairedSurrogate =>
                $"the unpaired surrogate {offender}, which is not a character; the comment table's " +
                $"encoding ({Wire.WebName}) would write it as '?'",
            XgUnrepresentableValueReason.ReservedCrlfEscape =>
                $"the character pair {offender}, the comment table's escape for an embedded CRLF; " +
                "it would read back as a CRLF",
            _ => throw new UnreachableException($"No refusal is composed for {reason}."),
        };
        return new XgUnrepresentableValueException($"Comment {index} contains {holds}.", reason, index, character, cause);
    }

    /// <summary>Each UTF-16 code unit of <paramref name="text"/> as <c>U+XXXX</c>, space-separated.</summary>
    private static string CodeUnits(string text) => string.Join(' ', text.Select(unit => $"U+{(int)unit:X4}"));
}
