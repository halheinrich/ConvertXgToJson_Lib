using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;

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

    /// <summary>
    /// Yields all decisions from a single already-parsed <see cref="XgFile"/>.
    /// </summary>
    /// <param name="file">The parsed XG file.</param>
    /// <param name="sourceFile">
    /// Originating file name including extension (e.g. "match.xg"), or null for
    /// stream/programmatic parses with no filename context. Copied verbatim onto
    /// every yielded <see cref="DecisionRow.SourceFile"/>.
    /// </param>
    /// <param name="state">
    /// Optional early-exit state. The caller sets flags after each yielded row;
    /// the iterator resets them at game boundaries. Pass null for no early-exit.
    /// </param>
    public static IEnumerable<DecisionRow> Iterate(
        XgFile file,
        string? sourceFile,
        XgIteratorState? state = null)
    {
        var context = new MatchContext(file.Records, sourceFile);

        foreach (var record in file.Records)
        {
            if (record is GameHeaderRecord gh)
            {
                context.Update(record); // must be before GameInfo so MatchLength is current

                if (state != null)
                {
                    state.AdvanceNextGame = false;

                    bool isMoney = context.MatchLength == 0;
                    state.GameInfo = new XgGameInfo
                    {
                        Away1 = isMoney ? 0 : context.MatchLength - gh.Score1,
                        Away2 = isMoney ? 0 : context.MatchLength - gh.Score2,
                        IsCrawfordGame = gh.CrawfordApplies,
                        IsStandardStart = BackgammonConstants.IsStandardOpeningPosition(gh.InitialPosition),
                    };
                }
                continue;
            }

            // Always update context — headers must be processed even when skipping
            // so that scores, game number, and cube state stay correct.
            context.Update(record);

            // Skip non-header records when a skip flag is active.
            if (state?.AdvanceNextGame == true || state?.AdvanceNextMatch == true)
                continue;

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

    /// <summary>
    /// Yields a <see cref="BgDecisionData"/> for every analysed checker-play and
    /// cube decision in <paramref name="file"/>. Analogous to <see cref="Iterate"/>
    /// but produces diagram data directly from the raw parse records rather than
    /// converting from <see cref="DecisionRow"/>.
    /// </summary>
    /// <param name="file">The parsed XG file.</param>
    /// <param name="sourceFile">
    /// Originating file name including extension (e.g. "match.xg"), or null for
    /// stream/programmatic parses with no filename context. Copied verbatim onto
    /// every yielded <see cref="DescriptiveData.SourceFile"/>.
    /// </param>
    /// <param name="state">
    /// Optional early-exit state. Behaves identically to <see cref="Iterate"/>.
    /// </param>
    public static IEnumerable<BgDecisionData> IterateDiagramRequests(
        XgFile file,
        string? sourceFile,
        XgIteratorState? state = null)
    {
        var context = new MatchContext(file.Records, sourceFile);

        foreach (var record in file.Records)
        {
            if (record is GameHeaderRecord gh)
            {
                context.Update(record);

                if (state != null)
                {
                    state.AdvanceNextGame = false;

                    bool isMoney = context.MatchLength == 0;
                    state.GameInfo = new XgGameInfo
                    {
                        Away1 = isMoney ? 0 : context.MatchLength - gh.Score1,
                        Away2 = isMoney ? 0 : context.MatchLength - gh.Score2,
                        IsCrawfordGame = gh.CrawfordApplies,
                        IsStandardStart = BackgammonConstants.IsStandardOpeningPosition(gh.InitialPosition),
                    };
                }
                continue;
            }

            context.Update(record);

            if (state?.AdvanceNextGame == true || state?.AdvanceNextMatch == true)
                continue;

            if (record is MoveRecord move && IsAnalysed(move))
            {
                var req = BuildMoveDiagramRequest(move, context, file.Rollouts);
                if (req != null) yield return req;
            }
            else if (record is CubeRecord cube && IsAnalysed(cube))
            {
                foreach (var req in BuildCubeDiagramRequests(cube, context, file.Rollouts))
                    yield return req;
            }
        }
    }

    // -----------------------------------------------------------------------
    //  Public API — directories
    // -----------------------------------------------------------------------

    /// <summary>
    /// Yields all decisions from every .xg file in <paramref name="xgDir"/>.
    /// </summary>
    /// <param name="xgDir">Directory containing .xg files.</param>
    /// <param name="state">
    /// Optional early-exit state. <see cref="XgIteratorState.AdvanceNextMatch"/>
    /// and <see cref="XgIteratorState.MatchInfo"/> are reset at the start of each
    /// file; <see cref="XgIteratorState.AdvanceNextGame"/> and
    /// <see cref="XgIteratorState.GameInfo"/> are reset at each game boundary
    /// inside <see cref="Iterate"/>.
    /// </param>
    public static IEnumerable<DecisionRow> IterateXgDirectory(
        string xgDir,
        XgIteratorState? state = null)
    {
        foreach (var path in Directory.EnumerateFiles(xgDir, "*.xg"))
        {
            if (state != null)
            {
                state.AdvanceNextMatch = false;
                state.AdvanceNextGame = false;
                state.MatchInfo = null;
                state.GameInfo = null;
            }

            XgFile file;
            try { file = XgFileReader.ReadFile(path); }
            catch { continue; }

            if (state != null)
                state.MatchInfo = ExtractMatchInfo(file);

            string sourceFile = Path.GetFileName(path);
            foreach (var row in Iterate(file, sourceFile, state))
                yield return row;
        }
    }

    /// <summary>
    /// Yields all decisions from every .json file in <paramref name="jsonDir"/>.
    /// </summary>
    /// <param name="jsonDir">Directory containing .json files.</param>
    /// <param name="state">Optional early-exit state. See <see cref="IterateXgDirectory"/>.</param>
    public static IEnumerable<DecisionRow> IterateJsonDirectory(
        string jsonDir,
        XgIteratorState? state = null)
    {
        foreach (var path in Directory.EnumerateFiles(jsonDir, "*.json"))
        {
            if (state != null)
            {
                state.AdvanceNextMatch = false;
                state.AdvanceNextGame = false;
                state.MatchInfo = null;
                state.GameInfo = null;
            }

            XgFile file;
            try { file = XgFileReader.ReadJson(path); }
            catch { continue; }

            if (state != null)
                state.MatchInfo = ExtractMatchInfo(file);

            string sourceFile = Path.GetFileName(path);
            foreach (var row in Iterate(file, sourceFile, state))
                yield return row;
        }
    }

    // -----------------------------------------------------------------------
    //  Move record — DecisionRow
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

        var xgidPosition = move.ActivePlayer >= 0
            ? move.InitialPosition
            : FlipPosition(move.InitialPosition);

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

        int[] board = ToBoard(move.InitialPosition.Points, move.ActivePlayer);
        int userPlayIndex = FindUserPlayIndex(analysis, move.FinalPosition);
        var (afterBest, afterPlayer) = ComputeMoveAfterBoards(board, analysis, userPlayIndex);

        return new DecisionRow
        {
            Xgid = xgid,
            Error = move.MoveError > -999.0 ? Math.Abs(move.MoveError) : 0.0,
            OnRollNeeds = ctx.NeedsFor(move.ActivePlayer),
            OpponentNeeds = ctx.NeedsFor(-move.ActivePlayer),
            IsCrawford = ctx.CrawfordJacoby == 1,
            MatchLength = ctx.MatchLength,
            Player = ctx.PlayerName(move.ActivePlayer),
            SourceFile = ctx.SourceFile,
            Game = ctx.GameNumber,
            MoveNum = ctx.MoveNumber,
            Roll = dice,
            AnalysisDepth = depth,
            Equity = bestEval.Equity,
            Board = board,
            AfterBestBoard = afterBest,
            AfterPlayerBoard = afterPlayer,
        };
    }

    // -----------------------------------------------------------------------
    //  Move record — DiagramRequest
    // -----------------------------------------------------------------------

    private static BgDecisionData? BuildMoveDiagramRequest(MoveRecord move, MatchContext ctx, List<RolloutContext> rollouts)
    {
        var analysis = move.Analysis;
        if (analysis.MoveCount == 0 || analysis.Evals.Length == 0)
            return null;
        int dice = DiceToInt(move.Dice);
        if (dice == 0) return null;

        string depth = ResolveDepth(
            evalLevel: analysis.EvalLevels.Length > 0 ? analysis.EvalLevels[0].Level : (short)0,
            rolloutIndices: move.RolloutIndices,
            rollouts: rollouts);

        int[] board = ToBoard(move.InitialPosition.Points, move.ActivePlayer);
        ComputePipCounts(board, out int onRollPips, out int opponentPips);

        int userPlayIndex = FindUserPlayIndex(analysis, move.FinalPosition);
        var (afterBest, afterPlayer) = ComputeMoveAfterBoards(board, analysis, userPlayIndex);

        var plays = new List<PlayCandidate>(analysis.MoveCount);
        double bestEquity = analysis.Evals.Length > 0 ? analysis.Evals[0].Equity : 0.0;
        for (int i = 0; i < analysis.MoveCount && i < analysis.Evals.Length; i++)
        {
            double equity = analysis.Evals[i].Equity;
            var eval = analysis.Evals[i];
            sbyte[] candidateMoves = i < analysis.Moves.Length ? analysis.Moves[i] : [];
            // Each candidate gets its own scratch board so hit-tracking in
            // one candidate doesn't leak into the next.
            int[] scratchBoard = (int[])board.Clone();
            plays.Add(new PlayCandidate
            {
                MoveNotation = MoveNotationFormatter.Format(candidateMoves, scratchBoard),
                Equity = equity,
                EquityLoss = i == 0 ? null : bestEquity - equity,
                IsUserPlay = i == userPlayIndex,
                WinPct = eval.WinSingle,
                WinGammonPct = eval.WinGammon,
                WinBgPct = eval.WinBackgammon,
                LosePct = eval.LoseSingle,
                LoseGammonPct = eval.LoseGammon,
                LoseBgPct = eval.LoseBackgammon,
            });
        }

        return new BgDecisionData
        {
            Position = new PositionData
            {
                Mop = board,
                OnRollNeeds = ctx.NeedsFor(move.ActivePlayer),
                OpponentNeeds = ctx.NeedsFor(-move.ActivePlayer),
                OnRollPipCount = onRollPips,
                OpponentPipCount = opponentPips,
                CubeSize = ctx.CubeValue,
                CubeOwner = CubeOwnerFor(ctx.CubePosition, move.ActivePlayer),
            },
            Decision = new DecisionData
            {
                IsCube = false,
                Dice = [move.Dice[0], move.Dice[1]],
                BestPlayIndex = 0,
                UserPlayIndex = userPlayIndex,
                UserPlayError = move.MoveError > -999.0 ? Math.Abs(move.MoveError) : (double?)null,
                Plays = plays,
                AnalysisDepths = [new AnalysisDepthEntry { Label = depth }],
            },
            Descriptive = new DescriptiveData
            {
                MatchLength = ctx.MatchLength,
                OnRollName = ctx.PlayerName(move.ActivePlayer),
                OpponentName = ctx.PlayerName(-move.ActivePlayer),
                SourceFile = ctx.SourceFile,
            },
            Outcome = new PlayOutcomeData
            {
                AfterBestBoard = afterBest,
                AfterPlayerBoard = afterPlayer,
            },
        };
    }

    // -----------------------------------------------------------------------
    //  Cube record — DecisionRow
    // -----------------------------------------------------------------------

    private static IEnumerable<DecisionRow> BuildCubeRows(CubeRecord cube, MatchContext ctx, List<RolloutContext> rollouts)
    {
        var analysis = cube.Analysis;

        string depth = ResolveDepth(
            evalLevel: analysis.LevelRequest,
            rolloutIndices: [cube.RolloutIndex],
            rollouts: rollouts);

        int cubeActual = CubeValueActual(cube.CubeValue);
        int cubePos = cube.CubeValue == 0 ? 0 : (cube.CubeValue > 0 ? 1 : -1);

        var xgidPosition = cube.ActivePlayer >= 0
            ? cube.Position
            : FlipPosition(cube.Position);

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

        yield return new DecisionRow
        {
            Xgid = xgid,
            Error = cube.ErrorCube > -999.0 ? Math.Abs(cube.ErrorCube) : 0.0,
            OnRollNeeds = ctx.NeedsFor(cube.ActivePlayer),
            OpponentNeeds = ctx.NeedsFor(-cube.ActivePlayer),
            IsCrawford = ctx.CrawfordJacoby == 1,
            MatchLength = ctx.MatchLength,
            Player = ctx.PlayerName(cube.ActivePlayer),
            SourceFile = ctx.SourceFile,
            Game = ctx.GameNumber,
            MoveNum = ctx.MoveNumber + 1,
            Roll = 0,
            AnalysisDepth = depth,
            Equity = IsUsable(analysis.EquityNoDouble) ? analysis.EquityNoDouble : 0f,
            Board = board,
            // Cube decisions carry no play; the PlayOutcomeData contract requires
            // both after-boards empty. Explicit to document the producer intent.
            AfterBestBoard = [],
            AfterPlayerBoard = [],
        };
    }

    // -----------------------------------------------------------------------
    //  Cube record — DiagramRequest
    // -----------------------------------------------------------------------

    private static IEnumerable<BgDecisionData> BuildCubeDiagramRequests(CubeRecord cube, MatchContext ctx, List<RolloutContext> rollouts)
    {
        var analysis = cube.Analysis;

        string depth = ResolveDepth(
            evalLevel: analysis.LevelRequest,
            rolloutIndices: [cube.RolloutIndex],
            rollouts: rollouts);

        int cubePos = cube.CubeValue == 0 ? 0 : (cube.CubeValue > 0 ? 1 : -1);

        int[] board = ToBoard(cube.Position.Points, cube.ActivePlayer);
        ComputePipCounts(board, out int onRollPips, out int opponentPips);

        var depthEntries = new List<AnalysisDepthEntry> { new() { Label = depth } };

        // Doubler row
        yield return new BgDecisionData
        {
            Position = new PositionData
            {
                Mop = board,
                OnRollNeeds = ctx.NeedsFor(cube.ActivePlayer),
                OpponentNeeds = ctx.NeedsFor(-cube.ActivePlayer),
                OnRollPipCount = onRollPips,
                OpponentPipCount = opponentPips,
                CubeSize = CubeValueActual(cube.CubeValue),
                CubeOwner = CubeOwnerFor(cubePos, cube.ActivePlayer),
            },
            Decision = new DecisionData
            {
                IsCube = true,
                Dice = [0, 0],
                NoDoubleEquity = IsUsable(analysis.EquityNoDouble) ? analysis.EquityNoDouble : 0.0,
                DoubleTakeEquity = IsUsable(analysis.EquityDoubleTake) ? analysis.EquityDoubleTake : 0.0,
                WinPctAfterNoDouble = analysis.EvalNoDouble.WinSingle,
                GammonPctAfterNoDouble = analysis.EvalNoDouble.WinGammon,
                BgPctAfterNoDouble = analysis.EvalNoDouble.WinBackgammon,
                LosePctAfterNoDouble = analysis.EvalNoDouble.LoseSingle,
                LoseGammonPctAfterNoDouble = analysis.EvalNoDouble.LoseGammon,
                LoseBgPctAfterNoDouble = analysis.EvalNoDouble.LoseBackgammon,
                WinPctAfterDoubleTake = analysis.EvalDoubleTake.WinSingle,
                GammonPctAfterDoubleTake = analysis.EvalDoubleTake.WinGammon,
                BgPctAfterDoubleTake = analysis.EvalDoubleTake.WinBackgammon,
                LosePctAfterDoubleTake = analysis.EvalDoubleTake.LoseSingle,
                LoseGammonPctAfterDoubleTake = analysis.EvalDoubleTake.LoseGammon,
                LoseBgPctAfterDoubleTake = analysis.EvalDoubleTake.LoseBackgammon,
                AnalysisDepths = depthEntries,
                UserDoubleError = cube.ErrorCube > -999.0 ? Math.Abs(cube.ErrorCube) : (double?)null,
                UserTakeError = (cube.Doubled == 1 && cube.ErrorTake > -999.0) ? Math.Abs(cube.ErrorTake) : (double?)null,
            },
            Descriptive = new DescriptiveData
            {
                MatchLength = ctx.MatchLength,
                OnRollName = ctx.PlayerName(cube.ActivePlayer),
                OpponentName = ctx.PlayerName(-cube.ActivePlayer),
                SourceFile = ctx.SourceFile,
            },
            // Cube decisions carry no play; PlayOutcomeData contract requires
            // both after-boards empty. Explicit for producer intent.
            Outcome = new PlayOutcomeData
            {
                AfterBestBoard = [],
                AfterPlayerBoard = [],
            },
        };
    }

    // -----------------------------------------------------------------------
    //  Board helpers
    // -----------------------------------------------------------------------

    private static int[] ToBoard(sbyte[] points, int activePlayer)
    {
        if (activePlayer >= 0)
        {
            var board = new int[26];
            for (int i = 0; i < 26; i++)
                board[i] = points[i];
            return board;
        }
        else
        {
            var board = new int[26];
            for (int i = 0; i < 26; i++)
                board[i] = -points[25 - i];
            return board;
        }
    }

    private static PositionEngine FlipPosition(PositionEngine pos)
    {
        var flipped = new sbyte[26];
        for (int i = 0; i < 26; i++)
            flipped[i] = (sbyte)-pos.Points[25 - i];
        return new PositionEngine { Points = flipped };
    }

    /// <summary>
    /// Computes pip counts from a board array already normalised to on-roll perspective.
    /// Points 1–24 only; bar checkers do not contribute to pip count.
    /// </summary>
    private static void ComputePipCounts(int[] board, out int onRollPips, out int opponentPips)
    {
        int onRoll = 0, opponent = 0;
        for (int i = 1; i <= 24; i++)
        {
            int v = board[i];
            if (v > 0) onRoll += v * i;
            else if (v < 0) opponent += -v * (25 - i);
        }
        onRollPips = onRoll;
        opponentPips = opponent;
    }

    /// <summary>
    /// Returns the <see cref="CubeOwner"/> from the on-roll player's perspective.
    /// <paramref name="cubePosition"/> uses the raw XG sign convention (+1 = player1 owns,
    /// -1 = player2 owns, 0 = centred); <paramref name="activePlayer"/> is +1 or -1.
    /// </summary>
    private static CubeOwner CubeOwnerFor(int cubePosition, int activePlayer)
    {
        if (cubePosition == 0) return CubeOwner.Centered;
        return cubePosition == activePlayer ? CubeOwner.OnRoll : CubeOwner.Opponent;
    }

    /// <summary>
    /// Returns true if two <see cref="PositionEngine"/> instances have identical Points arrays.
    /// </summary>
    private static bool PositionsEqual(PositionEngine a, PositionEngine b)
    {
        for (int i = 0; i < 26; i++)
            if (a.Points[i] != b.Points[i]) return false;
        return true;
    }

    /// <summary>
    /// Identifies which analysis candidate matches the move the player actually
    /// made. Returns <c>-1</c> when the played position is not in the analysed
    /// candidate set (e.g. the player chose a move XG didn't rank in its top-N).
    /// </summary>
    private static int FindUserPlayIndex(BestMoveAnalysis analysis, PositionEngine finalPosition)
    {
        for (int i = 0; i < analysis.PositionsPlayed.Length && i < analysis.MoveCount; i++)
            if (PositionsEqual(analysis.PositionsPlayed[i], finalPosition)) return i;
        return -1;
    }

    /// <summary>
    /// Computes the after-boards for a checker-play decision.
    ///
    /// <para>
    /// When <paramref name="userPlayIndex"/> is a valid index into
    /// <see cref="BestMoveAnalysis.Moves"/>, both boards are computed via
    /// <see cref="AfterBoardBuilder.ComputeAfterBoard"/>: best from
    /// <c>Moves[0]</c>, player from <c>Moves[userPlayIndex]</c>.
    /// </para>
    ///
    /// <para>
    /// Otherwise — when the player's actual play is not in the analysed
    /// candidate set, or XG did not emit a move encoding for that index —
    /// both boards are returned empty. Per the
    /// <see cref="PlayOutcomeData"/> contract this makes the decision
    /// invisible to board-based play-type filters, matching the handling of
    /// cube decisions.
    /// </para>
    /// </summary>
    private static (IReadOnlyList<int> afterBest, IReadOnlyList<int> afterPlayer) ComputeMoveAfterBoards(
        int[] priorBoard, BestMoveAnalysis analysis, int userPlayIndex)
    {
        if (userPlayIndex < 0 || userPlayIndex >= analysis.Moves.Length)
            return ([], []);

        return (
            AfterBoardBuilder.ComputeAfterBoard(priorBoard, analysis.Moves[0]),
            AfterBoardBuilder.ComputeAfterBoard(priorBoard, analysis.Moves[userPlayIndex])
        );
    }

    // -----------------------------------------------------------------------
    //  Match info helper
    // -----------------------------------------------------------------------

    public static XgMatchInfo ExtractMatchInfo(XgFile file)
    {
        foreach (var r in file.Records)
        {
            if (r is MatchHeaderRecord hm)
            {
                return new XgMatchInfo
                {
                    Player1 = hm.Player1,
                    Player2 = hm.Player2,
                    MatchLength = hm.MatchLength >= 99999 ? 0 : hm.MatchLength,
                };
            }
        }
        return new XgMatchInfo();
    }

    // -----------------------------------------------------------------------
    //  Depth resolution
    // -----------------------------------------------------------------------

    private static string ResolveDepth(
        short evalLevel,
        int[] rolloutIndices,
        List<RolloutContext> rollouts)
    {
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
        return LevelLabel(evalLevel);
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    private static bool IsAnalysed(MoveRecord move) =>
        move.Analysis.MoveCount > 0 && move.Analysis.Evals.Length > 0;

    // LevelRequest reflects what the user asked XG to compute, not what XG ran:
    // an .xgp closed before the analysis completed has Level == -100 (XG's
    // "queued, never ran" sentinel) but a non-zero LevelRequest. Gate on Level.
    private static bool IsAnalysed(CubeRecord cube) =>
        cube.Analysis.Level > 0;

    private static int DiceToInt(int[] dice) =>
        dice.Length >= 2 ? dice[0] * 10 + dice[1] : 0;

    private static bool IsUsable(float v) =>
        !float.IsNaN(v) && !float.IsInfinity(v) && v > -999f;

    /// <summary>
    /// Converts a raw XG cube value (signed log2 encoding) to the actual cube size.
    /// Raw value 0 means the cube is centred at 1. Positive/negative values encode
    /// which player owns the cube; the magnitude is log2 of the cube size.
    /// </summary>
    internal static int CubeValueActual(int raw) =>
        raw == 0 ? 1 : (int)Math.Pow(2, Math.Abs(raw));

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

}