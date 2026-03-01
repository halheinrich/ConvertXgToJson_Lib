using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Iterates over XgFile records and yields a <see cref="DecisionRow"/> for
/// every checker-play (MoveRecord) and cube decision (CubeRecord) that has
/// been analysed by XG.
/// </summary>
public static class XgDecisionIterator
{
    // -----------------------------------------------------------------------
    //  Public API — single file
    // -----------------------------------------------------------------------

    /// <summary>Yields all decisions from a single already-parsed <see cref="XgFile"/>.</summary>
    /// <param name="file">The parsed XG file.</param>
    /// <param name="matchId">Identifier for this match (e.g. filename without extension).</param>
    public static IEnumerable<DecisionRow> Iterate(XgFile file, string matchId)
    {
        var context = new MatchContext(file.Records, matchId);

        foreach (var record in file.Records)
        {
            context.Update(record);

            if (record is MoveRecord move && IsAnalysed(move))
            {
                var row = BuildMoveRow(move, context);
                if (row != null) yield return row;
            }
            else if (record is CubeRecord cube && IsAnalysed(cube))
            {
                var row = BuildCubeRow(cube, context);
                if (row != null) yield return row;
            }
        }
    }

    // -----------------------------------------------------------------------
    //  Public API — directories
    // -----------------------------------------------------------------------

    /// <summary>
    /// Parses all .xg files in <paramref name="xgDir"/> directly and yields every decision.
    /// Use this as the primary path — no intermediate JSON step required.
    /// </summary>
    public static IEnumerable<DecisionRow> IterateXgDirectory(string xgDir)
    {
        foreach (var path in Directory.EnumerateFiles(xgDir, "*.xg"))
        {
            XgFile file;
            try { file = XgFileReader.ReadFile(path); }
            catch { continue; }

            string matchId = Path.GetFileNameWithoutExtension(path);
            foreach (var row in Iterate(file, matchId))
                yield return row;
        }
    }

    /// <summary>
    /// Reads pre-exported JSON files from <paramref name="jsonDir"/> and yields every decision.
    /// Use this when you want to avoid re-parsing .xg files and have JSON already available.
    /// </summary>
    public static IEnumerable<DecisionRow> IterateJsonDirectory(string jsonDir)
    {
        foreach (var path in Directory.EnumerateFiles(jsonDir, "*.json"))
        {
            XgFile file;
            try { file = XgFileReader.ReadJson(path); }
            catch { continue; }

            string matchId = Path.GetFileNameWithoutExtension(path);
            foreach (var row in Iterate(file, matchId))
                yield return row;
        }
    }

    // -----------------------------------------------------------------------
    //  Move record
    // -----------------------------------------------------------------------

    private static DecisionRow? BuildMoveRow(MoveRecord move, MatchContext ctx)
    {
        var analysis = move.Analysis;
        if (analysis.MoveCount == 0 || analysis.Evals.Length == 0)
            return null;

        var    bestEval = analysis.Evals[0];
        int    dice     = DiceToInt(move.Dice);
        string depth    = LevelLabel(analysis.EvalLevels.Length > 0
                              ? analysis.EvalLevels[0].Level : (short)0);

        string xgid = XgidEncoder.Encode(
            position:       move.InitialPosition,
            cubeValue:      ctx.CubeValue,
            cubePos:        ctx.CubePosition,
            turn:           move.ActivePlayer >= 0 ? 1 : -1,
            dice:           dice,
            score1:         ctx.Score1,
            score2:         ctx.Score2,
            crawfordJacoby: ctx.CrawfordJacoby,
            matchLength:    ctx.MatchLength);

        return new DecisionRow
        {
            Xgid          = xgid,
            Error         = Math.Abs(move.ErrorMove),
            MatchScore    = ctx.MatchScore,
            MatchLength   = ctx.MatchLength,
            Player        = ctx.PlayerName(move.ActivePlayer),
            Match         = ctx.MatchId,
            Game          = ctx.GameNumber,
            MoveNum       = ctx.MoveNumber,
            Roll          = dice,
            AnalysisDepth = depth,
            Equity        = bestEval.Equity,
            IsCube        = false,
        };
    }

    // -----------------------------------------------------------------------
    //  Cube record
    // -----------------------------------------------------------------------

    private static DecisionRow? BuildCubeRow(CubeRecord cube, MatchContext ctx)
    {
        var    analysis   = cube.Analysis;
        double bestEquity = BestCubeEquity(analysis);
        string depth      = LevelLabel(analysis.LevelRequest);

        string xgid = XgidEncoder.Encode(
            position:       cube.Position,
            cubeValue:      ctx.CubeValue,
            cubePos:        ctx.CubePosition,
            turn:           cube.ActivePlayer >= 0 ? 1 : -1,
            dice:           0,   // 0 encodes as "00" = to roll / cube decision
            score1:         ctx.Score1,
            score2:         ctx.Score2,
            crawfordJacoby: ctx.CrawfordJacoby,
            matchLength:    ctx.MatchLength);

        return new DecisionRow
        {
            Xgid          = xgid,
            Error         = Math.Abs(cube.ErrorCube),
            MatchScore    = ctx.MatchScore,
            MatchLength   = ctx.MatchLength,
            Player        = ctx.PlayerName(cube.ActivePlayer),
            Match         = ctx.MatchId,
            Game          = ctx.GameNumber,
            MoveNum       = ctx.MoveNumber,
            Roll          = 0,
            AnalysisDepth = depth,
            Equity        = bestEquity,
            IsCube        = true,
        };
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    private static bool IsAnalysed(MoveRecord move) =>
        move.Analysis.MoveCount > 0 && move.Analysis.Evals.Length > 0;

    private static bool IsAnalysed(CubeRecord cube) =>
        cube.Analysis.Level > 0 || cube.Analysis.LevelRequest > 0;

    private static int DiceToInt(int[] dice) =>
        dice.Length >= 2 ? dice[0] * 10 + dice[1] : 0;

    private static double BestCubeEquity(DoubleActionAnalysis a)
    {
        double nd   = IsUsable(a.EquityNoDouble)   ? a.EquityNoDouble   : double.MinValue;
        double dt   = IsUsable(a.EquityDoubleTake) ? a.EquityDoubleTake : double.MinValue;
        double dd   = IsUsable(a.EquityDoubleDrop) ? a.EquityDoubleDrop : double.MinValue;
        double best = Math.Max(nd, Math.Max(dt, dd));
        return best == double.MinValue ? 0 : best;
    }

    private static bool IsUsable(float v) =>
        !float.IsNaN(v) && !float.IsInfinity(v) && v != 0f && v > -999f;

    /// <summary>
    /// XG analysis levels: 0=1-ply, 1=2-ply, 2=3-ply, 3=XG Roller,
    /// 4=XG Roller+, 5=XG Roller++, 6=Rollout
    /// </summary>
    private static string LevelLabel(short level) => level switch
    {
        0 => "1-ply",
        1 => "2-ply",
        2 => "3-ply",
        3 => "XG Roller",
        4 => "XG Roller+",
        5 => "XG Roller++",
        6 => "Rollout",
        _ => $"{level}-ply",
    };

    // -----------------------------------------------------------------------
    //  Match context tracker
    // -----------------------------------------------------------------------

    private sealed class MatchContext
    {
        public string MatchId        { get; }
        public int    MatchLength    { get; private set; }
        public int    Score1         { get; private set; }
        public int    Score2         { get; private set; }
        public int    CrawfordJacoby { get; private set; }
        public int    CubeValue      { get; private set; } = 1;
        public int    CubePosition   { get; private set; }
        public int    GameNumber     { get; private set; }
        public int    MoveNumber     { get; private set; }

        private string _player1 = "Player 1";
        private string _player2 = "Player 2";

        public MatchContext(List<SaveRecord> records, string matchId)
        {
            MatchId = matchId;
            foreach (var r in records)
            {
                if (r is MatchHeaderRecord hm)
                {
                    _player1    = hm.Player1;
                    _player2    = hm.Player2;
                    MatchLength = hm.MatchLength >= 99999 ? 0 : hm.MatchLength;
                    // Money game: Jacoby + 2×Beaver bitmask per XGID spec field 8
                    // Match game: 0 until crawford game, then 1
                    CrawfordJacoby = MatchLength == 0
                        ? (hm.Jacoby ? 1 : 0) + (hm.Beaver ? 2 : 0)
                        : 0;
                    break;
                }
            }
        }

        public void Update(SaveRecord record)
        {
            switch (record)
            {
                case GameHeaderRecord gh:
                    GameNumber++;
                    MoveNumber   = 0;
                    Score1       = gh.Score1;
                    Score2       = gh.Score2;
                    if (MatchLength > 0 && gh.CrawfordApplies)
                        CrawfordJacoby = 1;
                    CubeValue    = 1;
                    CubePosition = 0;
                    break;

                case MoveRecord mv:
                    MoveNumber++;
                    CubeValue = Math.Max(1, mv.CubeValue);
                    break;

                case CubeRecord cb:
                    MoveNumber++;
                    if (cb.Doubled == 1 && cb.Taken == 1)
                    {
                        CubeValue    = Math.Max(1, cb.CubeValue) * 2;
                        CubePosition = cb.ActivePlayer >= 0 ? 1 : -1;
                    }
                    break;
            }
        }

        public string PlayerName(int activePlayer) =>
            activePlayer >= 0 ? _player1 : _player2;

        public string MatchScore
        {
            get
            {
                if (MatchLength == 0) return "money";
                int away1 = MatchLength - Score1;
                int away2 = MatchLength - Score2;
                return $"{away1}a{away2}a";
            }
        }
    }
}
