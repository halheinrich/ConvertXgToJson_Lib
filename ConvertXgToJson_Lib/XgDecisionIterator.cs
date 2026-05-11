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
    /// Optional read-only observer. The iterator populates
    /// <see cref="XgIteratorState.MatchInfo"/> from the file's
    /// <see cref="MatchHeaderRecord"/> before the first yield, then
    /// repopulates <see cref="XgIteratorState.GameInfo"/> at each
    /// <see cref="GameHeaderRecord"/>. Inspect these for per-row context;
    /// pass null when not needed.
    /// </param>
    /// <param name="callbacks">
    /// Optional skip predicates. See <see cref="XgIteratorCallbacks"/> for
    /// the boundaries at which each predicate fires.
    /// </param>
    public static IEnumerable<DecisionRow> Iterate(
        XgFile file,
        string? sourceFile,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null)
    {
        var context = new MatchContext(file.Records, sourceFile);

        var matchInfo = ExtractMatchInfo(file);
        if (state != null)
        {
            state.MatchInfo = matchInfo;
            state.GameInfo = null;
        }

        if (callbacks?.SkipMatchAt?.Invoke(matchInfo) == true)
            yield break;

        bool skipCurrentGame = false;

        foreach (var record in file.Records)
        {
            if (record is GameHeaderRecord gh)
            {
                context.Update(record); // must be before GameInfo so MatchLength is current

                bool isMoney = context.MatchLength == 0;
                var gameInfo = new XgGameInfo
                {
                    Away1 = isMoney ? 0 : context.MatchLength - gh.Score1,
                    Away2 = isMoney ? 0 : context.MatchLength - gh.Score2,
                    IsCrawfordGame = gh.CrawfordApplies,
                    IsStandardStart = context.IsStandardStart,
                };

                if (state != null)
                    state.GameInfo = gameInfo;

                skipCurrentGame = callbacks?.SkipGameAt?.Invoke(gameInfo) == true;
                continue;
            }

            // Always update context — headers must be processed even when skipping
            // so that scores, game number, and cube state stay correct.
            context.Update(record);

            if (skipCurrentGame)
                continue;

            if (record is MoveRecord move && IsAnalysed(move) && !IsSentinelOnlyAnalysis(move.Analysis))
            {
                var row = BuildMoveRow(move, context, file.Rollouts);
                if (row != null)
                {
                    yield return row;
                    if (callbacks?.StopMatchAfter?.Invoke(row) == true)
                        yield break;
                    if (callbacks?.StopGameAfter?.Invoke(row) == true)
                        skipCurrentGame = true;
                }
            }
            else if (record is CubeRecord cube && IsAnalysed(cube))
            {
                foreach (var row in BuildCubeRows(cube, context, file.Rollouts))
                {
                    yield return row;
                    if (callbacks?.StopMatchAfter?.Invoke(row) == true)
                        yield break;
                    if (callbacks?.StopGameAfter?.Invoke(row) == true)
                    {
                        skipCurrentGame = true;
                        break;
                    }
                }
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
    /// Optional read-only observer. Behaves identically to
    /// <see cref="Iterate"/>: <see cref="XgIteratorState.MatchInfo"/> is
    /// populated before the first yield, <see cref="XgIteratorState.GameInfo"/>
    /// at each <see cref="GameHeaderRecord"/>.
    /// </param>
    /// <param name="callbacks">
    /// Optional skip predicates. See <see cref="XgIteratorCallbacks"/>.
    /// </param>
    public static IEnumerable<BgDecisionData> IterateDiagramRequests(
        XgFile file,
        string? sourceFile,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null)
    {
        var context = new MatchContext(file.Records, sourceFile);

        var matchInfo = ExtractMatchInfo(file);
        if (state != null)
        {
            state.MatchInfo = matchInfo;
            state.GameInfo = null;
        }

        if (callbacks?.SkipMatchAt?.Invoke(matchInfo) == true)
            yield break;

        bool skipCurrentGame = false;

        foreach (var record in file.Records)
        {
            if (record is GameHeaderRecord gh)
            {
                context.Update(record);

                bool isMoney = context.MatchLength == 0;
                var gameInfo = new XgGameInfo
                {
                    Away1 = isMoney ? 0 : context.MatchLength - gh.Score1,
                    Away2 = isMoney ? 0 : context.MatchLength - gh.Score2,
                    IsCrawfordGame = gh.CrawfordApplies,
                    IsStandardStart = context.IsStandardStart,
                };

                if (state != null)
                    state.GameInfo = gameInfo;

                skipCurrentGame = callbacks?.SkipGameAt?.Invoke(gameInfo) == true;
                continue;
            }

            context.Update(record);

            if (skipCurrentGame)
                continue;

            if (record is MoveRecord move && IsAnalysed(move) && !IsSentinelOnlyAnalysis(move.Analysis))
            {
                var req = BuildMoveDiagramRequest(move, context, file.Rollouts);
                if (req != null)
                {
                    yield return req;
                    if (callbacks?.StopMatchAfter?.Invoke(req) == true)
                        yield break;
                    if (callbacks?.StopGameAfter?.Invoke(req) == true)
                        skipCurrentGame = true;
                }
            }
            else if (record is CubeRecord cube && IsAnalysed(cube))
            {
                foreach (var req in BuildCubeDiagramRequests(cube, context, file.Rollouts))
                {
                    yield return req;
                    if (callbacks?.StopMatchAfter?.Invoke(req) == true)
                        yield break;
                    if (callbacks?.StopGameAfter?.Invoke(req) == true)
                    {
                        skipCurrentGame = true;
                        break;
                    }
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    //  Public API — directories
    // -----------------------------------------------------------------------

    /// <summary>
    /// Yields all decisions from every XG-format file in
    /// <paramref name="xgDir"/> — both <c>.xg</c> match files and
    /// <c>.xgp</c> position files. <see cref="XgFileReader.ReadFile"/>
    /// detects format from file content, so per-file handling is uniform.
    /// </summary>
    /// <param name="xgDir">Directory containing .xg and/or .xgp files.</param>
    /// <param name="state">Optional read-only observer. See <see cref="Iterate"/>
    /// — per-file <see cref="XgIteratorState.MatchInfo"/> repopulation happens
    /// inside <c>Iterate</c> at each file boundary.</param>
    /// <param name="callbacks">Optional skip predicates. Re-evaluated fresh per
    /// file — predicates are stateless from the producer's perspective, so a
    /// match-skip in one file has no effect on the next.</param>
    public static IEnumerable<DecisionRow> IterateXgDirectory(
        string xgDir,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null)
    {
        foreach (var path in EnumerateXgFormatFiles(xgDir))
        {
            XgFile file;
            try { file = XgFileReader.ReadFile(path); }
            catch { continue; }

            string sourceFile = Path.GetFileName(path);
            foreach (var row in Iterate(file, sourceFile, state, callbacks))
                yield return row;
        }
    }

    /// <summary>
    /// Yields all decisions from every .json file in <paramref name="jsonDir"/>.
    /// </summary>
    /// <param name="jsonDir">Directory containing .json files.</param>
    /// <param name="state">Optional read-only observer. See <see cref="Iterate"/>.</param>
    /// <param name="callbacks">Optional skip predicates. See <see cref="IterateXgDirectory"/>.</param>
    public static IEnumerable<DecisionRow> IterateJsonDirectory(
        string jsonDir,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null)
    {
        foreach (var path in Directory.EnumerateFiles(jsonDir, "*.json"))
        {
            XgFile file;
            try { file = XgFileReader.ReadJson(path); }
            catch { continue; }

            string sourceFile = Path.GetFileName(path);
            foreach (var row in Iterate(file, sourceFile, state, callbacks))
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

        // XG's native rank 0 is not always the highest-equity candidate;
        // CSV Equity and the depth label that describes it must both key
        // off the best-by-equity index, not Evals[0]. See
        // FindBestByEquityIndex for the convention rationale.
        int bestIdx = FindBestByEquityIndex(analysis);
        var bestEval = analysis.Evals[bestIdx];
        int dice = DiceToInt(move.Dice);

        string depth = ResolveDepth(
            evalLevel: bestIdx < analysis.EvalLevels.Length ? analysis.EvalLevels[bestIdx].Level : (short)0,
            rolloutIndex: bestIdx < move.RolloutIndices.Length ? move.RolloutIndices[bestIdx] : -1,
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
            crawfordJacoby: ctx.XgidCrawfordJacobyField,
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
            IsCrawford = ctx.IsCrawford,
            MatchLength = ctx.MatchLength,
            Player = ctx.PlayerName(move.ActivePlayer),
            SourceFile = ctx.SourceFile,
            Game = ctx.GameNumber,
            MoveNumber = ctx.MoveNumber,
            IsStandardStart = ctx.IsStandardStart,
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

        int[] board = ToBoard(move.InitialPosition.Points, move.ActivePlayer);
        ComputePipCounts(board, out int onRollPips, out int opponentPips);

        int rawUserPlayIndex = FindUserPlayIndex(analysis, move.FinalPosition);
        var (afterBest, afterPlayer) = ComputeMoveAfterBoards(board, analysis, rawUserPlayIndex);

        // XG stores candidates in its native ranking order, which is not
        // strict equity-descending: a rank-2 candidate can have higher equity
        // than rank-0. Sort by equity so Plays[0] is truly best, EquityLoss
        // (= bestEquity - candidateEquity) is non-negative throughout, and
        // the renderer's equity column reads monotonically. OrderByDescending
        // is stable, preserving XG's order for ties.
        int n = Math.Min(analysis.MoveCount, analysis.Evals.Length);
        int[] sortedIdx = Enumerable.Range(0, n)
            .OrderByDescending(i => analysis.Evals[i].Equity)
            .ToArray();

        // Sorted Plays means UserPlayIndex (an index into Plays per the
        // BgDataTypes contract) must be re-mapped from the XG-native index
        // FindUserPlayIndex returned.
        int userPlayIndex = -1;
        if (rawUserPlayIndex >= 0)
        {
            for (int k = 0; k < sortedIdx.Length; k++)
                if (sortedIdx[k] == rawUserPlayIndex) { userPlayIndex = k; break; }
        }

        var plays = new List<PlayCandidate>(n);
        double bestEquity = n > 0 ? analysis.Evals[sortedIdx[0]].Equity : 0.0;
        for (int k = 0; k < n; k++)
        {
            int i = sortedIdx[k];
            double equity = analysis.Evals[i].Equity;
            var eval = analysis.Evals[i];
            sbyte[] candidateMoves = i < analysis.Moves.Length ? analysis.Moves[i] : [];
            short evalLevel = i < analysis.EvalLevels.Length
                ? analysis.EvalLevels[i].Level
                : (short)0;
            var (candidateDepth, candidateDepthAbbrev, candidateDepthRank) = ResolveDepthInfo(
                evalLevel: evalLevel,
                rolloutIndex: i < move.RolloutIndices.Length ? move.RolloutIndices[i] : -1,
                rollouts: rollouts);
            // Each candidate gets its own scratch board so hit-tracking in
            // one candidate doesn't leak into the next. Translate once and
            // share the resulting Play between MoveNotation (rendered form)
            // and Play (structural form) so the two views can never disagree.
            int[] scratchBoard = (int[])board.Clone();
            Play candidatePlay = XgMoveTranslator.Translate(candidateMoves, scratchBoard);
            plays.Add(new PlayCandidate
            {
                MoveNotation = BgMoveGen.MoveNotationFormatter.Format(candidatePlay),
                Play = candidatePlay,
                Depth = candidateDepth,
                DepthAbbreviation = candidateDepthAbbrev,
                DepthRank = candidateDepthRank,
                Equity = equity,
                EquityLoss = bestEquity - equity,
                IsUserPlay = k == userPlayIndex,
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
                IsCrawford = ctx.IsCrawford,
            },
            Decision = new DecisionData
            {
                IsCube = false,
                Dice = [move.Dice[0], move.Dice[1]],
                BestPlayIndex = 0,
                UserPlayIndex = userPlayIndex,
                UserPlayError = move.MoveError > -999.0 ? Math.Abs(move.MoveError) : (double?)null,
                Plays = plays,
            },
            Descriptive = new DescriptiveData
            {
                MatchLength = ctx.MatchLength,
                OnRollName = ctx.PlayerName(move.ActivePlayer),
                OpponentName = ctx.PlayerName(-move.ActivePlayer),
                SourceFile = ctx.SourceFile,
                MoveNumber = ctx.MoveNumber,
                IsStandardStart = ctx.IsStandardStart,
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
            rolloutIndex: cube.RolloutIndex,
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
            crawfordJacoby: ctx.XgidCrawfordJacobyField,
            matchLength: ctx.MatchLength,
            maxCubeLog2: ctx.MaxCubeLimit);

        int[] board = ToBoard(cube.Position.Points, cube.ActivePlayer);

        yield return new DecisionRow
        {
            Xgid = xgid,
            Error = cube.ErrorCube > -999.0 ? Math.Abs(cube.ErrorCube) : 0.0,
            OnRollNeeds = ctx.NeedsFor(cube.ActivePlayer),
            OpponentNeeds = ctx.NeedsFor(-cube.ActivePlayer),
            IsCrawford = ctx.IsCrawford,
            MatchLength = ctx.MatchLength,
            Player = ctx.PlayerName(cube.ActivePlayer),
            SourceFile = ctx.SourceFile,
            Game = ctx.GameNumber,
            MoveNumber = ctx.MoveNumber + 1,
            IsStandardStart = ctx.IsStandardStart,
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

        var (depth, depthAbbrev, depthRank) = ResolveDepthInfo(
            evalLevel: analysis.LevelRequest,
            rolloutIndex: cube.RolloutIndex,
            rollouts: rollouts);

        int cubePos = cube.CubeValue == 0 ? 0 : (cube.CubeValue > 0 ? 1 : -1);

        int[] board = ToBoard(cube.Position.Points, cube.ActivePlayer);
        ComputePipCounts(board, out int onRollPips, out int opponentPips);

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
                IsCrawford = ctx.IsCrawford,
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
                CubeDepth = depth,
                CubeDepthAbbreviation = depthAbbrev,
                CubeDepthRank = depthRank,
                UserDoubleError = cube.ErrorCube > -999.0 ? Math.Abs(cube.ErrorCube) : (double?)null,
                UserTakeError = (cube.Doubled == 1 && cube.ErrorTake > -999.0) ? Math.Abs(cube.ErrorTake) : (double?)null,
            },
            Descriptive = new DescriptiveData
            {
                MatchLength = ctx.MatchLength,
                OnRollName = ctx.PlayerName(cube.ActivePlayer),
                OpponentName = ctx.PlayerName(-cube.ActivePlayer),
                SourceFile = ctx.SourceFile,
                MoveNumber = ctx.MoveNumber + 1,
                IsStandardStart = ctx.IsStandardStart,
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
    /// Points 1–24 contribute their distance from each player's home; bar checkers
    /// contribute the maximum distance of 25 pips each. Per the on-roll-POV layout,
    /// <c>board[25]</c> holds the on-roll player's bar (positive entries) and
    /// <c>board[0]</c> holds the opponent's bar (negative entries).
    /// </summary>
    internal static void ComputePipCounts(int[] board, out int onRollPips, out int opponentPips)
    {
        int onRoll = 0, opponent = 0;
        for (int i = 1; i <= 24; i++)
        {
            int v = board[i];
            if (v > 0) onRoll += v * i;
            else if (v < 0) opponent += -v * (25 - i);
        }
        onRoll += board[25] * 25;
        opponent += -board[0] * 25;
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
    /// Returns the index of the highest-equity entry in
    /// <see cref="BestMoveAnalysis.Evals"/> — the canonical "best play"
    /// locator for this subproject.
    ///
    /// <para>
    /// XG stores candidates in its native ranking order, which is not
    /// always strict equity-descending: a rank-&gt;0 entry can have higher
    /// equity than rank 0. Rank-coupled data (<c>Evals</c>, <c>Moves</c>,
    /// <c>PositionsPlayed</c>, <c>EvalLevels</c>) shares the same index,
    /// so any producer surface that reports "best play" — CSV
    /// <c>DecisionRow.Equity</c>, <c>PlayOutcomeData.AfterBestBoard</c>,
    /// the top of sorted <c>BgDecisionData.Plays</c> — must resolve it
    /// through this helper rather than hard-coding <c>[0]</c>.
    /// </para>
    ///
    /// <para>
    /// Stable tie-break: on equal equity, the lower XG-native index
    /// wins, matching the semantics of a descending stable sort.
    /// Callers are expected to have already gated on <c>MoveCount == 0</c>
    /// or <c>Evals.Length == 0</c>; returns 0 on an empty analysis.
    /// </para>
    /// </summary>
    internal static int FindBestByEquityIndex(BestMoveAnalysis analysis)
    {
        int n = Math.Min(analysis.MoveCount, analysis.Evals.Length);
        if (n == 0) return 0;

        int bestIdx = 0;
        double bestEq = analysis.Evals[0].Equity;
        for (int i = 1; i < n; i++)
        {
            double eq = analysis.Evals[i].Equity;
            if (eq > bestEq)
            {
                bestIdx = i;
                bestEq = eq;
            }
        }
        return bestIdx;
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
    /// <c>Moves[FindBestByEquityIndex(analysis)]</c>, player from
    /// <c>Moves[userPlayIndex]</c>. The "best" index keys off the
    /// highest-equity candidate (see <see cref="FindBestByEquityIndex"/>),
    /// not XG-native rank 0 — those disagree on the subset of decisions
    /// where XG's stored ranking is not strict equity-descending.
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

        int bestIdx = FindBestByEquityIndex(analysis);

        return (
            AfterBoardBuilder.ComputeAfterBoard(priorBoard, analysis.Moves[bestIdx]),
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

    /// <summary>
    /// Resolves the analysis depth for a candidate into three parallel
    /// forms: the full human-readable label, a compact abbreviation for
    /// narrow cells, and an ordinal rank (higher = deeper / more
    /// rigorous). All three come from the same structured inputs — no
    /// string re-parsing — so the producer keeps ownership of depth
    /// semantics.
    ///
    /// <para>
    /// Rollout branch: when <paramref name="rolloutIndex"/> is a valid
    /// index into <paramref name="rollouts"/>, the rollout's inner ply
    /// level (<c>Level2</c>, falling back to <c>Level1</c>, then
    /// <c>LevelTrunc</c>) combines with <c>GamesRolled</c> to produce:
    /// <c>Label = "Rollout: {trials} trials. {inner ply label}"</c>,
    /// <c>Abbreviation = "{innerPly}p{trials}"</c> (e.g. "3p1296"),
    /// <c>Rank = 100 + innerPly</c>. The ply-label switch encodes ply as
    /// <c>short - 1</c>, so <c>Level*</c> value 2 is a 3-ply rollout.
    /// </para>
    ///
    /// <para>
    /// Non-rollout branch: returns <see cref="LevelLabel"/>,
    /// <see cref="LevelAbbreviation"/>, and <see cref="LevelRank"/> for
    /// <paramref name="evalLevel"/>. The rank ordering is:
    /// N-ply → N (1..7), XG Roller family → 20–22, Book V1/V2 → 0, any
    /// unrecognised level → 0. The edge case is a "Rollout" sentinel
    /// (<c>short 100</c>) without a matching rollout context, which ranks
    /// 100 — the same floor as a no-inner-ply rollout (e.g. truncated at
    /// level 0).
    /// </para>
    ///
    /// <para>
    /// Per-candidate scalar input: callers pass the rollout index keyed
    /// to a single candidate (move-path: <c>move.RolloutIndices[i]</c>;
    /// cube-path: <c>cube.RolloutIndex</c>). The earlier array-shaped
    /// signature iterated and returned on the first valid hit, which
    /// caused every candidate in a decision to inherit the rollout
    /// label whenever any candidate was rolled out.
    /// </para>
    /// </summary>
    internal static (string Label, string Abbreviation, int Rank) ResolveDepthInfo(
        short evalLevel,
        int rolloutIndex,
        List<RolloutContext> rollouts)
    {
        if (rolloutIndex >= 0 && rolloutIndex < rollouts.Count)
        {
            var ctx = rollouts[rolloutIndex];
            int plyLevel = ctx.Level2 > 0 ? ctx.Level2
                         : ctx.Level1 > 0 ? ctx.Level1
                         : ctx.LevelTrunc;
            int innerPly = plyLevel + 1;
            string label = $"Rollout: {ctx.GamesRolled} trials. {LevelLabel((short)plyLevel)}";
            string abbrev = $"{innerPly}p{ctx.GamesRolled}";
            int rank = 100 + innerPly;
            return (label, abbrev, rank);
        }
        return (LevelLabel(evalLevel), LevelAbbreviation(evalLevel), LevelRank(evalLevel));
    }

    /// <summary>
    /// Thin wrapper returning only the label form of
    /// <see cref="ResolveDepthInfo"/>, for callers that don't need the
    /// abbreviation or rank — e.g. <see cref="DecisionRow.AnalysisDepth"/>
    /// on the CSV path.
    /// </summary>
    internal static string ResolveDepth(
        short evalLevel,
        int rolloutIndex,
        List<RolloutContext> rollouts)
        => ResolveDepthInfo(evalLevel, rolloutIndex, rollouts).Label;

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Enumerates all XG-format files in a directory: both <c>*.xg</c>
    /// (match files) and <c>*.xgp</c> (position files). Order is .xg
    /// first then .xgp; within each extension, filesystem order
    /// (non-deterministic per OS). Two separate enumerations rather than
    /// a <c>*.xg*</c> glob — the broader pattern would also match
    /// hypothetical .xgz, .xgr, etc. files, which we don't assume are
    /// XG-format.
    /// </summary>
    private static IEnumerable<string> EnumerateXgFormatFiles(string xgDir) =>
        Directory.EnumerateFiles(xgDir, "*.xg")
            .Concat(Directory.EnumerateFiles(xgDir, "*.xgp"));

    private static bool IsAnalysed(MoveRecord move) =>
        move.Analysis.MoveCount > 0 && move.Analysis.Evals.Length > 0;

    /// <summary>
    /// Returns <c>true</c> when any candidate in <paramref name="analysis"/>'s
    /// <c>Moves</c> array starts with a known XG non-play sentinel pair. Used
    /// by <see cref="Iterate"/> and <see cref="IterateDiagramRequests"/> to
    /// skip these decisions before they reach
    /// <see cref="Parsing.AfterBoardBuilder.ComputeAfterBoard"/> or
    /// <see cref="XgMoveTranslator.Translate"/>: feeding either leaf the
    /// sentinel encoding has historically produced an
    /// <see cref="IndexOutOfRangeException"/> (the <c>(-100, -100)</c>
    /// pattern) or a "1/1" notation glitch (the <c>(0, 0)</c> pattern).
    ///
    /// <para>
    /// Two known sentinel pairs:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>(-100, -100)</c> — XG's <b>illegal-play workaround</b>. When
    ///     the recorded play in the source file is illegal, XG forces the
    ///     next position rather than refusing to load, and emits this
    ///     sentinel at the user-play slot in <c>Moves</c> while leaving
    ///     other candidates as real legal plays.
    ///   </description></item>
    ///   <item><description>
    ///     <c>(0, 0)</c> — XG's <b>no-legal-move</b> (dance) encoding,
    ///     emitted when the on-roll player has no legal play.
    ///   </description></item>
    /// </list>
    ///
    /// <para>
    /// Predicate scans every entry in <c>analysis.Moves</c> rather than just
    /// the best-by-equity candidate, because the illegal-play workaround
    /// places the sentinel at the user-play index — and
    /// <see cref="ComputeMoveAfterBoards"/> reads both the best-by-equity
    /// and user-play slots. Either contains the sentinel, the leaf trips.
    /// </para>
    ///
    /// <para>
    /// Internal-not-private so test code that pairs raw <c>MoveRecord</c>s
    /// with iterator output can mirror the emission filter without
    /// re-implementing the predicate. Skipped records would otherwise
    /// shift downstream iterator-pairing indices.
    /// </para>
    /// </summary>
    internal static bool IsSentinelOnlyAnalysis(BestMoveAnalysis analysis)
    {
        // Moves is fixed-length 32 with zero-padded unused entries; only the
        // first MoveCount slots are real candidates. Scanning past MoveCount
        // would read the (0, 0, …) padding as a dance sentinel and falsely
        // trigger on every analysis with fewer than 32 candidates.
        int n = Math.Min(analysis.MoveCount, analysis.Moves.Length);
        for (int i = 0; i < n; i++)
        {
            sbyte[] candidate = analysis.Moves[i];
            if (candidate.Length < 2) continue;
            if ((candidate[0] == -100 && candidate[1] == -100)
                || (candidate[0] == 0 && candidate[1] == 0))
                return true;
        }
        return false;
    }

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

    /// <summary>
    /// Compact display form of <see cref="LevelLabel"/>, sized for narrow
    /// table cells. N-ply labels are kept intact (short enough already);
    /// XG Roller family collapses to R / R+ / R++; Book V1 and Book V2
    /// both collapse to "Book" — the version distinction is preserved in
    /// the full <see cref="LevelLabel"/> but isn't surfaced in the
    /// compact column. The Rollout sentinel (<c>short 100</c>) without a
    /// matching rollout context abbreviates to "Ro" — the normal rollout
    /// path goes through <see cref="ResolveDepthInfo"/>'s rollout branch
    /// and never hits this code.
    /// </summary>
    private static string LevelAbbreviation(short level) => level switch
    {
        0 => "1-ply",
        1 => "2-ply",
        2 => "3-ply",
        12 => "3-ply red",
        3 => "4-ply",
        4 => "5-ply",
        5 => "6-ply",
        6 => "7-ply",
        100 => "Ro",
        1000 => "R",
        1001 => "R+",
        1002 => "R++",
        998 => "Book",
        999 => "Book",
        _ => $"level-{level}",
    };

    /// <summary>
    /// Ordinal ranking of the analysis depth; higher = deeper / more
    /// rigorous. Consumed by <see cref="BackgammonDiagram_Lib"/> to flag
    /// out-of-order analysis across adjacent sorted-by-equity plays.
    ///
    /// <para>
    /// Numeric gaps between categories leave room for future depths
    /// without renumbering: N-ply occupies 1..7, XG Roller family 20..22,
    /// rollouts 100+inner-ply (see <see cref="ResolveDepthInfo"/>). Book
    /// V1/V2 and any unrecognised level rank 0 — the lowest slot —
    /// because a static book lookup is not an analysis of this position.
    /// "3-ply red" shares rank 3 with plain 3-ply: reduced variance
    /// doesn't deepen search, it only narrows the candidate set.
    /// </para>
    /// </summary>
    private static int LevelRank(short level) => level switch
    {
        0 => 1,
        1 => 2,
        2 => 3,
        12 => 3,
        3 => 4,
        4 => 5,
        5 => 6,
        6 => 7,
        100 => 100,
        1000 => 20,
        1001 => 21,
        1002 => 22,
        998 => 0,
        999 => 0,
        _ => 0,
    };

}