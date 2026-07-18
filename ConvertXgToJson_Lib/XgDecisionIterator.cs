using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Iterates over XgFile records and yields one row per analysed decision —
/// every checker-play (MoveRecord) and cube decision (CubeRecord) XG has
/// analysed. Two surfaces over the same decision set: <see cref="Iterate"/>
/// yields flat <see cref="DecisionRow"/> records (CSV-shaped), while
/// <see cref="IterateDiagramRequests"/> yields <see cref="BgDecisionData"/>
/// records (diagram-shaped).
///
/// <para>
/// <c>.xgp</c> position files are the exception: they yield <em>at most one</em>
/// decision. See <see cref="SelectXgpDecision"/> for the policy and its
/// rationale.
/// </para>
/// </summary>
public static class XgDecisionIterator
{
    // -----------------------------------------------------------------------
    //  Public API — single file
    // -----------------------------------------------------------------------

    /// <summary>
    /// Yields all decisions from a single already-parsed <see cref="XgFile"/>.
    ///
    /// <para>
    /// When <paramref name="sourceFile"/> names an <c>.xgp</c> position file,
    /// at most one decision is yielded — the analysed checker-play if there is
    /// one, else the analysed cube. See <see cref="SelectXgpDecision"/>.
    /// </para>
    /// </summary>
    /// <param name="file">The parsed XG file.</param>
    /// <param name="sourceFile">
    /// Originating file name including extension (e.g. <c>"match.xg"</c> or
    /// <c>"position.xgp"</c>). Must be non-null — the iterator stamps a
    /// <see cref="DecisionId"/> on every yielded row, and the extension
    /// drives the discrimination between <see cref="XgDecisionId"/> and
    /// <see cref="XgpDecisionId"/>. Copied verbatim onto every yielded
    /// <see cref="DecisionRow.SourceFile"/>.
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
    /// <param name="options">
    /// Optional producer configuration. See <see cref="XgIteratorOptions"/> —
    /// notably <see cref="XgIteratorOptions.OpeningBook"/> for enriching
    /// book-stamped candidates with the cached rollout's parameters.
    /// </param>
    /// <param name="logger">
    /// Optional logger. When a decision is skipped because XG stamped its
    /// played candidate with the illegal-play marker (see
    /// <see cref="SentinelKind.IllegalPlay"/>), a <c>Warning</c> is emitted
    /// naming the source file, game, move, and roll. Defaults to
    /// <see cref="NullLogger.Instance"/> — silent, and suppressible by log
    /// level. Dance ((0, 0)) skips remain silent (normal, not an error).
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown eagerly (before any deferred enumeration) when
    /// <paramref name="sourceFile"/> is <see langword="null"/>. DecisionId
    /// stamping requires a filename; this is a producer-contract violation,
    /// not a content-level parse error.
    /// </exception>
    public static IEnumerable<DecisionRow> Iterate(
        XgFile file,
        string? sourceFile,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null,
        XgIteratorOptions? options = null,
        ILogger? logger = null)
    {
        if (sourceFile == null)
            throw new InvalidOperationException(
                "XgDecisionIterator.Iterate requires a non-null sourceFile for DecisionId stamping.");
        return IterateCore<DecisionRow>(file, sourceFile, state, callbacks, options, logger, BuildMoveRow, BuildCubeRows);
    }

    /// <summary>
    /// Single entry point behind both decision surfaces. Walks the record
    /// stream once via <see cref="IterateAnalysedDecisions"/>, then — for
    /// <c>.xgp</c> position files only — narrows the result to a single
    /// decision through <see cref="SelectXgpDecision"/>.
    ///
    /// <para>
    /// Placing the <c>.xgp</c> emission policy here, rather than in either
    /// caller, is what guarantees <see cref="Iterate"/> and
    /// <see cref="IterateDiagramRequests"/> can never disagree about which
    /// decision an <c>.xgp</c> represents.
    /// </para>
    /// </summary>
    private static IEnumerable<T> IterateCore<T>(
        XgFile file,
        string sourceFile,
        XgIteratorState? state,
        XgIteratorCallbacks? callbacks,
        XgIteratorOptions? options,
        ILogger? logger,
        Func<MoveRecord, MatchContext, string, List<RolloutContext>, OpeningBook?, T?> buildMove,
        Func<CubeRecord, MatchContext, string, List<RolloutContext>, IEnumerable<T>> buildCube)
        where T : class, IDecisionFilterData
    {
        var decisions = IterateAnalysedDecisions(
            file, sourceFile, state, callbacks, options, logger, buildMove, buildCube);

        return IsXgpSource(sourceFile) ? SelectXgpDecision(decisions) : decisions;
    }

    /// <summary>
    /// Applies the <c>.xgp</c> single-decision emission policy: emit the
    /// checker-play if the file has an analysed, non-sentinel one; otherwise
    /// emit the analysed cube, if any; otherwise emit nothing.
    ///
    /// <para>
    /// An <c>.xgp</c>'s move pane exists only because dice were rolled, so
    /// dice in the file mean the saved decision <em>is</em> the play. XG
    /// nonetheless always writes a cube pane, which is incidental — a
    /// curated cube problem is a pre-roll position and carries no move pane
    /// at all. Analysis depth is deliberately not compared: an analysed play
    /// wins even against a more deeply analysed cube.
    /// </para>
    ///
    /// <para>
    /// This is what makes the bare-filename <see cref="XgpDecisionId"/> a
    /// valid key. Before the policy existed, an <c>.xgp</c> with analysis in
    /// both panes yielded two decisions stamped with the same Id.
    /// </para>
    ///
    /// <para>
    /// A sentinel-skipped play (illegal play / dance) never reaches this
    /// filter — <see cref="IterateAnalysedDecisions"/> drops it upstream — so
    /// it does not suppress an otherwise analysed cube.
    /// </para>
    ///
    /// <para>
    /// Look-ahead is bounded and <c>.xgp</c>-scoped: at most one cube is held
    /// while scanning for a play, and the first play short-circuits the walk.
    /// <c>.xg</c> iteration bypasses this filter entirely and stays streaming.
    /// </para>
    /// </summary>
    private static IEnumerable<T> SelectXgpDecision<T>(IEnumerable<T> decisions)
        where T : class, IDecisionFilterData
    {
        T? cube = null;

        foreach (var decision in decisions)
        {
            if (!decision.IsCube)
            {
                yield return decision;
                yield break;
            }
            cube ??= decision;
        }

        if (cube != null)
            yield return cube;
    }

    /// <summary>
    /// Shared iteration skeleton for both decision surfaces. Walks the record
    /// stream once — match-info extraction, state population, the skip / stop
    /// callbacks, game-header handling — and delegates only the per-decision
    /// row construction to <paramref name="buildMove"/> /
    /// <paramref name="buildCube"/>. <c>Iterate</c> supplies the
    /// <see cref="DecisionRow"/> builders; <c>IterateDiagramRequests</c> the
    /// <see cref="BgDecisionData"/> builders.
    /// </summary>
    private static IEnumerable<T> IterateAnalysedDecisions<T>(
        XgFile file,
        string sourceFile,
        XgIteratorState? state,
        XgIteratorCallbacks? callbacks,
        XgIteratorOptions? options,
        ILogger? logger,
        Func<MoveRecord, MatchContext, string, List<RolloutContext>, OpeningBook?, T?> buildMove,
        Func<CubeRecord, MatchContext, string, List<RolloutContext>, IEnumerable<T>> buildCube)
        where T : class, IDecisionFilterData
    {
        logger ??= NullLogger.Instance;
        var openingBook = options?.OpeningBook;

        var context = new MatchContext(file.Records, sourceFile, file.Comments);

        var matchInfo = ExtractMatchInfo(file)
            ?? throw new InvalidDataException(
                $"XG file '{sourceFile}' has no readable match header — cannot iterate decisions.");

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

                var gameInfo = XgGameInfo.From(gh, context.MatchLength);

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

            if (record is MoveRecord move && IsAnalysed(move))
            {
                // Skip XG's non-play sentinels before they reach a move leaf:
                // an illegal-play marker historically crashed AfterBoardBuilder,
                // a dance rendered as "1/1" garbage. Illegal plays are worth a
                // contextual warning; dances are normal and skip silently.
                switch (ClassifySentinelAnalysis(move.Analysis))
                {
                    case SentinelKind.IllegalPlay:
                        logger.LogWarning(
                            "Illegal play in {SourceFile}, game {Game}, move {MoveNumber}, roll {Roll}",
                            sourceFile, context.GameNumber, context.MoveNumber, DiceToInt(move.Dice));
                        continue;
                    case SentinelKind.Dance:
                        continue;
                }

                var row = buildMove(move, context, sourceFile, file.Rollouts, openingBook);
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
                foreach (var row in buildCube(cube, context, sourceFile, file.Rollouts))
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
    ///
    /// <para>
    /// The <c>.xgp</c> single-decision emission policy applies identically here
    /// — both surfaces route through the same <see cref="IterateCore"/>. See
    /// <see cref="SelectXgpDecision"/>.
    /// </para>
    /// </summary>
    /// <param name="file">The parsed XG file.</param>
    /// <param name="sourceFile">
    /// Originating file name including extension (e.g. <c>"match.xg"</c> or
    /// <c>"position.xgp"</c>). Must be non-null — same contract as
    /// <see cref="Iterate"/>; the iterator stamps a <see cref="DecisionId"/>
    /// on every yielded record, and the extension drives the discrimination
    /// between <see cref="XgDecisionId"/> and <see cref="XgpDecisionId"/>.
    /// Copied verbatim onto every yielded <see cref="DescriptiveData.SourceFile"/>.
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
    /// <param name="options">
    /// Optional producer configuration. Behaves identically to
    /// <see cref="Iterate"/> — see <see cref="XgIteratorOptions"/>.
    /// </param>
    /// <param name="logger">
    /// Optional logger. Behaves identically to <see cref="Iterate"/>: an
    /// illegal-play skip emits a contextual <c>Warning</c>; dances skip
    /// silently. Defaults to <see cref="NullLogger.Instance"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown eagerly (before any deferred enumeration) when
    /// <paramref name="sourceFile"/> is <see langword="null"/>. DecisionId
    /// stamping requires a filename; this is a producer-contract violation,
    /// not a content-level parse error.
    /// </exception>
    public static IEnumerable<BgDecisionData> IterateDiagramRequests(
        XgFile file,
        string? sourceFile,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null,
        XgIteratorOptions? options = null,
        ILogger? logger = null)
    {
        if (sourceFile == null)
            throw new InvalidOperationException(
                "XgDecisionIterator.IterateDiagramRequests requires a non-null sourceFile for DecisionId stamping.");
        return IterateCore<BgDecisionData>(file, sourceFile, state, callbacks, options, logger, BuildMoveDiagramRequest, BuildCubeDiagramRequests);
    }

    // -----------------------------------------------------------------------
    //  Directory walks (internal — external consumers build their own walks
    //  over EnumerateXgFormatFiles + Iterate, as XgFilter_Lib does)
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
    /// <param name="options">Optional producer configuration, shared across
    /// all files in the walk. See <see cref="XgIteratorOptions"/>.</param>
    internal static IEnumerable<DecisionRow> IterateXgDirectory(
        string xgDir,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null,
        XgIteratorOptions? options = null)
    {
        foreach (var path in XgFileReader.EnumerateXgFormatFiles(xgDir))
        {
            XgFile file;
            try { file = XgFileReader.ReadFile(path); }
            catch { continue; }

            string sourceFile = Path.GetFileName(path);
            foreach (var row in Iterate(file, sourceFile, state, callbacks, options))
                yield return row;
        }
    }

    /// <summary>
    /// Yields all decisions from every .json file in <paramref name="jsonDir"/>.
    /// </summary>
    /// <param name="jsonDir">Directory containing .json files.</param>
    /// <param name="state">Optional read-only observer. See <see cref="Iterate"/>.</param>
    /// <param name="callbacks">Optional skip predicates. See <see cref="IterateXgDirectory"/>.</param>
    /// <param name="options">Optional producer configuration. See <see cref="IterateXgDirectory"/>.</param>
    internal static IEnumerable<DecisionRow> IterateJsonDirectory(
        string jsonDir,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null,
        XgIteratorOptions? options = null)
    {
        foreach (var path in Directory.EnumerateFiles(jsonDir, "*.json"))
        {
            XgFile file;
            try { file = XgFileReader.ReadJson(path); }
            catch { continue; }

            string sourceFile = Path.GetFileName(path);
            foreach (var row in Iterate(file, sourceFile, state, callbacks, options))
                yield return row;
        }
    }

    // -----------------------------------------------------------------------
    //  Move record — DecisionRow
    // -----------------------------------------------------------------------

    private static DecisionRow? BuildMoveRow(MoveRecord move, MatchContext ctx, string sourceFile, List<RolloutContext> rollouts, OpeningBook? book)
    {
        var analysis = move.Analysis;
        if (!IsAnalysed(move))
            return null;

        // XG's native rank 0 is not always the highest-equity candidate;
        // CSV Equity and the depth label that describes it must both key
        // off the best-by-equity index, not Evals[0]. See
        // FindBestByEquityIndex for the convention rationale.
        int bestIdx = FindBestByEquityIndex(analysis);
        var bestEval = analysis.Evals[bestIdx];
        int dice = DiceToInt(move.Dice);

        // Label and taxonomy pair all key off the same best-by-equity
        // candidate — book entry included — so DecisionRow.AnalysisDepth and
        // DecisionRow.AnalysisMode/AnalysisLevel can never describe
        // different candidates.
        short bestLevel = bestIdx < analysis.EvalLevels.Length ? analysis.EvalLevels[bestIdx].Level : (short)0;
        var (depth, _, _, mode, level) = ResolveDepthInfo(
            evalLevel: bestLevel,
            rolloutIndex: bestIdx < move.RolloutIndices.Length ? move.RolloutIndices[bestIdx] : -1,
            rollouts: rollouts,
            bookEntry: LookupBookEntry(book, bestLevel, analysis, bestIdx, move.ActivePlayer, ctx));

        string xgid = BuildXgid(
            move.InitialPosition, move.ActivePlayer, ctx.CubeValue, ctx.CubePosition, dice, ctx);

        int[] board = ToBoard(move.InitialPosition.Points, move.ActivePlayer);
        int userPlayIndex = FindUserPlayIndex(analysis, move.FinalPosition);
        var (afterBest, afterPlayer) = ComputeMoveAfterBoards(board, analysis, userPlayIndex);

        return new DecisionRow
        {
            Id = BuildDecisionId(sourceFile, ctx.GameNumber, ctx.MoveNumber, isCube: false),
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
            AnalysisMode = mode,
            AnalysisLevel = level,
            Equity = bestEval.Equity,
            Board = board,
            AfterBestBoard = afterBest,
            AfterPlayerBoard = afterPlayer,
        };
    }

    // -----------------------------------------------------------------------
    //  Move record — DiagramRequest
    // -----------------------------------------------------------------------

    private static BgDecisionData? BuildMoveDiagramRequest(MoveRecord move, MatchContext ctx, string sourceFile, List<RolloutContext> rollouts, OpeningBook? book)
    {
        var analysis = move.Analysis;
        if (!IsAnalysed(move))
            return null;
        int dice = DiceToInt(move.Dice);
        if (dice == 0) return null;

        string xgid = BuildXgid(
            move.InitialPosition, move.ActivePlayer, ctx.CubeValue, ctx.CubePosition, dice, ctx);

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
            // Each candidate enriches from its own book entry (keyed by its
            // own resulting position) — a book-analysed decision can mix
            // book-stamped and evaluated candidates, and different candidates
            // resolve to different book rollouts.
            var (candidateDepth, candidateDepthAbbrev, candidateDepthRank, candidateMode, candidateLevel) = ResolveDepthInfo(
                evalLevel: evalLevel,
                rolloutIndex: i < move.RolloutIndices.Length ? move.RolloutIndices[i] : -1,
                rollouts: rollouts,
                bookEntry: LookupBookEntry(book, evalLevel, analysis, i, move.ActivePlayer, ctx));
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
                AnalysisMode = candidateMode,
                AnalysisLevel = candidateLevel,
                Equity = equity,
                EquityLoss = bestEquity - equity,
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
            Id = BuildDecisionId(sourceFile, ctx.GameNumber, ctx.MoveNumber, isCube: false),
            Xgid = xgid,
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
                Game = ctx.GameNumber,
                MoveNumber = ctx.MoveNumber,
                IsStandardStart = ctx.IsStandardStart,
                Comment = ctx.CommentAt(move.CommentIndex),
                Flagged = move.Flagged,
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

    private static IEnumerable<DecisionRow> BuildCubeRows(CubeRecord cube, MatchContext ctx, string sourceFile, List<RolloutContext> rollouts)
    {
        var analysis = cube.Analysis;

        // No book entry: the cube-row keying convention against the opening
        // book is unproven (see LookupBookEntry), so a book-stamped cube
        // resolves to the degraded BookRollout + Unknown pair by design.
        var (depth, _, _, mode, level) = ResolveDepthInfo(
            evalLevel: analysis.LevelRequest,
            rolloutIndex: cube.RolloutIndex,
            rollouts: rollouts);

        int cubeActual = CubeValueActual(cube.CubeValue);
        int cubePos = Math.Sign(cube.CubeValue);

        string xgid = BuildXgid(
            cube.Position, cube.ActivePlayer, cubeActual, cubePos, dice: 0, ctx);

        int[] board = ToBoard(cube.Position.Points, cube.ActivePlayer);

        yield return new DecisionRow
        {
            Id = BuildDecisionId(sourceFile, ctx.GameNumber, ctx.MoveNumber + 1, isCube: true),
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
            AnalysisMode = mode,
            AnalysisLevel = level,
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

    private static IEnumerable<BgDecisionData> BuildCubeDiagramRequests(CubeRecord cube, MatchContext ctx, string sourceFile, List<RolloutContext> rollouts)
    {
        var analysis = cube.Analysis;

        // No book entry — same degradation rationale as BuildCubeRows.
        var (depth, depthAbbrev, depthRank, mode, level) = ResolveDepthInfo(
            evalLevel: analysis.LevelRequest,
            rolloutIndex: cube.RolloutIndex,
            rollouts: rollouts);

        int cubePos = Math.Sign(cube.CubeValue);
        int cubeActual = CubeValueActual(cube.CubeValue);

        string xgid = BuildXgid(
            cube.Position, cube.ActivePlayer, cubeActual, cubePos, dice: 0, ctx);

        int[] board = ToBoard(cube.Position.Points, cube.ActivePlayer);
        ComputePipCounts(board, out int onRollPips, out int opponentPips);

        yield return new BgDecisionData
        {
            Id = BuildDecisionId(sourceFile, ctx.GameNumber, ctx.MoveNumber + 1, isCube: true),
            Xgid = xgid,
            Position = new PositionData
            {
                Mop = board,
                OnRollNeeds = ctx.NeedsFor(cube.ActivePlayer),
                OpponentNeeds = ctx.NeedsFor(-cube.ActivePlayer),
                OnRollPipCount = onRollPips,
                OpponentPipCount = opponentPips,
                CubeSize = cubeActual,
                CubeOwner = CubeOwnerFor(cubePos, cube.ActivePlayer),
                IsCrawford = ctx.IsCrawford,
            },
            Decision = new DecisionData
            {
                IsCube = true,
                Dice = [0, 0],
                NoDoubleEquity = IsUsable(analysis.EquityNoDouble) ? analysis.EquityNoDouble : 0.0,
                DoubleTakeEquity = IsUsable(analysis.EquityDoubleTake) ? analysis.EquityDoubleTake : 0.0,
                CubelessNoDoubleEquity = IsUsable(analysis.EvalNoDouble.Equity) ? analysis.EvalNoDouble.Equity : 0.0,
                CubelessDoubleTakeEquity = IsUsable(analysis.EvalDoubleTake.Equity) ? analysis.EvalDoubleTake.Equity : 0.0,
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
                CubeAnalysisMode = mode,
                CubeAnalysisLevel = level,
                UserDoubleError = cube.ErrorCube > -999.0 ? Math.Abs(cube.ErrorCube) : (double?)null,
                UserTakeError = (cube.Doubled == 1 && cube.ErrorTake > -999.0) ? Math.Abs(cube.ErrorTake) : (double?)null,
            },
            Descriptive = new DescriptiveData
            {
                MatchLength = ctx.MatchLength,
                OnRollName = ctx.PlayerName(cube.ActivePlayer),
                OpponentName = ctx.PlayerName(-cube.ActivePlayer),
                SourceFile = ctx.SourceFile,
                Game = ctx.GameNumber,
                MoveNumber = ctx.MoveNumber + 1,
                IsStandardStart = ctx.IsStandardStart,
                Comment = ctx.CommentAt(cube.CommentIndex),
                Flagged = cube.Flagged,
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
        var board = new int[26];
        for (int i = 0; i < 26; i++)
            board[i] = points[i];
        return activePlayer >= 0 ? board : BackgammonConstants.Flip(board);
    }

    private static PositionEngine FlipPosition(PositionEngine pos)
        => new() { Points = BackgammonConstants.Flip(pos.Points) };

    /// <summary>
    /// Builds the XGID string for a decision. When <paramref name="activePlayer"/>
    /// is negative the position is flipped, the cube position negated, and the
    /// two scores swapped so the XGID reads from the on-roll player's side.
    /// Shared by the move-row and cube-row builders.
    /// </summary>
    private static string BuildXgid(
        PositionEngine position, int activePlayer, int cubeValue, int cubePosition,
        int dice, MatchContext ctx)
    {
        var xgidPosition = activePlayer >= 0 ? position : FlipPosition(position);
        int xgidCubePos = activePlayer >= 0 ? cubePosition : -cubePosition;

        return XgidEncoder.Encode(
            position: xgidPosition,
            cubeValue: cubeValue,
            cubePos: xgidCubePos,
            turn: 1,
            dice: dice,
            score1: activePlayer >= 0 ? ctx.Score1 : ctx.Score2,
            score2: activePlayer >= 0 ? ctx.Score2 : ctx.Score1,
            crawfordJacoby: ctx.XgidCrawfordJacobyField,
            matchLength: ctx.MatchLength,
            maxCubeLog2: ctx.MaxCubeLimit);
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

    /// <summary>
    /// Scans <paramref name="file"/>'s records for the first
    /// <see cref="MatchHeaderRecord"/> and returns an <see cref="XgMatchInfo"/>
    /// populated from it, or <c>null</c> if no match header is present.
    /// Callers that previously relied on a default-constructed return (empty
    /// strings, <c>MatchLength = 0</c>) should treat <c>null</c> as
    /// "match header unreadable" rather than as a zero-length money match.
    /// </summary>
    public static XgMatchInfo? ExtractMatchInfo(XgFile file)
    {
        foreach (var r in file.Records)
        {
            if (r is MatchHeaderRecord hm)
                return XgMatchInfo.From(hm);
        }
        return null;
    }

    // -----------------------------------------------------------------------
    //  Depth resolution
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves the analysis depth for a candidate into five parallel
    /// forms: the full human-readable label, a compact abbreviation for
    /// narrow cells, an ordinal rank (higher = deeper / more rigorous),
    /// and the machine-usable <see cref="AnalysisMode"/> ×
    /// <see cref="AnalysisLevel"/> taxonomy pair. All five come from the
    /// same structured inputs — no string re-parsing — so the producer
    /// keeps ownership of depth semantics.
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
    /// The pair is <see cref="AnalysisMode.Rollout"/> plus the inner ply as
    /// its <see cref="AnalysisLevel"/>: <c>innerPly</c> 1–7 maps to
    /// <see cref="AnalysisLevel.Ply1"/>–<see cref="AnalysisLevel.Ply7"/>;
    /// an <c>innerPly</c> outside that range degrades the level to
    /// <see cref="AnalysisLevel.Unknown"/> while rank / abbreviation still
    /// reflect the raw value (defensive — a rolled-out candidate always
    /// carries an in-range inner ply in practice).
    /// </para>
    ///
    /// <para>
    /// Book branch: when <paramref name="bookEntry"/> is non-null — the
    /// caller resolved a V2-book-stamped candidate against a loaded
    /// <see cref="OpeningBook"/> (see <see cref="LookupBookEntry"/>) — and
    /// the entry is a rollout entry (<c>Level == 100</c>), the entry's
    /// stored rollout parameters enrich the projection:
    /// <c>Label = "Book V2: {trials} trials. {moves-level label}"</c>,
    /// <c>Abbreviation = "B{moves-level token}p{trials}"</c> (e.g.
    /// "B4p12960"), following the rollout sibling forms above. The pair is
    /// <see cref="AnalysisMode.BookRollout"/> plus the entry's
    /// <c>RolloutMovesLevel</c> mapped through <see cref="LevelInfo"/> —
    /// the moves level, because only checker-play candidates are enriched
    /// today: the book's cube-row keying convention is unproven (no
    /// book-stamped cube decision exists in the fixture corpus to pin it),
    /// so cube rows never pass an entry and degrade to
    /// <see cref="AnalysisMode.BookRollout"/> +
    /// <see cref="AnalysisLevel.Unknown"/>. Rank stays 99 — enrichment
    /// recovers the cached rollout's parameters, but <c>DepthRank</c>
    /// semantics hold stable across enrichment (the file itself still
    /// records less than an explicit rollout). A non-rollout entry (the
    /// book also stores Roller++ evaluation baselines) falls through
    /// unenriched: its stored levels are zeroed, not rollout parameters.
    /// </para>
    ///
    /// <para>
    /// Non-rollout branch: returns the <see cref="LevelInfo"/> projection —
    /// label, abbreviation, rank, and the mode × level pair — for
    /// <paramref name="evalLevel"/>. The rank ordering is: N-ply → N (1..7),
    /// XG Roller family → 20–22, Book V1/V2 → 99 (rollout-derived opening
    /// book: above XG Roller++ (22), below the explicit-rollout floor
    /// (100)), any unrecognised level → 0. The edge case is a "Rollout"
    /// sentinel (<c>short 100</c>) without a matching rollout context, which
    /// ranks 100 as <see cref="AnalysisMode.Rollout"/> +
    /// <see cref="AnalysisLevel.Unknown"/> — the same degradation as a
    /// no-inner-ply rollout (e.g. truncated at level 0).
    /// </para>
    ///
    /// <para>
    /// Per-candidate scalar input: callers pass the rollout index keyed
    /// to a single candidate (move-path: <c>move.RolloutIndices[i]</c>;
    /// cube-path: <c>cube.RolloutIndex</c>) and the book entry resolved
    /// for that same candidate's resulting position. The earlier
    /// array-shaped signature iterated and returned on the first valid
    /// hit, which caused every candidate in a decision to inherit the
    /// rollout label whenever any candidate was rolled out.
    /// </para>
    /// </summary>
    internal static (string Label, string Abbreviation, int Rank, AnalysisMode Mode, AnalysisLevel Level) ResolveDepthInfo(
        short evalLevel,
        int rolloutIndex,
        List<RolloutContext> rollouts,
        OpeningBookEntry? bookEntry = null)
    {
        if (rolloutIndex >= 0 && rolloutIndex < rollouts.Count)
        {
            var ctx = rollouts[rolloutIndex];
            int plyLevel = ctx.Level2 > 0 ? ctx.Level2
                         : ctx.Level1 > 0 ? ctx.Level1
                         : ctx.LevelTrunc;
            int innerPly = plyLevel + 1;
            string label = $"Rollout: {ctx.GamesRolled} trials. {LevelInfo((short)plyLevel).Label}";
            string abbrev = $"{innerPly}p{ctx.GamesRolled}";
            int rank = 100 + innerPly;
            return (label, abbrev, rank, AnalysisMode.Rollout, LevelForInnerPly(innerPly));
        }

        if (bookEntry is { IsRollout: true })
        {
            var inner = LevelInfo((short)bookEntry.RolloutMovesLevel);
            string label = $"Book V2: {bookEntry.Trials} trials. {inner.Label}";
            string abbrev = $"B{BookInnerToken(inner)}p{bookEntry.Trials}";
            return (label, abbrev, LevelInfo(evalLevel).Rank, AnalysisMode.BookRollout, inner.Level);
        }

        return LevelInfo(evalLevel);
    }

    /// <summary>
    /// Maps a rollout's inner evaluation ply to the <see cref="AnalysisLevel"/>
    /// member stamped alongside <see cref="AnalysisMode.Rollout"/>. Inner ply
    /// 1–7 stamp the matching
    /// <see cref="AnalysisLevel.Ply1"/>–<see cref="AnalysisLevel.Ply7"/>;
    /// anything outside that range degrades to
    /// <see cref="AnalysisLevel.Unknown"/> ("level not recorded" — the
    /// taxonomy's documented graceful-degradation stamp). A plain arithmetic
    /// offset would couple this to the enum's declaration order, which
    /// <see cref="AnalysisLevel"/> documents as informational-not-contractual;
    /// the explicit switch keeps the mapping single-sourced.
    /// </summary>
    private static AnalysisLevel LevelForInnerPly(int innerPly) => innerPly switch
    {
        1 => AnalysisLevel.Ply1,
        2 => AnalysisLevel.Ply2,
        3 => AnalysisLevel.Ply3,
        4 => AnalysisLevel.Ply4,
        5 => AnalysisLevel.Ply5,
        6 => AnalysisLevel.Ply6,
        7 => AnalysisLevel.Ply7,
        _ => AnalysisLevel.Unknown,
    };

    /// <summary>
    /// Compact moves-level token for the enriched book abbreviation
    /// ("B{token}p{trials}"), parallel to the rollout sibling's inner-ply
    /// digit ("{innerPly}p{trials}"): a ply level contributes its ply number
    /// (rank 1–7 <b>is</b> the ply number, so "B4p12960" for a 4-ply-moves
    /// entry), any other level its <see cref="LevelInfo"/> abbreviation —
    /// unreachable for moves levels in the shipped database (all ply codes)
    /// but the book format allows Roller codes, and the cube level
    /// demonstrably uses them.
    /// </summary>
    private static string BookInnerToken(
        (string Label, string Abbreviation, int Rank, AnalysisMode Mode, AnalysisLevel Level) inner) =>
        inner.Rank is >= 1 and <= 7 ? inner.Rank.ToString() : inner.Abbreviation;

    /// <summary>XG's level code for a V2-book-analysed candidate — the only
    /// code the enrichment lookup fires on. The V1 code (999) is never looked
    /// up: only the V2 database is parsed, and a V1 stamp's rollout did not
    /// come from it.</summary>
    private const short BookV2Level = 998;

    /// <summary>
    /// Resolves a book-stamped checker-play candidate against the caller's
    /// loaded <see cref="OpeningBook"/>, or returns null when enrichment does
    /// not apply — which is the normal case, checked cheapest-first: no book
    /// supplied, the candidate is not V2-book-stamped
    /// (<paramref name="evalLevel"/> ≠ 998), no stored resulting position, a
    /// decision context outside the proven keying conventions, or a plain
    /// lookup miss. A null return degrades the candidate to the bare
    /// <see cref="AnalysisMode.BookRollout"/> + <see cref="AnalysisLevel.Unknown"/>
    /// stamp in <see cref="ResolveDepthInfo"/>.
    ///
    /// <para>
    /// The key is exactly the pane data session 1 proved bitwise:
    /// <c>PositionsPlayed[i]</c> (the candidate's resulting position,
    /// player-1-relative) plus the decision context, normalized by the
    /// <see cref="OpeningBookKey"/> factories (perspective flip, away-pair
    /// orientation, Jacoby / Crawford). Two context guards scope the lookup
    /// to what those factories cover: the cube must be centred at 1 (the
    /// factories' only supported cube context — the book's turned-cube owner
    /// sign is unverified), and match play must have both aways ≥ 1 (the
    /// factories' argument contract; a book stamp outside a real match score
    /// would be malformed input anyway).
    /// </para>
    ///
    /// <para>
    /// Checker-play candidates only. Cube rows never reach this helper: the
    /// book's cube-decision keying convention is unproven — the fixture
    /// corpus contains no book-stamped cube decision to pin it against — so
    /// cube book stamps degrade rather than guess (see the cube builders).
    /// </para>
    /// </summary>
    private static OpeningBookEntry? LookupBookEntry(
        OpeningBook? book,
        short evalLevel,
        BestMoveAnalysis analysis,
        int candidateIndex,
        int activePlayer,
        MatchContext ctx)
    {
        if (book == null || evalLevel != BookV2Level)
            return null;
        if (candidateIndex >= analysis.PositionsPlayed.Length)
            return null;
        if (ctx.CubeValue != 1 || ctx.CubePosition != 0)
            return null;

        OpeningBookKey key;
        if (ctx.MatchLength == 0)
        {
            key = OpeningBookKey.ForMoneyPlay(
                analysis.PositionsPlayed[candidateIndex], activePlayer, ctx.IsJacoby);
        }
        else
        {
            int moverAway = ctx.NeedsFor(activePlayer);
            int opponentAway = ctx.NeedsFor(-activePlayer);
            if (moverAway < 1 || opponentAway < 1)
                return null;
            key = OpeningBookKey.ForMatchPlay(
                analysis.PositionsPlayed[candidateIndex], activePlayer,
                moverAway, opponentAway, ctx.IsCrawford);
        }

        return book.TryGetEntry(key, out var entry) ? entry : null;
    }

    /// <summary>
    /// Thin wrapper returning only the label form of
    /// <see cref="ResolveDepthInfo"/>, for callers that don't need the
    /// abbreviation, rank, or class.
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
    /// Builds the <see cref="DecisionId"/> stamped onto every yielded
    /// <see cref="DecisionRow"/> / <see cref="BgDecisionData"/> by
    /// dispatching on the source file's extension.
    ///
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>.xgp</c> → <see cref="XgpDecisionId"/> keyed on the bare
    ///     filename, with no within-file coordinates. XG itself does
    ///     <em>not</em> guarantee one decision per <c>.xgp</c> — it always
    ///     writes a cube pane alongside the move pane, and both can carry
    ///     analysis. What makes the bare filename a valid key is
    ///     <see cref="SelectXgpDecision"/>, the iterator's emission policy,
    ///     which reduces every <c>.xgp</c> to at most one decision.
    ///   </description></item>
    ///   <item><description>
    ///     <c>.xg</c> → <see cref="XgDecisionId"/> carrying the within-file
    ///     tuple <c>(Filename, Game, MoveNumber, IsCube)</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <c>.json</c> → <see cref="XgDecisionId"/> with the same tuple
    ///     shape as <c>.xg</c>. <c>.json</c> is treated as an
    ///     XG-format-equivalent serialization — multi-decision content
    ///     written through <see cref="XgFileReader.WriteJsonAsync"/> and
    ///     parsed back through <see cref="XgFileReader.ReadJson"/>; the
    ///     record-level structure (game / move / cube) is identical to
    ///     <c>.xg</c>. The resulting Id's <c>Filename</c> ends in
    ///     <c>.json</c> by design: a <c>.xg</c> and <c>.json</c> of the
    ///     same content are distinct on-disk artifacts and legitimately
    ///     carry different Ids. This parallels the existing
    ///     <c>.xg</c> ↔ <c>.xgp</c> Id asymmetry — same decision, different
    ///     storage shape, different Id. Cross-format Id identity is not a
    ///     goal of this design.
    ///   </description></item>
    /// </list>
    ///
    /// <para>
    /// The cube emission path passes <c>ctx.MoveNumber + 1</c> for
    /// <paramref name="moveNumber"/> so the Id agrees with the emitted
    /// <c>DecisionRow.MoveNumber</c> / <c>DescriptiveData.MoveNumber</c>.
    /// </para>
    ///
    /// <para>
    /// Extension match is case-insensitive invariant — <c>.XG</c> and
    /// <c>.xg</c> on Windows clones both route to the <see cref="XgDecisionId"/>
    /// shape. Any other extension is a producer-side contract violation;
    /// <see cref="Iterate"/> / <see cref="IterateDiagramRequests"/> validate
    /// <c>sourceFile</c> non-null at iteration entry, but the extension is
    /// only verified once a candidate reaches a <c>Build*</c> site.
    /// </para>
    ///
    /// <para>
    /// Internal-not-private so test code can drive the unknown-extension
    /// path directly without synthesizing a full <see cref="XgFile"/>.
    /// Parallel to <see cref="IsSentinelOnlyAnalysis"/> and
    /// <see cref="FindBestByEquityIndex"/>.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="sourceFile"/>'s extension is none of
    /// <c>.xg</c>, <c>.xgp</c>, or <c>.json</c>.
    /// </exception>
    internal static DecisionId BuildDecisionId(
        string sourceFile, int game, int moveNumber, bool isCube)
    {
        if (IsXgpSource(sourceFile))
            return new XgpDecisionId(sourceFile);

        string ext = Path.GetExtension(sourceFile);
        if (string.Equals(ext, ".xg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase))
            return new XgDecisionId(sourceFile, game, moveNumber, isCube);
        throw new InvalidOperationException(
            $"Unsupported file extension '{ext}' for DecisionId stamping; expected '.xg', '.xgp', or '.json' (sourceFile='{sourceFile}').");
    }

    /// <summary>
    /// Single source of the <c>.xgp</c> recognition rule. Both the emission
    /// policy (<see cref="SelectXgpDecision"/>, applied in
    /// <see cref="IterateCore"/>) and Id stamping
    /// (<see cref="BuildDecisionId"/>) key off this predicate, so a file can
    /// never be treated as an <c>.xgp</c> by one and not the other. Extension
    /// match is case-insensitive invariant.
    /// </summary>
    private static bool IsXgpSource(string sourceFile) =>
        string.Equals(Path.GetExtension(sourceFile), ".xgp", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnalysed(MoveRecord move) =>
        move.Analysis.MoveCount > 0 && move.Analysis.Evals.Length > 0;

    /// <summary>
    /// Returns <c>true</c> when any candidate in <paramref name="analysis"/>'s
    /// <c>Moves</c> array is a known XG non-play sentinel. Used by
    /// <see cref="Iterate"/> and <see cref="IterateDiagramRequests"/> to skip
    /// these decisions before they reach
    /// <see cref="Parsing.AfterBoardBuilder.ComputeAfterBoard"/> or
    /// <see cref="XgMoveTranslator.Translate"/>: feeding either leaf the
    /// sentinel encoding has historically produced an
    /// <see cref="IndexOutOfRangeException"/> (the illegal-play marker) or a
    /// "1/1" notation glitch (the <c>(0, 0)</c> dance). A thin
    /// <c>!= </c><see cref="SentinelKind.None"/> projection of
    /// <see cref="ClassifySentinelAnalysis"/> — see that method and the
    /// single-sourced <see cref="XgMoveEncoding.ClassifyCandidate"/> for the
    /// recognition rules (illegal play keys on <c>from ==
    /// </c><see cref="XgMoveEncoding.IllegalPlayMarker"/>, covering both
    /// <c>(-100, -100)</c> and <c>(-100, X)</c>; dance is <c>(0, 0)</c>).
    ///
    /// <para>
    /// Internal-not-private so test code that pairs raw <c>MoveRecord</c>s
    /// with iterator output can mirror the emission filter without
    /// re-implementing the predicate. Skipped records would otherwise
    /// shift downstream iterator-pairing indices.
    /// </para>
    /// </summary>
    internal static bool IsSentinelOnlyAnalysis(BestMoveAnalysis analysis) =>
        ClassifySentinelAnalysis(analysis) != SentinelKind.None;

    /// <summary>
    /// Classifies an analysis by the first non-play sentinel among its real
    /// candidates, driving both the iterator skip and (for
    /// <see cref="SentinelKind.IllegalPlay"/>) the contextual warning. Returns
    /// <see cref="SentinelKind.None"/> for an ordinary analysis.
    ///
    /// <para>
    /// An illegal-play marker outranks a dance: the marker is the error-worthy
    /// condition and, in real files, sits at the user-play slot alongside other
    /// legal candidates, so it can co-occur with neither — but should it, the
    /// log-worthy kind wins. Recognition of each pair is delegated to the
    /// single-sourced <see cref="XgMoveEncoding.ClassifyCandidate"/>.
    /// </para>
    ///
    /// <para>
    /// Scans only the first <c>MoveCount</c> slots: <c>Moves</c> is
    /// fixed-length with <c>(0, 0, …)</c> zero-padding, which would otherwise
    /// read as a dance sentinel and falsely trigger on every analysis with
    /// fewer than the maximum candidate count.
    /// </para>
    /// </summary>
    internal static SentinelKind ClassifySentinelAnalysis(BestMoveAnalysis analysis)
    {
        int n = Math.Min(analysis.MoveCount, analysis.Moves.Length);
        SentinelKind found = SentinelKind.None;
        for (int i = 0; i < n; i++)
        {
            switch (XgMoveEncoding.ClassifyCandidate(analysis.Moves[i]))
            {
                case SentinelKind.IllegalPlay:
                    return SentinelKind.IllegalPlay; // log-worthy; outranks a dance
                case SentinelKind.Dance:
                    found = SentinelKind.Dance;
                    break;
            }
        }
        return found;
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

    /// <summary>
    /// Resolves an XG analysis level into its three display / ordering forms:
    /// the full <c>Label</c>, a compact <c>Abbreviation</c> for narrow table
    /// cells, and an ordinal <c>Rank</c> (higher = deeper / more rigorous).
    /// Single source of the level taxonomy — <see cref="ResolveDepthInfo"/>
    /// projects whichever forms a caller needs.
    ///
    /// <para>
    /// Abbreviation: N-ply labels are short enough to keep intact; the XG
    /// Roller family collapses to R / R+ / R++; Book V1 and V2 both collapse
    /// to "Book" (the version distinction survives only in <c>Label</c>). The
    /// Rollout sentinel (<c>short 100</c>) without a matching rollout context
    /// abbreviates to "Ro" — the normal rollout path goes through
    /// <see cref="ResolveDepthInfo"/>'s rollout branch and never reaches here.
    /// </para>
    ///
    /// <para>
    /// Rank: numeric gaps between categories leave room for future depths
    /// without renumbering — N-ply occupies 1..7, the XG Roller family 20..22,
    /// rollouts 100 + inner-ply (see <see cref="ResolveDepthInfo"/>). Book
    /// V1/V2 rank 99 — XG's opening book is rollout-derived, so it sits above
    /// XG Roller++ (22) yet below the explicit-rollout floor (100): a cached
    /// rollout whose parameters the file no longer records ranks under a
    /// rollout the file actually carries. Any unrecognised level ranks 0 (the
    /// floor, tied with nothing meaningful). "3-ply red" shares rank 3 with
    /// plain 3-ply: reduced variance narrows the candidate set, it does not
    /// deepen search. The rank lets downstream rendering flag out-of-order
    /// analysis across adjacent sorted-by-equity plays. Book enrichment
    /// (<see cref="ResolveDepthInfo"/>'s book branch) deliberately leaves the
    /// rank at 99 even when the entry's rollout parameters are recovered —
    /// <c>DepthRank</c> semantics stay stable across enrichment.
    /// </para>
    ///
    /// <para>
    /// The <see cref="AnalysisMode"/> × <see cref="AnalysisLevel"/> pair is
    /// the machine-usable taxonomy behind the three display forms — the
    /// single-sourced classification for depth filtering. The mode says how
    /// the numbers were produced (ply searches and the Roller family are
    /// <see cref="AnalysisMode.Evaluation"/>; the book codes are
    /// <see cref="AnalysisMode.BookRollout"/> — a cached rollout whose
    /// parameters live in the book database, not the file); the level is the
    /// evaluation level itself, or <see cref="AnalysisLevel.Unknown"/> where
    /// the file does not record one (both book codes here — enrichment in
    /// <see cref="ResolveDepthInfo"/> supplies the level when a book database
    /// is available; and the no-context rollout sentinel, whose known inner
    /// plies are stamped in <see cref="ResolveDepthInfo"/>'s rollout branch).
    /// "3-ply red" levels as <see cref="AnalysisLevel.Ply3"/> (same as plain
    /// 3-ply); an unrecognised code is <see cref="AnalysisMode.Unknown"/> +
    /// <see cref="AnalysisLevel.Unknown"/>.
    /// </para>
    ///
    /// <para>
    /// The book entry's stored rollout levels (<c>RolloutMovesLevel</c> /
    /// <c>RolloutCubeLevel</c>) use this same PLAYERLEVEL code space, so the
    /// book branch maps them through this switch too — the level taxonomy has
    /// exactly one decoding site.
    /// </para>
    /// </summary>
    private static (string Label, string Abbreviation, int Rank, AnalysisMode Mode, AnalysisLevel Level) LevelInfo(short level) => level switch
    {
        0    => ("1-ply",        "1-ply",     1,   AnalysisMode.Evaluation,  AnalysisLevel.Ply1),
        1    => ("2-ply",        "2-ply",     2,   AnalysisMode.Evaluation,  AnalysisLevel.Ply2),
        2    => ("3-ply",        "3-ply",     3,   AnalysisMode.Evaluation,  AnalysisLevel.Ply3),
        12   => ("3-ply red",    "3-ply red", 3,   AnalysisMode.Evaluation,  AnalysisLevel.Ply3),
        3    => ("4-ply",        "4-ply",     4,   AnalysisMode.Evaluation,  AnalysisLevel.Ply4),
        4    => ("5-ply",        "5-ply",     5,   AnalysisMode.Evaluation,  AnalysisLevel.Ply5),
        5    => ("6-ply",        "6-ply",     6,   AnalysisMode.Evaluation,  AnalysisLevel.Ply6),
        6    => ("7-ply",        "7-ply",     7,   AnalysisMode.Evaluation,  AnalysisLevel.Ply7),
        100  => ("Rollout",      "Ro",        100, AnalysisMode.Rollout,     AnalysisLevel.Unknown),
        1000 => ("XG Roller",    "R",         20,  AnalysisMode.Evaluation,  AnalysisLevel.XgRoller),
        1001 => ("XG Roller+",   "R+",        21,  AnalysisMode.Evaluation,  AnalysisLevel.XgRollerPlus),
        1002 => ("XG Roller++",  "R++",       22,  AnalysisMode.Evaluation,  AnalysisLevel.XgRollerPlusPlus),
        998  => ("Book V2",      "Book",      99,  AnalysisMode.BookRollout, AnalysisLevel.Unknown),
        999  => ("Book V1",      "Book",      99,  AnalysisMode.BookRollout, AnalysisLevel.Unknown),
        _    => ($"level-{level}", $"level-{level}", 0, AnalysisMode.Unknown, AnalysisLevel.Unknown),
    };

}