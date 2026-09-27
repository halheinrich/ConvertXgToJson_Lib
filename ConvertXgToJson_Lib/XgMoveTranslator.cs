using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Parsing;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Translates XG's raw per-candidate move encoding into a
/// <see cref="BgDataTypes_Lib.Play"/>, with each hit recorded in
/// <see cref="BgDataTypes_Lib.Move.ToPt"/>'s sign. XG's encoding carries no
/// hit mark, so the translator reads it off the board the play starts from;
/// the play is then spelt by <see cref="Play.ToNotation"/> and applied by
/// BgDataTypes_Lib's play rule, never here.
///
/// <para>
/// Input is the 8-element <see cref="sbyte"/> array stored at
/// <c>BestMoveAnalysis.Moves[i]</c> — up to four adjacent (from, to)
/// pairs in active-player POV, 0-indexed point values:
/// </para>
/// <list type="bullet">
///   <item><description><c>from == -1</c> — terminator (stop)</description></item>
///   <item><description><c>from == 24</c> — bar entry → <c>FrPt = 25</c></description></item>
///   <item><description><c>to &lt; 0</c> — bear off → <c>ToPt = 0</c>. XG encodes overshoots as <c>to = from - die</c> and so can emit <c>-2, -3, …</c>; any negative is a bear-off. Point-index resolution is <see cref="XgMoveEncoding"/>'s.</description></item>
///   <item><description>otherwise — regular point, <c>FrPt = from + 1</c>, <c>ToPt = to + 1</c></description></item>
/// </list>
///
/// <para>
/// A move landing on a point that holds an opponent blot in the starting
/// position is the hit: its <c>ToPt</c> is negated, and the point stops
/// counting as a blot for the rest of the play, so a later move landing there
/// is not marked again. That is the whole of the translator's board reading —
/// it does not apply the play. XG's stored encodings are kept as they are,
/// multi-die moves included (halheinrich/backgammon#277): whether a
/// translated play is valid from its position is BgDataTypes_Lib's play rule's
/// question, which the decision iterator asks of every candidate.
/// </para>
///
/// <para>
/// The translator does NOT special-case XG's <c>(0, 0)</c> "no legal
/// moves" (dance) sentinel; the decision iterator skips sentinel analyses
/// before any candidate reaches here.
/// </para>
/// </summary>
internal static class XgMoveTranslator
{
    private const int BoardSize = 26;

    /// <summary>
    /// The play <paramref name="moves"/> encodes, played from
    /// <paramref name="board"/> — the position in the mover's frame, which
    /// the translator only reads.
    /// </summary>
    public static Play Translate(sbyte[] moves, BoardPosition board)
    {
        Span<int> points = stackalloc int[BoardSize];
        board.CopyTo(points);

        var play = new Play();
        for (int i = 0; i + 1 < moves.Length; i += 2)
        {
            sbyte from = moves[i];
            sbyte to = moves[i + 1];
            if (XgMoveEncoding.IsTerminator(from)) break;

            var (frPt, toLabel, isBearOff) = XgMoveEncoding.DecodeMovePair(from, to);
            int toPt;
            if (isBearOff)
            {
                toPt = 0;
            }
            else if (points[toLabel] == -1)
            {
                points[toLabel] = 0;
                toPt = -toLabel;
            }
            else
            {
                toPt = toLabel;
            }
            play.Add(new Move(frPt, toPt));
        }
        return play;
    }
}
