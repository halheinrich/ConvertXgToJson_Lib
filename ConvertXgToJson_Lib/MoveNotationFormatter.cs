using System.Text;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Formats XG's raw per-candidate move encoding into standard backgammon
/// notation ("8/5(2)", "bar/22", "24/18*", "6/off").
///
/// Input is <see cref="Models.BestMoveAnalysis.Moves"/>[i] — an 8-element
/// <see cref="sbyte"/> array containing up to four adjacent (from, to) pairs
/// in active-player POV, 0-indexed point values. Sentinels:
///   from == -1             → terminator (stop)
///   from == 24             → bar entry
///   to   == -1 (from ≥ 0)  → bear off
///   otherwise              → regular point (value + 1)
///
/// Hit detection reads the on-roll-POV board passed in; opponent blots
/// (board[to+1] == -1) are marked with "*" and the formatter updates the
/// board in place so chained sub-moves that revisit the same point don't
/// re-flag the hit.
///
/// Sub-moves on the same checker with no hit at the intermediate point are
/// compressed ("24/21 21/15" → "24/15"), including interleaved cases like
/// doubles where XG emits `(19,13)(19,13)(13,7)(13,7)` — each `(13,7)` is
/// matched to one of the earlier chains by endpoint, producing "20/8(2)".
/// A hit at the intermediate keeps both legs visible so the hit is not lost.
/// </summary>
internal static class MoveNotationFormatter
{
    private record struct Chain(int FromRaw, int ToRaw, bool Hit);

    /// <summary>
    /// Formats a candidate move list. <paramref name="boardOnRollPov"/> is
    /// the 26-element board BEFORE the move is applied, in on-roll POV
    /// (positive = active, negative = opponent). The caller may pass a
    /// scratch copy if it needs to preserve the original — this method
    /// mutates the array while walking hits.
    /// </summary>
    public static string Format(sbyte[] moves, int[] boardOnRollPov)
    {
        if (moves.Length == 0) return string.Empty;

        // Build chains greedily: each new (from, to) extends an existing
        // open chain whose endpoint equals `from` if possible, otherwise
        // starts a new chain. A chain whose last leg hit cannot be extended
        // — extending would erase the "*" on the intermediate point.
        var chains = new List<Chain>(4);
        for (int i = 0; i + 1 < moves.Length; i += 2)
        {
            sbyte from = moves[i];
            sbyte to   = moves[i + 1];
            if (from == -1) break;

            bool hit = false;
            if (to >= 0 && to <= 23)
            {
                int boardIdx = to + 1;
                if (boardOnRollPov[boardIdx] == -1)
                {
                    hit = true;
                    boardOnRollPov[boardIdx] = 0;
                    boardOnRollPov[0] -= 1;
                }
            }

            int extendIdx = -1;
            for (int j = 0; j < chains.Count; j++)
            {
                var c = chains[j];
                if (!c.Hit && c.ToRaw >= 0 && c.ToRaw <= 23 && c.ToRaw == from)
                {
                    extendIdx = j;
                    break;
                }
            }

            if (extendIdx >= 0)
            {
                var c = chains[extendIdx];
                chains[extendIdx] = new Chain(c.FromRaw, to, hit);
            }
            else
            {
                chains.Add(new Chain(from, to, hit));
            }
        }

        if (chains.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        int idx = 0;
        while (idx < chains.Count)
        {
            int run = 1;
            while (idx + run < chains.Count && chains[idx + run] == chains[idx])
                run++;

            if (sb.Length > 0) sb.Append(' ');
            var chain = chains[idx];
            sb.Append(LabelFrom(chain.FromRaw)).Append('/').Append(LabelTo(chain.ToRaw));
            if (chain.Hit) sb.Append('*');
            if (run > 1) sb.Append('(').Append(run).Append(')');

            idx += run;
        }

        return sb.ToString();
    }

    private static string LabelFrom(int raw) => raw == 24 ? "bar" : (raw + 1).ToString();
    private static string LabelTo(int raw) => raw == -1 ? "off" : (raw + 1).ToString();
}
