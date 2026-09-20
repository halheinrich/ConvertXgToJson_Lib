using System.Text;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Single source of truth for the one format conversion this library owes a
/// consumer: XG stores a decision's comment as an RTF document, and this type
/// reduces that document to the text XG's own comment pane shows. It is a pure
/// string-to-string mapping with no dependency on the walk, the file, or the
/// container; <see cref="MatchContext.CommentAt"/> is its only caller, and
/// through it every emitted <c>DescriptiveData.Comment</c> is plain text.
///
/// <para>
/// The division is deliberate and is the point of the type: the comment
/// <i>table</i> stays raw everywhere it is stored or written —
/// <c>XgFile.Comments</c>, <see cref="Parsing.CommentParser"/>,
/// <see cref="Writing.CommentWriter"/> and the <c>.xgp</c> slice export all
/// copy it verbatim, because a round trip must reproduce XG's bytes. Only the
/// stamp path converts. This library is the one member that knows XG's comment
/// format, so the knowledge lives here rather than in a consumer: BgQuiz shows
/// the comment verbatim and must never inspect its format
/// (halheinrich/backgammon#233).
/// </para>
///
/// <para><b>The contract.</b> A comment that does not begin with the RTF
/// header <c>{\rtf</c> is plain text and passes through unchanged, byte for
/// byte — a note holding a stray backslash or brace is untouched. Otherwise
/// the document is read as follows.
/// <list type="bullet">
/// <item>Groups <c>{ … }</c> nest. A group contributes no text when it is a
/// destination that holds no body text: its first control word is one of the
/// <see cref="TextlessDestinations"/>, or the group opens with the control
/// symbol <c>\*</c> (an ignorable destination). Suppression is inherited by
/// nested groups.</item>
/// <item>A control word is a backslash, ASCII letters, an optional signed
/// decimal parameter, and one optional delimiting space, which is consumed and
/// is not text. <c>\par</c> and <c>\line</c> produce a line break;
/// <c>\tab</c> produces a tab; every other control word produces nothing —
/// formatting is dropped, never rendered.</item>
/// <item>Control symbols: <c>\\</c>, <c>\{</c>, <c>\}</c> produce the literal
/// character; <c>\~</c> a space; <c>\_</c> a hyphen; <c>\-</c> nothing;
/// <c>\'hh</c> the character whose byte is <c>hh</c> in the document's ANSI
/// code page. A backslash before a literal CR or LF is the RTF spec's spelling
/// of a paragraph break and produces a line break (the contract's list does not
/// name it; dropping it would silently join two paragraphs, which is the very
/// defect this type exists to fix, and XG never writes that form, so the rule
/// cannot misfire on XG's own output). Every other control symbol produces
/// nothing.</item>
/// <item><c>\uN</c> produces the UTF-16 code unit <c>N</c> (a negative
/// <c>N</c> means <c>N + 65536</c>), then skips the fallback that follows it:
/// the number of characters given by the most recent <c>\ucN</c> in scope, one
/// by default. A skipped character is a literal character or a <c>\'hh</c>
/// escape; a brace or another control word ends the skip early rather than
/// being swallowed, which keeps a malformed document from eating a
/// <c>\par</c>.</item>
/// <item>A raw CR or LF in the RTF source is not text.</item>
/// <item>The line break is CRLF — the same form a plain multi-line comment
/// already carries once <see cref="Parsing.CommentParser"/> reverses the
/// table's <c>#1#2</c> escape — so a consumer sees one line-break form
/// whatever the source.</item>
/// <item>Trailing line breaks and trailing whitespace are trimmed; interior
/// blank lines and runs of spaces are kept exactly, because the user lays
/// notes out with them.</item>
/// <item><b>Degrade, never block.</b> Malformed RTF — an unbalanced brace, a
/// truncated escape — yields the text extracted so far and never throws: one
/// bad comment must not end a corpus walk.</item>
/// </list>
/// </para>
///
/// <para><b>Stated limit: the code page.</b> <c>\'hh</c> is decoded as
/// Latin-1, except <c>0x80</c>–<c>0x9F</c>, which take the
/// <see cref="Windows1252Supplement"/> table — Windows-1252, which is what XG
/// declares (<c>\ansicpg1252</c>) and where it differs from Latin-1. Any other
/// declared code page is decoded the same way, and a declared
/// <c>\ansicpg</c> is otherwise ignored; the corpus does carry
/// <c>\ansicpg932</c> comments, whose text is ASCII, so the limit does not
/// bite there. Honouring an arbitrary code page would mean the
/// <c>System.Text.Encoding.CodePages</c> package, whose provider has to be
/// registered process-wide — a global side effect a library may not take on a
/// caller's behalf — and whose code-page tables would be carried into BgQuiz's
/// trimmed browser client for a case that has never occurred. The table below
/// is 32 characters.
/// </para>
/// </summary>
internal static class RtfPlainText
{
    /// <summary>The header every RTF document opens with.</summary>
    private const string RtfHeader = @"{\rtf";

    /// <summary>
    /// The line break both <c>\par</c> and <c>\line</c> produce — the form
    /// <see cref="Parsing.CommentParser"/> restores for a plain comment, so
    /// one break form reaches a consumer whatever the source.
    /// </summary>
    private const string LineBreak = "\r\n";

    /// <summary>
    /// Destinations whose body is not the document's text. A group whose
    /// first control word is one of these contributes nothing.
    /// </summary>
    private static readonly HashSet<string> TextlessDestinations =
    [
        "fonttbl", "colortbl", "stylesheet", "info",
        "pict", "object", "header", "footer",
    ];

    /// <summary>
    /// Windows-1252's <c>0x80</c>–<c>0x9F</c> block, the only range where it
    /// differs from Latin-1, indexed by <c>byte − 0x80</c>. Written as code-point
    /// escapes rather than as the characters themselves: several are invisible
    /// or look alike on the page (the five positions Windows-1252 leaves
    /// undefined — <c>0x81 0x8D 0x8F 0x90 0x9D</c>, which keep their Latin-1
    /// code point — and the four curved quotes), so only the escape form can be
    /// checked against the code-page chart by reading it.
    /// </summary>
    private const string Windows1252Supplement =
        "\u20AC\u0081\u201A\u0192\u201E\u2026\u2020\u2021" +  // 80-87  EUR . , f , ... dagger ddagger
        "\u02C6\u2030\u0160\u2039\u0152\u008D\u017D\u008F" +  // 88-8F  circumflex permille S< OE . Z .
        "\u0090\u2018\u2019\u201C\u201D\u2022\u2013\u2014" +  // 90-97  . quotes bullet en-dash em-dash
        "\u02DC\u2122\u0161\u203A\u0153\u009D\u017E\u0178";  // 98-9F  tilde TM s > oe . z Y-diaeresis

    /// <summary>
    /// The text a reader sees for <paramref name="source"/>: the source itself
    /// when it is not an RTF document, otherwise the document's body text per
    /// the contract on <see cref="RtfPlainText"/>. Never throws.
    /// </summary>
    internal static string Extract(string source)
    {
        if (!source.StartsWith(RtfHeader, StringComparison.Ordinal))
            return source;

        var text = new StringBuilder(source.Length);
        var enclosing = new Stack<GroupState>();
        var group = new GroupState(UnicodeFallbackSkip: 1, Suppressed: false);

        // Set by '{' and cleared by the next token: only a group's *first*
        // control word or symbol can name it a destination.
        bool atGroupStart = false;

        // Characters of a \uN fallback still owed to the skip.
        int fallbackSkip = 0;

        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];
            switch (c)
            {
                case '{':
                    enclosing.Push(group);
                    atGroupStart = true;
                    fallbackSkip = 0;
                    i++;
                    break;

                case '}':
                    // An unbalanced close is dropped rather than thrown on.
                    if (enclosing.Count > 0)
                        group = enclosing.Pop();
                    atGroupStart = false;
                    fallbackSkip = 0;
                    i++;
                    break;

                case '\\':
                    i = Control(source, i, text, ref group, ref atGroupStart, ref fallbackSkip);
                    break;

                case '\r' or '\n':
                    i++;
                    break;

                default:
                    Emit(text, c, group, ref fallbackSkip);
                    atGroupStart = false;
                    i++;
                    break;
            }
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Reads the control word or control symbol starting at the backslash at
    /// <paramref name="start"/>, applies it, and returns the index just past
    /// it (past its one delimiting space, for a control word).
    /// </summary>
    private static int Control(
        string source, int start, StringBuilder text,
        ref GroupState group, ref bool atGroupStart, ref int fallbackSkip)
    {
        bool first = atGroupStart;
        atGroupStart = false;

        // A trailing lone backslash: nothing follows to apply.
        if (start + 1 >= source.Length)
            return source.Length;

        return char.IsAsciiLetter(source[start + 1])
            ? ControlWord(source, start, text, ref group, first, ref fallbackSkip)
            : ControlSymbol(source, start, text, ref group, first, ref fallbackSkip);
    }

    private static int ControlWord(
        string source, int start, StringBuilder text,
        ref GroupState group, bool first, ref int fallbackSkip)
    {
        int end = start + 1;
        while (end < source.Length && char.IsAsciiLetter(source[end]))
            end++;
        string word = source[(start + 1)..end];

        // An optional signed decimal parameter. A '-' with no digits behind it
        // is not part of the control word and stays as text.
        bool negative = end < source.Length && source[end] == '-';
        int digits = negative ? end + 1 : end;
        int afterDigits = digits;
        while (afterDigits < source.Length && char.IsAsciiDigit(source[afterDigits]))
            afterDigits++;

        int? parameter = null;
        if (afterDigits > digits)
        {
            parameter = Magnitude(source, digits, afterDigits, negative);
            end = afterDigits;
        }

        // One delimiting space belongs to the control word and is not text.
        if (end < source.Length && source[end] == ' ')
            end++;

        if (word == "u")
        {
            int unit = parameter ?? 0;
            if (unit < 0)
                unit += 0x10000;
            Emit(text, (char)(unit & 0xFFFF), group, ref fallbackSkip);
            fallbackSkip = group.UnicodeFallbackSkip;
            return end;
        }

        // Every control word but \uN ends an outstanding fallback skip rather
        // than being swallowed by it — a malformed document must not eat a
        // \par. Cleared before the switch, so what follows always emits.
        fallbackSkip = 0;

        if (first && TextlessDestinations.Contains(word))
        {
            group = group with { Suppressed = true };
            return end;
        }

        switch (word)
        {
            case "par" or "line":
                Emit(text, LineBreak, group, ref fallbackSkip);
                break;
            case "tab":
                Emit(text, "\t", group, ref fallbackSkip);
                break;
            case "uc":
                group = group with { UnicodeFallbackSkip = Math.Max(0, parameter ?? 1) };
                break;
            default:
                // Every other control word is formatting, and is dropped.
                break;
        }

        return end;
    }

    private static int ControlSymbol(
        string source, int start, StringBuilder text,
        ref GroupState group, bool first, ref int fallbackSkip)
    {
        char symbol = source[start + 1];
        switch (symbol)
        {
            case '\\' or '{' or '}':
                Emit(text, symbol, group, ref fallbackSkip);
                return start + 2;

            case '~':                                   // non-breaking space
                Emit(text, ' ', group, ref fallbackSkip);
                return start + 2;

            case '_':                                   // non-breaking hyphen
                Emit(text, '-', group, ref fallbackSkip);
                return start + 2;

            case '-':                                   // optional hyphen: not shown
                fallbackSkip = 0;
                return start + 2;

            case '*':                                   // ignorable destination
                if (first)
                    group = group with { Suppressed = true };
                fallbackSkip = 0;
                return start + 2;

            case '\r' or '\n':                          // the spec's spelling of \par
                fallbackSkip = 0;
                Emit(text, LineBreak, group, ref fallbackSkip);
                return start + 2;

            case '\'':
                return HexEscape(source, start, text, group, ref fallbackSkip);

            default:
                fallbackSkip = 0;
                return start + 2;
        }
    }

    /// <summary>
    /// <c>\'hh</c>: the character whose byte is <c>hh</c> in the document's
    /// ANSI code page. A truncated or non-hex escape yields nothing and
    /// consumes only the backslash and the quote, leaving whatever followed as
    /// text — degrading by a character rather than by the rest of the note.
    /// </summary>
    private static int HexEscape(
        string source, int start, StringBuilder text, GroupState group, ref int fallbackSkip)
    {
        int digits = start + 2;
        if (digits + 1 >= source.Length
            || !TryHex(source[digits], out int high)
            || !TryHex(source[digits + 1], out int low))
        {
            fallbackSkip = 0;
            return Math.Min(digits, source.Length);
        }

        Emit(text, AnsiChar((high << 4) | low), group, ref fallbackSkip);
        return digits + 2;
    }

    /// <summary>
    /// One byte of the document's ANSI code page as a character: Latin-1,
    /// except the Windows-1252 supplement — see the stated limit on
    /// <see cref="RtfPlainText"/>.
    /// </summary>
    private static char AnsiChar(int value) =>
        value is >= 0x80 and <= 0x9F ? Windows1252Supplement[value - 0x80] : (char)value;

    /// <summary>
    /// Appends <paramref name="value"/> unless this group is suppressed or a
    /// <c>\uN</c> fallback is still owed — one unit of which this consumes.
    /// </summary>
    private static void Emit(StringBuilder text, char value, GroupState group, ref int fallbackSkip)
    {
        if (fallbackSkip > 0)
            fallbackSkip--;
        else if (!group.Suppressed)
            text.Append(value);
    }

    /// <inheritdoc cref="Emit(StringBuilder, char, GroupState, ref int)"/>
    private static void Emit(StringBuilder text, string value, GroupState group, ref int fallbackSkip)
    {
        if (fallbackSkip > 0)
            fallbackSkip--;
        else if (!group.Suppressed)
            text.Append(value);
    }

    /// <summary>
    /// The control word's decimal parameter, saturating at
    /// <see cref="int.MaxValue"/> rather than overflowing — a nonsensical
    /// parameter must degrade, not throw.
    /// </summary>
    private static int Magnitude(string source, int digits, int end, bool negative)
    {
        long value = 0;
        for (int i = digits; i < end && value <= int.MaxValue; i++)
            value = value * 10 + (source[i] - '0');
        value = Math.Min(value, int.MaxValue);
        return (int)(negative ? -value : value);
    }

    private static bool TryHex(char c, out int value)
    {
        value = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };
        return value >= 0;
    }

    /// <summary>
    /// What a group inherits from its parent and may override for itself: the
    /// <c>\ucN</c> fallback width, and whether the group's text is discarded.
    /// Suppression only ever spreads inwards — a nested group inside a font
    /// table is still a font table.
    /// </summary>
    private readonly record struct GroupState(int UnicodeFallbackSkip, bool Suppressed);
}
