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
/// </summary>
internal static class MoveNotationFormatter
{
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

        var parts = new List<(string From, string To, bool Hit)>(4);
        for (int i = 0; i + 1 < moves.Length; i += 2)
        {
            sbyte from = moves[i];
            sbyte to   = moves[i + 1];
            if (from == -1) break;

            string fromLabel = from == 24 ? "bar" : (from + 1).ToString();
            string toLabel;
            bool hit = false;

            if (to == -1)
            {
                toLabel = "off";
            }
            else
            {
                toLabel = (to + 1).ToString();
                int boardIdx = to + 1;
                if (boardIdx >= 1 && boardIdx <= 24 && boardOnRollPov[boardIdx] == -1)
                {
                    hit = true;
                    boardOnRollPov[boardIdx] = 0; // opponent blot goes to bar
                    boardOnRollPov[0] -= 1;
                }
            }

            parts.Add((fromLabel, toLabel, hit));
        }

        if (parts.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        int idx = 0;
        while (idx < parts.Count)
        {
            int run = 1;
            while (idx + run < parts.Count && parts[idx + run] == parts[idx])
                run++;

            if (sb.Length > 0) sb.Append(' ');
            var (from, to, hit) = parts[idx];
            sb.Append(from).Append('/').Append(to);
            if (hit) sb.Append('*');
            if (run > 1) sb.Append('(').Append(run).Append(')');

            idx += run;
        }

        return sb.ToString();
    }
}
