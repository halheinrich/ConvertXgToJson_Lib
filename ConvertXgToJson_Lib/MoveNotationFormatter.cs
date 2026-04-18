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
/// Adjacent sub-moves on the same checker with no hit at the intermediate
/// point are compressed ("24/21 21/15" → "24/15"); a hit at the
/// intermediate keeps both legs visible so the hit is not lost.
/// </summary>
internal static class MoveNotationFormatter
{
    private record struct Leg(int FromRaw, int ToRaw, bool Hit);

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

        var legs = new List<Leg>(4);
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

            legs.Add(new Leg(from, to, hit));
        }

        if (legs.Count == 0) return string.Empty;

        // Merge chained sub-moves on the same checker when the intermediate
        // point had no hit — e.g. (23,20,20,14) → single leg (23,14).
        var merged = new List<Leg>(legs.Count);
        for (int i = 0; i < legs.Count; i++)
        {
            var cur = legs[i];
            while (i + 1 < legs.Count
                   && !cur.Hit
                   && cur.ToRaw >= 0 && cur.ToRaw <= 23
                   && legs[i + 1].FromRaw == cur.ToRaw)
            {
                var next = legs[i + 1];
                cur = new Leg(cur.FromRaw, next.ToRaw, next.Hit);
                i++;
            }
            merged.Add(cur);
        }

        var sb = new StringBuilder();
        int idx = 0;
        while (idx < merged.Count)
        {
            int run = 1;
            while (idx + run < merged.Count && merged[idx + run] == merged[idx])
                run++;

            if (sb.Length > 0) sb.Append(' ');
            var leg = merged[idx];
            sb.Append(LabelFrom(leg.FromRaw)).Append('/').Append(LabelTo(leg.ToRaw));
            if (leg.Hit) sb.Append('*');
            if (run > 1) sb.Append('(').Append(run).Append(')');

            idx += run;
        }

        return sb.ToString();
    }

    private static string LabelFrom(int raw) => raw == 24 ? "bar" : (raw + 1).ToString();
    private static string LabelTo(int raw) => raw == -1 ? "off" : (raw + 1).ToString();
}
