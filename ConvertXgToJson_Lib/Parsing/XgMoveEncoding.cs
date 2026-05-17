namespace ConvertXgToJson_Lib.Parsing;

/// <summary>
/// Decodes XG's raw per-candidate move encoding — the 8-element
/// <see cref="sbyte"/> arrays stored at <see cref="Models.BestMoveAnalysis.Moves"/>,
/// adjacent <c>(from, to)</c> pairs in active-player POV, 0-indexed point values.
///
/// <para>
/// Single source of the point-index resolution shared by
/// <see cref="XgMoveTranslator"/> (encoding → <see cref="BgDataTypes_Lib.Play"/>)
/// and <see cref="AfterBoardBuilder"/> (encoding → after-board). Loop control
/// (the <c>from == -1</c> terminator, the <c>(0, 0)</c> dance sentinel) and
/// hit handling stay with each consumer — those genuinely differ between a
/// play-builder and a board-applier.
/// </para>
/// </summary>
internal static class XgMoveEncoding
{
    /// <summary>
    /// Resolves one raw <c>(from, to)</c> pair into 1-based point indices:
    /// <c>from == 24</c> is bar entry (<c>FromIdx = 25</c>); regular points are
    /// <c>value + 1</c>; <c>to &lt; 0</c> is a bear-off (<c>IsBearOff</c> set,
    /// <c>ToIdx</c> unused). XG encodes overshoot bear-offs as
    /// <c>to = from - die</c>, so any negative <c>to</c> is a bear-off.
    /// </summary>
    internal static (int FromIdx, int ToIdx, bool IsBearOff) DecodeMovePair(sbyte from, sbyte to)
        => (from == 24 ? 25 : from + 1, to < 0 ? 0 : to + 1, to < 0);
}
