namespace ConvertXgToJson_Lib.Parsing;

/// <summary>
/// Classification of a single XG candidate's first <c>(from, to)</c> pair into
/// the non-play sentinel families XG emits, or <see cref="None"/> for a real
/// play. Returned by <see cref="XgMoveEncoding.ClassifyCandidate"/>; the single
/// source consumers key skip decisions and logging off.
/// </summary>
internal enum SentinelKind
{
    /// <summary>A real, decodable play — not a sentinel.</summary>
    None,

    /// <summary>
    /// XG's illegal-play workaround: the recorded source play was illegal, so
    /// XG forced the next position rather than refusing to load and stamped
    /// <see cref="XgMoveEncoding.IllegalPlayMarker"/> as the candidate's
    /// <c>from</c>. Feeding this to a move leaf historically threw
    /// <see cref="IndexOutOfRangeException"/>; the iterator skips it.
    /// </summary>
    IllegalPlay,

    /// <summary>
    /// XG's no-legal-move (dance) encoding — <c>(0, 0)</c>. The on-roll player
    /// had no legal play; the whole move list is a board no-op. Normal, not an
    /// error.
    /// </summary>
    Dance,
}

/// <summary>
/// Decodes XG's raw per-candidate move encoding — the 8-element
/// <see cref="sbyte"/> arrays stored at <see cref="Models.BestMoveAnalysis.Moves"/>,
/// adjacent <c>(from, to)</c> pairs in active-player POV, 0-indexed point values.
///
/// <para>
/// Single source of the XG sentinel vocabulary and the point-index resolution
/// shared by <see cref="XgMoveTranslator"/> (encoding →
/// <see cref="BgDataTypes_Lib.Play"/>) and <see cref="AfterBoardBuilder"/>
/// (encoding → after-board). The sentinel <i>values</i> (<see cref="Terminator"/>,
/// <see cref="IllegalPlayMarker"/>, the <c>(0, 0)</c> dance pair, the
/// <see cref="Bar"/> point) are single-sourced here. The control flow each
/// consumer wraps around those values is intentionally <i>not</i> shared:
/// <see cref="XgMoveTranslator"/> deliberately renders a dance rather than
/// breaking on it, <see cref="AfterBoardBuilder"/> treats a dance as a board
/// no-op, and the decision iterator skips both illegal plays and dances at its
/// boundary. Centralize the recognition of the values, not the decisions about
/// them.
/// </para>
/// </summary>
internal static class XgMoveEncoding
{
    /// <summary>
    /// XG's per-pair terminator: a <c>from</c> of <c>-1</c> ends a candidate's
    /// move list — the remaining pairs are zero / terminator padding.
    /// </summary>
    internal const sbyte Terminator = -1;

    /// <summary>
    /// XG's illegal-play marker. When the recorded play in the source file is
    /// illegal, XG forces the next position rather than refusing to load and
    /// stamps this value as the offending candidate's <c>from</c>. <c>-100</c>
    /// is never a legal <c>from</c>, so its presence at <c>from</c> alone
    /// identifies the marker — the partner <c>to</c> may be <c>-100</c> or a
    /// stray index.
    /// </summary>
    internal const sbyte IllegalPlayMarker = -100;

    /// <summary>
    /// The bar in XG's 0-indexed <c>from</c> encoding. Resolves to
    /// <see cref="BarIdx"/> in 1-based point space.
    /// </summary>
    internal const sbyte Bar = 24;

    /// <summary>The bar's 1-based point index (bar entry destination / origin).</summary>
    internal const int BarIdx = 25;

    /// <summary>
    /// True when <paramref name="from"/> is the per-pair
    /// <see cref="Terminator"/> — the signal to stop walking a candidate's
    /// move pairs.
    /// </summary>
    internal static bool IsTerminator(sbyte from) => from == Terminator;

    /// <summary>
    /// Classifies a candidate by its first <c>(from, to)</c> pair. A candidate
    /// shorter than a full pair is <see cref="SentinelKind.None"/> (nothing to
    /// recognise). Illegal-play detection keys on <c>from ==
    /// </c><see cref="IllegalPlayMarker"/> alone — covering both
    /// <c>(-100, -100)</c> and <c>(-100, X)</c> — because <c>-100</c> is never
    /// a legal <c>from</c>. The dance sentinel is the exact <c>(0, 0)</c> pair.
    /// </summary>
    internal static SentinelKind ClassifyCandidate(sbyte[] candidate)
    {
        if (candidate.Length < 2) return SentinelKind.None;
        if (candidate[0] == IllegalPlayMarker) return SentinelKind.IllegalPlay;
        if (candidate[0] == 0 && candidate[1] == 0) return SentinelKind.Dance;
        return SentinelKind.None;
    }

    /// <summary>
    /// Resolves one raw <c>(from, to)</c> pair into 1-based point indices:
    /// <c>from == </c><see cref="Bar"/> is bar entry (<c>FromIdx =
    /// </c><see cref="BarIdx"/>); regular points are <c>value + 1</c>;
    /// <c>to &lt; 0</c> is a bear-off (<c>IsBearOff</c> set, <c>ToIdx</c>
    /// unused). XG encodes overshoot bear-offs as <c>to = from - die</c>, so
    /// any negative <c>to</c> is a bear-off.
    ///
    /// <para>
    /// Defensive fail-fast: a <c>from</c> outside <c>[0, </c><see cref="Bar"/><c>]</c>
    /// or a <c>to</c> above <see cref="Bar"/> is outside XG's legal move domain
    /// and throws <see cref="ArgumentOutOfRangeException"/> with a legible
    /// message rather than letting a board index underflow / overflow. The
    /// decision iterator already skips the known sentinels
    /// (<see cref="ClassifyCandidate"/>), so this path is unreachable for known
    /// inputs — it turns a future <i>unknown</i> sentinel or corrupt encoding
    /// into a clear failure instead of a raw <see cref="IndexOutOfRangeException"/>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="from"/> is outside <c>[0, </c><see cref="Bar"/><c>]</c>,
    /// or <paramref name="to"/> exceeds <see cref="Bar"/>.
    /// </exception>
    internal static (int FromIdx, int ToIdx, bool IsBearOff) DecodeMovePair(sbyte from, sbyte to)
    {
        if (from < 0 || from > Bar)
            throw new ArgumentOutOfRangeException(
                nameof(from), from,
                $"XG move 'from' must be a 0-indexed point in [0, {Bar}] (where {Bar} is the bar); "
                + $"value {from} is outside the legal domain. "
                + (from == IllegalPlayMarker
                    ? "This is XG's illegal-play marker, which the decision iterator is expected to "
                      + "skip before any move decoding."
                    : "This indicates an unrecognised XG sentinel or a corrupt move encoding."));

        // 'to' is legitimately negative for bear-offs (encoded as from - die),
        // so only the high end is bounded: a destination point cannot exceed
        // the bar.
        if (to > Bar)
            throw new ArgumentOutOfRangeException(
                nameof(to), to,
                $"XG move 'to' must be a point index of at most {Bar}; "
                + $"value {to} is outside the legal domain (unrecognised sentinel or corrupt encoding).");

        return (from == Bar ? BarIdx : from + 1, to < 0 ? 0 : to + 1, to < 0);
    }
}
