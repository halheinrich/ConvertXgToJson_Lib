using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Parsing;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Translates XG's raw per-candidate move encoding into a
/// <see cref="BgDataTypes_Lib.Play"/> with hits pre-encoded into
/// <see cref="BgDataTypes_Lib.Move.ToPt"/>'s sign, ready to hand to
/// <see cref="BgMoveGen.MoveNotationFormatter.Format(Play)"/>.
///
/// <para>
/// Input is the 8-element <see cref="sbyte"/> array stored at
/// <c>BestMoveAnalysis.Moves[i]</c> — up to four adjacent (from, to)
/// pairs in active-player POV, 0-indexed point values:
/// </para>
/// <list type="bullet">
///   <item><description><c>from == -1</c> — terminator (stop)</description></item>
///   <item><description><c>from == 24</c> — bar entry → <c>FrPt = 25</c></description></item>
///   <item><description><c>to &lt; 0</c> — bear off → <c>ToPt = 0</c>. XG encodes overshoots as <c>to = from - die</c> and so can emit <c>-2, -3, …</c>; treating any negative as bear-off. Point-index resolution is shared with <see cref="ConvertXgToJson_Lib.Parsing.AfterBoardBuilder"/> via <see cref="ConvertXgToJson_Lib.Parsing.XgMoveEncoding"/>.</description></item>
///   <item><description>otherwise — regular point, <c>FrPt = from + 1</c>, <c>ToPt = to + 1</c></description></item>
/// </list>
///
/// <para>
/// Hit detection consumes the on-roll-POV board passed in: when the
/// destination point holds an opponent blot (<c>board[to+1] == -1</c>),
/// the translator records the hit by negating <c>ToPt</c> and updates
/// the board in place so chained sub-moves that revisit the same point
/// don't re-flag the hit. The caller is responsible for passing a
/// scratch copy if it needs to preserve the original — this method
/// mutates the array. Mirrors the producer-side hit semantics that
/// previously lived inline in the local <c>MoveNotationFormatter</c>;
/// surfacing it on the translator keeps the formatter (and the
/// underlying <see cref="BgDataTypes_Lib.Play"/> primitive) board-agnostic.
/// </para>
///
/// <para>
/// The translator does NOT special-case XG's <c>(0, 0)</c> "no legal
/// moves" (dance) sentinel — preserving the local formatter's prior
/// behaviour, which rendered dances as "1/1" garbage. AfterBoardBuilder
/// breaks on <c>(0, 0)</c> because a dance has no board change to
/// apply; rendering a recognizable dance sentinel in notation is a
/// distinct concern, deferred to a follow-up.
/// </para>
/// </summary>
internal static class XgMoveTranslator
{
    public static Play Translate(sbyte[] moves, int[] boardOnRollPov)
    {
        var play = new Play();
        for (int i = 0; i + 1 < moves.Length; i += 2)
        {
            sbyte from = moves[i];
            sbyte to = moves[i + 1];
            if (from == -1) break;

            var (frPt, toLabel, isBearOff) = XgMoveEncoding.DecodeMovePair(from, to);
            int toPt;
            if (isBearOff)
            {
                toPt = 0;
            }
            else if (boardOnRollPov[toLabel] == -1)
            {
                boardOnRollPov[toLabel] = 0;
                boardOnRollPov[0] -= 1;
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
