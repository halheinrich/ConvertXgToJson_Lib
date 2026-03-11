using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Iterates over XgFile records and yields a <see cref="DecisionRow"/> for
/// every checker-play (MoveRecord) and cube decision (CubeRecord) that has
/// been analysed by XG. Dummy change to test GitHub integrity.
/// </summary>
public static class XgDecisionIterator
{
    // -----------------------------------------------------------------------
    //  Public API — single file
    // -----------------------------------------------------------------------

    /// <summary>Yields all decisions from a single already-parsed <see cref="XgFile"/>.</summary>
    public static IEnumerable<DecisionRow> Iterate(XgFile file, string matchId)
    {
        var context = new MatchContext(file.Records, matchId);

        foreach (var record in file.Records)
        {
            context.Update(record);

            if (record is MoveRecord move && IsAnalysed(move))
            {
                var row = BuildMoveRow(move, context, file.Rollouts);
                if (row != null) yield return row;
            }
            else if (record is CubeRecord cube && IsAnalysed(cube))
            {
                foreach (var row in BuildCubeRows(cube, context, file.Rollouts))
                    yield return row;
            }
        }
    }

    // -----------------------------------------------------------------------
    //  Public API — directories
    // -----------------------------------------------------------------------

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
    private static DecisionRow? BuildMoveRow(MoveRecord move, MatchContext ctx, List<RolloutContext> rollouts)
    {
        var analysis = move.Analysis;
        if (analysis.MoveCount == 0 || analysis.Evals.Length == 0)
            return null;

        var bestEval = analysis.Evals[0];
        int dice = DiceToInt(move.Dice);

        string depth = ResolveDepth(
            evalLevel: analysis.EvalLevels.Length > 0 ? analysis.EvalLevels[0].Level : (short)0,
            rolloutIndices: move.RolloutIndices,
            rollouts: rollouts);

        // XGID position is always bottom-player perspective.
        // When the top player is on roll, XG stores InitialPosition from the top
        // player's perspective, so flip it back to bottom-player perspective.
        var xgidPosition = move.ActivePlayer >= 0
            ? move.InitialPosition
            : FlipPosition(move.InitialPosition);

        // CubePos is stored relative to the active player; normalize to bottom-player.
        int xgidCubePos = move.ActivePlayer >= 0
            ? ctx.CubePosition
            : -ctx.CubePosition;

        string xgid = XgidEncoder.Encode(
            position: xgidPosition,
            cubeValue: ctx.CubeValue,
            cubePos: xgidCubePos,
            turn: 1,
            dice: dice,
            score1: move.ActivePlayer >= 0 ? ctx.Score1 : ctx.Score2,
            score2: move.ActivePlayer >= 0 ? ctx.Score2 : ctx.Score1,
            crawfordJacoby: ctx.CrawfordJacoby,
            matchLength: ctx.MatchLength,
            maxCubeLog2: ctx.MaxCubeLimit);

        return new DecisionRow
        {
            Xgid = xgid,
            Error = Math.Abs(move.MoveError),
            MatchScore = ctx.MatchScore,
            MatchLength = ctx.MatchLength,
            Player = ctx.PlayerName(move.ActivePlayer),
            Match = ctx.MatchId,
            Game = ctx.GameNumber,
            MoveNum = ctx.MoveNumber,
            Roll = dice,
            AnalysisDepth = depth,
            Equity = bestEval.Equity,
            Board = ToBoard(move.InitialPosition.Points, move.ActivePlayer),
        };
    }

    // -----------------------------------------------------------------------
    //  Cube record
    // -----------------------------------------------------------------------
    private static IEnumerable<DecisionRow> BuildCubeRows(CubeRecord cube, MatchContext ctx, List<RolloutContext> rollouts)
    {
        var analysis = cube.Analysis;

        string depth = ResolveDepth(
            evalLevel: analysis.LevelRequest,
            rolloutIndices: [cube.RolloutIndex],
            rollouts: rollouts);

        // CubeB encoding: 0=centered(1), ±N = 2^N owned by ± side relative to bottom player
        int cubeActual = cube.CubeValue == 0 ? 1 : (int)Math.Pow(2, Math.Abs(cube.CubeValue));
        int cubePos = cube.CubeValue == 0 ? 0 : (cube.CubeValue > 0 ? 1 : -1);

        // XGID position is always bottom-player perspective.
        // When the top player is on roll, XG stores Position from the top
        // player's perspective, so flip it back to bottom-player perspective.
        var xgidPosition = cube.ActivePlayer >= 0
            ? cube.Position
            : FlipPosition(cube.Position);

        // CubePos is stored relative to the active player; normalize to bottom-player.
        int xgidCubePos = cube.ActivePlayer >= 0
            ? cubePos
            : -cubePos;

        string xgid = XgidEncoder.Encode(
            position: xgidPosition,
            cubeValue: cubeActual,
            cubePos: xgidCubePos,
            turn: 1,
            dice: 0,
            score1: cube.ActivePlayer >= 0 ? ctx.Score1 : ctx.Score2,
            score2: cube.ActivePlayer >= 0 ? ctx.Score2 : ctx.Score1,
            crawfordJacoby: ctx.CrawfordJacoby,
            matchLength: ctx.MatchLength,
            maxCubeLog2: ctx.MaxCubeLimit);

        int[] board = ToBoard(cube.Position.Points, cube.ActivePlayer);

        // Row 1: the doubling player's decision
        yield return new DecisionRow
        {
            Xgid = xgid,
            Error = Math.Abs(cube.ErrorCube),
            MatchScore = ctx.MatchScore,
            MatchLength = ctx.MatchLength,
            Player = ctx.PlayerName(cube.ActivePlayer),
            Match = ctx.MatchId,
            Game = ctx.GameNumber,
            MoveNum = ctx.MoveNumber,
            Roll = 0,
            AnalysisDepth = depth,
            Equity = IsUsable(analysis.EquityNoDouble) ? analysis.EquityNoDouble : 0f,
            Board = board,
        };

        // Row 2: the take/drop decision — only when player actually doubled
        // and the take error is meaningful (not the sentinel -1000)
        if (cube.Doubled == 1 && cube.ErrorTake > -999.0)
        {
            yield return new DecisionRow
            {
                Xgid = xgid,
                Error = Math.Abs(cube.ErrorTake),
                MatchScore = ctx.MatchScore,
                MatchLength = ctx.MatchLength,
                Player = ctx.PlayerName(-cube.ActivePlayer),
                Match = ctx.MatchId,
                Game = ctx.GameNumber,
                MoveNum = ctx.MoveNumber,
                Roll = 0,
                AnalysisDepth = depth,
                Equity = IsUsable(analysis.EquityDoubleTake) ? analysis.EquityDoubleTake : 0f,
                Board = FlipBoard(board),
            };
        }
    }    // -----------------------------------------------------------------------
    //  Board helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Converts a raw sbyte[26] position (positive = bottom player) into an
    /// int[26] normalized to the player on roll.
    /// If the active player is the bottom player (activePlayer >= 0) the
    /// array is used as-is; otherwise every element is negated and the array
    /// is reversed so that positive always means "player on roll".
    /// </summary>
    private static int[] ToBoard(sbyte[] points, int activePlayer)
    {
        if (activePlayer >= 0)
        {
            // Bottom player is on roll — positive values already represent them
            var board = new int[26];
            for (int i = 0; i < 26; i++)
                board[i] = points[i];
            return board;
        }
        else
        {
            // Top player is on roll — negate and reverse so positive = on roll
            var board = new int[26];
            for (int i = 0; i < 26; i++)
                board[i] = -points[25 - i];
            return board;
        }
    }

    /// <summary>
    /// Flips a board from one player's perspective to the other's.
    /// Used for the take/drop row where the responder is on roll.
    /// </summary>
    private static int[] FlipBoard(int[] board)
    {
        var flipped = new int[26];
        for (int i = 0; i < 26; i++)
            flipped[i] = -board[25 - i];
        return flipped;
    }
    private static PositionEngine FlipPosition(PositionEngine pos)
    {
        var flipped = new sbyte[26];
        for (int i = 0; i < 26; i++)
            flipped[i] = (sbyte)-pos.Points[25 - i];
        return new PositionEngine { Points = flipped };
    }
    // -----------------------------------------------------------------------
    //  Depth resolution
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the analysis depth label.
    /// Rollout detection is index-based: if any RolloutIndex points to a valid
    /// RolloutContext, it's a rollout regardless of the eval level value.
    /// </summary>
    private static string ResolveDepth(
        short evalLevel,
        int[] rolloutIndices,
        List<RolloutContext> rollouts)
    {
        // Find first valid rollout index
        foreach (int i in rolloutIndices)
        {
            if (i >= 0 && i < rollouts.Count)
            {
                var ctx = rollouts[i];
                int plyLevel = ctx.Level2 > 0 ? ctx.Level2
                             : ctx.Level1 > 0 ? ctx.Level1
                             : ctx.LevelTrunc;
                return $"Rollout: {ctx.GamesRolled} trials. {LevelLabel((short)plyLevel)}";
            }
        }

        // No valid rollout index → normal analysis
        return LevelLabel(evalLevel);
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    private static bool IsAnalysed(MoveRecord move) =>
        move.Analysis.MoveCount > 0 && move.Analysis.Evals.Length > 0
        && move.MoveError > -999.0;

    private static bool IsAnalysed(CubeRecord cube) =>
        (cube.Analysis.Level > 0 || cube.Analysis.LevelRequest > 0)
        && cube.ErrorCube > -999.0;

    private static int DiceToInt(int[] dice) =>
        dice.Length >= 2 ? dice[0] * 10 + dice[1] : 0;

    private static double BestCubeEquity(DoubleActionAnalysis a)
    {
        double nd = IsUsable(a.EquityNoDouble) ? a.EquityNoDouble : double.MinValue;
        double dt = IsUsable(a.EquityDoubleTake) ? a.EquityDoubleTake : double.MinValue;
        double dd = IsUsable(a.EquityDoubleDrop) ? a.EquityDoubleDrop : double.MinValue;
        double best = Math.Max(nd, Math.Max(dt, dd));
        return best == double.MinValue ? 0 : best;
    }

    private static bool IsUsable(float v) =>
        !float.IsNaN(v) && !float.IsInfinity(v) && v != 0f && v > -999f;

    /// <summary>
    /// Maps XG analysis level codes to display labels.
    /// PLAYERLEVEL TABLE from xg_format.pas:
    ///   0=1-ply, 1=2-ply, 2=3-ply, 12=3-ply red
    ///   3=4-ply, 4=5-ply, 5=6-ply, 6=7-ply
    ///   100=Rollout, 1000=XGRoller, 1001=XGRoller+, 1002=XGRoller++
    ///   999=Opening Book V1, 998=Opening Book V2
    /// </summary>
    private static string LevelLabel(short level) => level switch
    {
        0 => "1-ply",
        1 => "2-ply",
        2 => "3-ply",
        12 => "3-ply red",
        3 => "4-ply",
        4 => "5-ply",
        5 => "6-ply",
        6 => "7-ply",
        100 => "Rollout",
        1000 => "XG Roller",
        1001 => "XG Roller+",
        1002 => "XG Roller++",
        998 => "Book V1",
        999 => "Book V2",
        _ => $"level-{level}",
    };

    // -----------------------------------------------------------------------
    //  Match context tracker
    // -----------------------------------------------------------------------

    private sealed class MatchContext
    {
        public string MatchId { get; }
        public int MatchLength { get; private set; }
        public int Score1 { get; private set; }
        public int Score2 { get; private set; }
        public int CrawfordJacoby { get; private set; }
        public int CubeValue { get; private set; } = 1;
        public int CubePosition { get; private set; }
        public int GameNumber { get; private set; }
        public int MoveNumber { get; private set; }
        public int MaxCubeLimit { get; private set; } = 6;

        private string _player1 = "Player 1";
        private string _player2 = "Player 2";

        public MatchContext(List<SaveRecord> records, string matchId)
        {
            MatchId = matchId;
            foreach (var r in records)
            {
                if (r is MatchHeaderRecord hm)
                {
                    _player1 = hm.Player1;
                    _player2 = hm.Player2;
                    MatchLength = hm.MatchLength >= 99999 ? 0 : hm.MatchLength;
                    CrawfordJacoby = MatchLength == 0
                        ? (hm.Jacoby ? 1 : 0) + (hm.Beaver ? 2 : 0)
                        : 0;
                    MaxCubeLimit = hm.CubeLimit > 0 ? hm.CubeLimit : 6;
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
                    MoveNumber = 0;
                    Score1 = gh.Score1;
                    Score2 = gh.Score2;
                    if (MatchLength > 0 && gh.CrawfordApplies)
                        CrawfordJacoby = 1;
                    CubeValue = 1;
                    CubePosition = 0;
                    break;

                case MoveRecord mv:
                    MoveNumber++;
                    // CubeA encoding: 0=centered/1, ±N means cube=2^N owned by ± side
                    CubeValue = mv.CubeValue == 0 ? 1 : (int)Math.Pow(2, Math.Abs(mv.CubeValue));
                    CubePosition = mv.CubeValue == 0 ? 0 : (mv.CubeValue > 0 ? 1 : -1);
                    break;

                case CubeRecord cb:
                    // Only count as a move if the player actually doubled.
                    // A no-double decision (Doubled == 0) is a voluntary
                    // pass and does not advance the move counter.
                    if (cb.Doubled == 1)
                    {
                        MoveNumber++;
                        if (cb.Taken == 1)
                        {
                            // After a take the cube doubles; CubeB is pre-double encoded value
                            int preCube = cb.CubeValue == 0 ? 1 : (int)Math.Pow(2, Math.Abs(cb.CubeValue));
                            CubeValue = preCube * 2;
                            CubePosition = cb.ActivePlayer >= 0 ? 1 : -1;
                        }
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
                string crawford = CrawfordJacoby == 1 ? "C" : "";
                return $"{away1}a{away2}a{crawford}";
            }
        }
    }
}