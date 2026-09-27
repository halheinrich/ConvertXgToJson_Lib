using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Iterates over XgFile records and yields one decision per analysed
/// checker play (MoveRecord) and cube decision (CubeRecord) XG has analysed.
/// Two surfaces over the same decisions:
/// <see cref="IterateDiagramRequests"/> yields the <see cref="BgDecisionData"/>
/// records — a <see cref="CheckerPlayDecision"/> or a <see cref="CubeDecision"/>
/// — and <see cref="Iterate"/> yields each as a flat <see cref="DecisionRow"/>
/// built from its record for one ranking.
///
/// <para>
/// <b>A record states what XG stores</b> (the arc's rule,
/// halheinrich/backgammon#273): the board, the cube, the session's terms and
/// standing, the roll, the candidates in XG's own order with their typed
/// depth facts, the played move or cube actions, and XG's error only where
/// the record states no move to derive it from. Everything those determine —
/// the XGID, the pip counts, the after-boards, the notation, the depth
/// labels and ranks, the best play and each play's error under a ranking —
/// BgDataTypes_Lib derives, so nothing here produces it.
/// </para>
///
/// <para>
/// <c>.xgp</c> position files yield <em>at most one</em> decision. See
/// <see cref="SelectXgpDecision"/> for the policy and its rationale.
/// </para>
/// </summary>
public static class XgDecisionIterator
{
    // -----------------------------------------------------------------------
    //  Public API — single file
    // -----------------------------------------------------------------------

    /// <summary>
    /// Yields every decision of a single already-parsed <see cref="XgFile"/>
    /// as a <see cref="DecisionRow"/>, built from its record for the options'
    /// ranking (<see cref="XgIteratorOptions.Ranking"/>; the default is
    /// <see cref="PlayRanking.Equity"/>) by <see cref="DecisionRow.From"/> —
    /// so a row and its record cannot disagree.
    ///
    /// <para>
    /// When <paramref name="sourceFile"/> names an <c>.xgp</c> position file,
    /// at most one decision is yielded — the analysed checker play if there is
    /// one, else the analysed cube. See <see cref="SelectXgpDecision"/>.
    /// </para>
    /// </summary>
    /// <param name="file">The parsed XG file.</param>
    /// <param name="sourceFile">
    /// Originating file name including extension (e.g. <c>"match.xg"</c> or
    /// <c>"position.xgp"</c>). Must be non-null — the iterator stamps a
    /// <see cref="DecisionId"/> on every decision, and the extension drives
    /// the discrimination between <see cref="XgDecisionId"/> and
    /// <see cref="XgpDecisionId"/>; a row's <see cref="DecisionRow.SourceFile"/>
    /// is the id's.
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
    /// the boundaries at which each predicate fires; the post-yield ones see
    /// the yielded row.
    /// </param>
    /// <param name="options">
    /// Optional producer configuration. See <see cref="XgIteratorOptions"/> —
    /// the ranking the rows are built for, and the opening book that enriches
    /// book-stamped candidates.
    /// </param>
    /// <param name="logger">
    /// Optional logger. A <c>Warning</c> names each decision skipped for a
    /// fault in XG's data — its played candidate stamped with XG's
    /// illegal-play marker (<see cref="SentinelKind.IllegalPlay"/>), or a
    /// candidate invalid from its own position — with the source file, game,
    /// move and roll. Defaults to <see cref="NullLogger.Instance"/> — silent,
    /// and suppressible by log level. Dances ((0, 0)) and positions that are
    /// not decisions skip silently: both are ordinary data, not errors.
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
        options ??= XgIteratorOptions.Default;
        var ranking = options.Ranking;
        return IterateCore(
            file, sourceFile, state, callbacks, options, logger,
            project: record => DecisionRow.From(record, ranking),
            view: static row => row);
    }

    /// <summary>
    /// Yields a <see cref="BgDecisionData"/> for every analysed checker play
    /// and cube decision in <paramref name="file"/> — a
    /// <see cref="CheckerPlayDecision"/> or a <see cref="CubeDecision"/>,
    /// built directly from the raw records.
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
    /// <see cref="Iterate"/>; the record's <see cref="BgDecisionData.SourceFile"/>
    /// is its id's.
    /// </param>
    /// <param name="state">
    /// Optional read-only observer. Behaves identically to
    /// <see cref="Iterate"/>: <see cref="XgIteratorState.MatchInfo"/> is
    /// populated before the first yield, <see cref="XgIteratorState.GameInfo"/>
    /// at each <see cref="GameHeaderRecord"/>.
    /// </param>
    /// <param name="callbacks">
    /// Optional skip predicates. See <see cref="XgIteratorCallbacks"/>; the
    /// post-yield ones see each record through its view for the options'
    /// ranking (<see cref="BgDecisionData.ViewFor"/>).
    /// </param>
    /// <param name="options">
    /// Optional producer configuration. Behaves identically to
    /// <see cref="Iterate"/> — see <see cref="XgIteratorOptions"/>. A record
    /// does not depend on the ranking; only the callbacks' views do.
    /// </param>
    /// <param name="logger">
    /// Optional logger. Behaves identically to <see cref="Iterate"/>.
    /// Defaults to <see cref="NullLogger.Instance"/>.
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
        options ??= XgIteratorOptions.Default;
        var ranking = options.Ranking;
        return IterateCore(
            file, sourceFile, state, callbacks, options, logger,
            project: static record => record,
            view: record => record.ViewFor(ranking));
    }

    /// <summary>
    /// Single entry point behind both decision surfaces. Walks the record
    /// stream once via <see cref="IterateAnalysedDecisions"/>, projecting each
    /// record to the surface's element, then — for <c>.xgp</c> position files
    /// only — narrows the result to a single decision through
    /// <see cref="SelectXgpDecision"/>.
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
        XgIteratorOptions options,
        ILogger? logger,
        Func<BgDecisionData, T> project,
        Func<T, IDecisionFilterData> view)
        where T : class
    {
        var decisions = IterateAnalysedDecisions(
            file, sourceFile, state, callbacks, options, logger, project, view);

        return IsXgpSource(sourceFile) ? SelectXgpDecision(decisions, view) : decisions;
    }

    /// <summary>
    /// Applies the <c>.xgp</c> single-decision emission policy: emit the
    /// checker play if the file has one the walk built; otherwise emit the
    /// analysed cube, if any; otherwise emit nothing.
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
    /// A play the walk skipped — a sentinel analysis (illegal play or dance),
    /// a position that is not a decision, a candidate invalid from its
    /// position — never reaches this filter:
    /// <see cref="IterateAnalysedDecisions"/> drops it upstream, so it does
    /// not suppress an otherwise analysed cube.
    /// </para>
    ///
    /// <para>
    /// Look-ahead is bounded and <c>.xgp</c>-scoped: at most one cube is held
    /// while scanning for a play, and the first play short-circuits the walk.
    /// <c>.xg</c> iteration bypasses this filter entirely and stays streaming.
    /// </para>
    /// </summary>
    private static IEnumerable<T> SelectXgpDecision<T>(IEnumerable<T> decisions, Func<T, IDecisionFilterData> view)
        where T : class
    {
        T? cube = null;

        foreach (var decision in decisions)
        {
            if (view(decision).Kind == DecisionKind.CheckerPlay)
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
    /// callbacks, game-header handling — and builds each decision's record,
    /// which <paramref name="project"/> turns into the surface's element and
    /// <paramref name="view"/> shows to the post-yield callbacks.
    ///
    /// <para>
    /// <b>What is not a record.</b> The emission rules sit here, at the one
    /// dispatch both surfaces share, so the two can never disagree about which
    /// source decisions become records: an unanalysed move or cube pane; a
    /// Crawford game's cube pane (<see cref="AdmitsCubeDecision"/>); a
    /// position that is not a decision, for either kind; and, in
    /// <see cref="ReadCheckerPlay"/>, XG's two non-play sentinels and a checker
    /// play holding a candidate invalid from its own position.
    /// </para>
    /// </summary>
    private static IEnumerable<T> IterateAnalysedDecisions<T>(
        XgFile file,
        string sourceFile,
        XgIteratorState? state,
        XgIteratorCallbacks? callbacks,
        XgIteratorOptions options,
        ILogger? logger,
        Func<BgDecisionData, T> project,
        Func<T, IDecisionFilterData> view)
        where T : class
    {
        logger ??= NullLogger.Instance;

        var context = new MatchContext(file.Records, file.Comments);

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
            // Always update context — headers must be processed even when
            // skipping so that the standing, game number and cube state stay
            // correct.
            context.Update(record);

            if (record is GameHeaderRecord)
            {
                var gameInfo = context.GameInfo!;

                if (state != null)
                    state.GameInfo = gameInfo;

                skipCurrentGame = callbacks?.SkipGameAt?.Invoke(gameInfo) == true;
                continue;
            }

            if (skipCurrentGame)
                continue;

            BgDecisionData? decision = record switch
            {
                MoveRecord move when IsAnalysed(move) =>
                    ReadCheckerPlay(move, context, sourceFile, file.Rollouts, options.OpeningBook, logger),
                CubeRecord cube when IsCubeDecision(cube, context) =>
                    BuildCube(cube, context, sourceFile, file.Rollouts),
                _ => null,
            };
            if (decision is null)
                continue;

            var element = project(decision);
            yield return element;

            if (callbacks is null)
                continue;
            var filterView = view(element);
            if (callbacks.StopMatchAfter?.Invoke(filterView) == true)
                yield break;
            if (callbacks.StopGameAfter?.Invoke(filterView) == true)
                skipCurrentGame = true;
        }
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
    //  Move record — the checker play
    // -----------------------------------------------------------------------

    /// <summary>
    /// The checker-play record of an analysed move record, or
    /// <see langword="null"/> when the source decision builds none. Four
    /// kinds of analysed move build no record, each passed by without
    /// catching anything:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Not a decision position</b> — a side has borne off all its
    ///     checkers (Hal's ruling of 2026-09-27, halheinrich/backgammon#273).
    ///     Legitimate XG data, simply not a decision in the record model:
    ///     passed by silently, as ordinary filtering, as a cube decision's is
    ///     (<see cref="IsCubeDecision"/>). The rule is
    ///     <see cref="PositionData.IsDecisionPosition"/>'s, asked here and not
    ///     restated.
    ///   </description></item>
    ///   <item><description>
    ///     <b>A non-play sentinel</b> — XG's illegal-play marker, skipped with
    ///     a warning, or a dance, skipped silently (see
    ///     <see cref="ClassifySentinelAnalysis"/>).
    ///   </description></item>
    ///   <item><description>
    ///     <b>No roll</b> — a move record whose dice are both 0 poses no
    ///     checker-play decision.
    ///   </description></item>
    ///   <item><description>
    ///     <b>A corrupt candidate</b> — a candidate invalid from the decision's
    ///     own position, which the record would refuse (Hal's ruling of
    ///     2026-09-25 on invalid plays, applied to stored candidates). Found
    ///     with <see cref="BoardState.TryApplyPlay"/>, the non-throwing door
    ///     to the same play rule the record's check runs, on a fresh board per
    ///     candidate — the method applies a valid play, so a shared board would
    ///     test later candidates from the wrong position. The decision is
    ///     skipped the way an illegal-play marker is, with a warning naming the
    ///     file, game, move, roll and every invalid candidate.
    ///   </description></item>
    /// </list>
    /// Any other refusal from BgDataTypes_Lib is not caught: it fails loud.
    /// </summary>
    private static CheckerPlayDecision? ReadCheckerPlay(
        MoveRecord move, MatchContext ctx, string sourceFile, List<RolloutContext> rollouts,
        OpeningBook? book, ILogger logger)
    {
        var board = OnRollBoard(move.InitialPosition, move.ActivePlayer);
        if (!PositionData.IsDecisionPosition(board))
            return null;

        // Skip XG's non-play sentinels before they reach the translator:
        // an illegal-play marker historically crashed the leaves, a dance
        // rendered as "1/1" garbage. Illegal plays are worth a contextual
        // warning; dances are normal and skip silently.
        switch (ClassifySentinelAnalysis(move.Analysis))
        {
            case SentinelKind.IllegalPlay:
                logger.LogWarning(
                    "Illegal play in {SourceFile}, game {Game}, move {MoveNumber}, roll {Roll}",
                    sourceFile, ctx.GameNumber, ctx.MoveNumber, DiceToInt(move.Dice));
                return null;
            case SentinelKind.Dance:
                return null;
        }

        if (!StatesRoll(move))
            return null;

        var plays = CandidatePlays(move.Analysis, board);
        var invalid = InvalidCandidates(board, plays);
        if (invalid.Count > 0)
        {
            logger.LogWarning(
                "Corrupt candidate in {SourceFile}, game {Game}, move {MoveNumber}, roll {Roll}: invalid from the decision's position: {InvalidCandidates}",
                sourceFile, ctx.GameNumber, ctx.MoveNumber, DiceToInt(move.Dice),
                string.Join("; ", invalid.Select(i => $"candidate {i + 1} ({plays[i].ToNotation()})")));
            return null;
        }

        return BuildCheckerPlay(move, ctx, sourceFile, board, plays, rollouts, book);
    }

    /// <summary>
    /// Every candidate's play, in XG's order, translated from XG's encoding
    /// against the decision's <paramref name="board"/> (the mover's frame).
    /// A candidate is a slot the analysis carries a move encoding and an
    /// evaluation for, within its move count.
    /// </summary>
    private static List<Play> CandidatePlays(BestMoveAnalysis analysis, BoardPosition board)
    {
        int count = CandidateCount(analysis);
        var plays = new List<Play>(count);
        for (int i = 0; i < count; i++)
            plays.Add(XgMoveTranslator.Translate(analysis.Moves[i], board));
        return plays;
    }

    /// <summary>The number of candidates an analysis carries: the slots with a move encoding and an evaluation, within its move count.</summary>
    private static int CandidateCount(BestMoveAnalysis analysis) =>
        Math.Min(analysis.MoveCount, Math.Min(analysis.Evals.Length, analysis.Moves.Length));

    /// <summary>
    /// The indices of the candidates invalid from <paramref name="board"/>,
    /// each tested by <see cref="BoardState.TryApplyPlay"/> on a fresh board.
    /// </summary>
    private static List<int> InvalidCandidates(BoardPosition board, List<Play> plays)
    {
        var invalid = new List<int>();
        for (int i = 0; i < plays.Count; i++)
            if (!new BoardState(board).TryApplyPlay(plays[i]))
                invalid.Add(i);
        return invalid;
    }

    /// <summary>
    /// Builds the checker-play record: the position, the session and the
    /// descriptive facts, the roll as rolled, the candidates in XG's order —
    /// each with its typed depth facts, equity and probabilities — the
    /// played candidate, and XG's error for a played move not among them.
    /// </summary>
    private static CheckerPlayDecision BuildCheckerPlay(
        MoveRecord move, MatchContext ctx, string sourceFile, BoardPosition board,
        List<Play> plays, List<RolloutContext> rollouts, OpeningBook? book)
    {
        var analysis = move.Analysis;
        var seat = MatchContext.SeatOf(move.ActivePlayer);
        var id = BuildDecisionId(sourceFile, ctx.GameNumber, ctx.MoveNumber, isCube: false);

        var candidates = new List<PlayCandidate>(plays.Count);
        for (int i = 0; i < plays.Count; i++)
        {
            var eval = analysis.Evals[i];
            // Each candidate enriches from its own book entry (keyed by its
            // own resulting position) — a book-analysed decision can mix
            // book-stamped and evaluated candidates, and different candidates
            // resolve to different book rollouts.
            var depth = i < analysis.EvalLevels.Length
                ? XgDepthFacts.Resolve(
                    analysis.EvalLevels[i].Level,
                    rolloutIndex: i < move.RolloutIndices.Length ? move.RolloutIndices[i] : -1,
                    rollouts,
                    LookupBookEntry(book, analysis.EvalLevels[i].Level, analysis, i, move.ActivePlayer, ctx))
                : XgDepthFacts.NotRecorded;
            candidates.Add(new PlayCandidate
            {
                Play = plays[i],
                AnalysisMode = depth.Mode,
                AnalysisLevel = depth.Level,
                RolloutTrials = depth.RolloutTrials,
                BookEdition = depth.BookEdition,
                UnrecognizedLevelCode = depth.UnrecognizedLevelCode,
                Equity = eval.Equity,
                WinPct = eval.WinSingle,
                WinGammonPct = eval.WinGammon,
                WinBgPct = eval.WinBackgammon,
                LoseGammonPct = eval.LoseGammon,
                LoseBgPct = eval.LoseBackgammon,
            });
        }

        int? userPlayIndex = FindUserPlayIndex(analysis, plays.Count, move.FinalPosition);

        return new CheckerPlayDecision
        {
            Id = id,
            Position = new PositionData
            {
                Mop = board,
                CubeSize = ctx.CubeValue,
                CubeOwner = CubeOwnerFor(ctx.CubePosition, seat),
                Session = ctx.SessionFor(seat),
            },
            Descriptive = Descriptive(id, ctx, seat, move.CommentIndex, move.Flagged),
            Decision = new CheckerPlayDecisionData
            {
                Dice = [move.Dice[0], move.Dice[1]],
                Plays = candidates,
                UserPlayIndex = userPlayIndex,
                // XG's error for the played move is stored only where the
                // record states no played candidate to derive it from.
                UnlistedPlayError = userPlayIndex is null && move.MoveError > -999.0
                    ? Math.Abs(move.MoveError)
                    : null,
            },
        };
    }

    // -----------------------------------------------------------------------
    //  Cube record — the cube decision
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds the cube-decision record of a cube record the dispatch admitted
    /// (<see cref="IsCubeDecision"/>), the doubler's view: the position, the
    /// session and the descriptive facts, the analysis's typed depth facts,
    /// equities and probabilities, the played actions, and XG's error for a
    /// half whose action the record does not state.
    /// </summary>
    private static CubeDecision BuildCube(
        CubeRecord cube, MatchContext ctx, string sourceFile, List<RolloutContext> rollouts)
    {
        var analysis = cube.Analysis;
        var board = OnRollBoard(cube.Position, cube.ActivePlayer);
        var seat = MatchContext.SeatOf(cube.ActivePlayer);
        var id = BuildDecisionId(sourceFile, ctx.GameNumber, ctx.MoveNumber + 1, isCube: true);

        // No book entry: the cube-row keying convention against the opening
        // book is unproven (see LookupBookEntry), so a book-stamped cube states
        // its bare book facts by design. Depth resolves from Level — the level
        // that produced the stored equities — not the LevelRequest setting; the
        // two diverge on 59% of analysed corpus cubes (halheinrich/backgammon#161).
        var depth = XgDepthFacts.Resolve(analysis.Level, cube.RolloutIndex, rollouts);

        // Resolved once: the doubler half is both a stamped field and the
        // "a double was offered" gate on the taker's error, so the two can
        // never disagree about whether this record holds a double.
        CubeAction? doublerAction = UserDoublerActionOf(cube);
        CubeAction? takerAction = UserTakerActionOf(cube);

        return new CubeDecision
        {
            Id = id,
            Position = new PositionData
            {
                Mop = board,
                CubeSize = CubeValueActual(cube.CubeValue),
                CubeOwner = CubeOwnerFor(Math.Sign(cube.CubeValue), seat),
                Session = ctx.SessionFor(seat),
            },
            Descriptive = Descriptive(id, ctx, seat, cube.CommentIndex, cube.Flagged),
            Decision = new CubeDecisionData
            {
                AnalysisMode = depth.Mode,
                AnalysisLevel = depth.Level,
                RolloutTrials = depth.RolloutTrials,
                BookEdition = depth.BookEdition,
                UnrecognizedLevelCode = depth.UnrecognizedLevelCode,
                NoDoubleEquity = analysis.EquityNoDouble,
                DoubleTakeEquity = analysis.EquityDoubleTake,
                CubelessNoDoubleEquity = analysis.EvalNoDouble.Equity,
                CubelessDoubleTakeEquity = analysis.EvalDoubleTake.Equity,
                WinPctAfterNoDouble = analysis.EvalNoDouble.WinSingle,
                GammonPctAfterNoDouble = analysis.EvalNoDouble.WinGammon,
                BgPctAfterNoDouble = analysis.EvalNoDouble.WinBackgammon,
                LoseGammonPctAfterNoDouble = analysis.EvalNoDouble.LoseGammon,
                LoseBgPctAfterNoDouble = analysis.EvalNoDouble.LoseBackgammon,
                WinPctAfterDoubleTake = analysis.EvalDoubleTake.WinSingle,
                GammonPctAfterDoubleTake = analysis.EvalDoubleTake.WinGammon,
                BgPctAfterDoubleTake = analysis.EvalDoubleTake.WinBackgammon,
                LoseGammonPctAfterDoubleTake = analysis.EvalDoubleTake.LoseGammon,
                LoseBgPctAfterDoubleTake = analysis.EvalDoubleTake.LoseBackgammon,
                // Temporary arc debt (Hal's ruling on halheinrich/backgammon#273,
                // 2026-09-27): XG stores no such value, so the record's claim that
                // it is stored is a producer-model gap. The 0 every converted
                // record has carried stays until BgDataTypes_Lib's next leg of
                // halheinrich/backgammon#273 removes the field; it is not derived
                // here (the figure is booked as halheinrich/backgammon#288).
                ProbOfOpponentErrorJustifyingDouble = 0,
                UserDoublerAction = doublerAction,
                UserTakerAction = takerAction,
                // XG's error for a half is stored only where the record states
                // no action of that half to derive it from; the taker's exists
                // only once a double was offered.
                UnstatedDoublerActionError = doublerAction is null && cube.ErrorCube > -999.0
                    ? Math.Abs(cube.ErrorCube)
                    : null,
                UnstatedTakerActionError = doublerAction is CubeAction.Double && takerAction is null && cube.ErrorTake > -999.0
                    ? Math.Abs(cube.ErrorTake)
                    : null,
            },
        };
    }

    // -----------------------------------------------------------------------
    //  Both kinds — the descriptive facts
    // -----------------------------------------------------------------------

    /// <summary>
    /// The descriptive facts of a decision taken from <paramref name="seat"/>:
    /// the players' names, whether its game started from the standard
    /// position, its comment and its flag. A standalone position — an
    /// <see cref="XgpDecisionId"/> — belongs to no game, so states no standard
    /// start (halheinrich/backgammon#124); the record holds the two to
    /// agreement, and the start is read off the id here so they cannot differ.
    /// </summary>
    private static DescriptiveData Descriptive(DecisionId id, MatchContext ctx, Seat seat, int commentIndex, bool flagged) => new()
    {
        OnRollName = ctx.NameOf(seat),
        OpponentName = ctx.NameOf(seat == Seat.Player1 ? Seat.Player2 : Seat.Player1),
        IsStandardStart = id is XgDecisionId ? ctx.GameInfo!.IsStandardStart : null,
        Comment = ctx.CommentAt(commentIndex),
        Flagged = flagged,
    };

    // -----------------------------------------------------------------------
    //  Cube record — played action
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps <see cref="CubeRecord.Doubled"/> onto the doubler-half
    /// <see cref="CubeAction"/> the player on roll actually played, or
    /// <see langword="null"/> when the record holds no played cube action.
    /// Single source of the "a double was offered" test for this producer —
    /// the taker's error gate reads it too.
    ///
    /// <para>
    /// <c>Doubled</c> is a pane-state field, not a two-valued flag: only
    /// <c>1</c> (doubled) and <c>0</c> (declined to double, rolled) record
    /// an action that was played. Neither negative value does. <c>-2</c> is
    /// the incidental cube pane XG writes beside a checker play, and
    /// <c>-1</c> is the pane XG writes where a game ended with no cube
    /// action taken — every analysed <c>-1</c> record in the local corpus
    /// is the last record of its game, each followed by a game footer whose
    /// <see cref="GameFooterRecord.Termination"/> is ≥ 100, XG's
    /// by-resignation encoding. <c>-1</c> is also the pane state
    /// <see cref="XgpExporter"/> writes for a curated <c>.xgp</c> cube
    /// problem, which likewise records nothing played. Both map to null —
    /// the carrier's "played action not recorded" — rather than being
    /// flattened into <see cref="CubeAction.NoDouble"/>, which would assert
    /// a decision the player never made.
    /// </para>
    ///
    /// <para>
    /// Deliberately independent of <see cref="CubeRecord.ErrorCube"/>: the
    /// played action is a fact about the game, its error a fact about the
    /// analysis, and a decision can be one without the other. The corpus
    /// makes the two look interchangeable — <c>ErrorCube</c> carries its
    /// −1000 not-analysed sentinel on exactly the <c>Doubled == -1</c>
    /// records — but that is a consequence of there being no action to
    /// score, not evidence that an unscored action is an unknown one.
    /// Keying off <c>ErrorCube</c> would make the null rule an accident of
    /// this corpus.
    /// </para>
    /// </summary>
    private static CubeAction? UserDoublerActionOf(CubeRecord cube) => cube.Doubled switch
    {
        1 => CubeAction.Double,
        0 => CubeAction.NoDouble,
        _ => null,
    };

    /// <summary>
    /// Maps <see cref="CubeRecord.Taken"/> onto the taker-half
    /// <see cref="CubeAction"/> the opponent actually played, or
    /// <see langword="null"/> when no response is recorded.
    ///
    /// <para>
    /// Gated on the doubler half rather than on <c>Taken</c> alone. XG
    /// leaves <c>Taken</c> at its <c>-1</c> "no response" value on every
    /// undoubled record, so reading it in isolation would happen to satisfy
    /// the carrier's cross-half producer contract — a recorded taker
    /// response implies the doubler doubled — by accident of the source
    /// data rather than by anything this producer guarantees.
    /// </para>
    ///
    /// <para>
    /// A recorded double with no response (<c>-1</c>) is a real shape and
    /// stays null: the corpus holds a handful, each the last record of a
    /// game that ended by resignation before the opponent formally
    /// answered. That is exactly the case the per-half carrier exists to
    /// express — doubler recorded, taker not.
    /// </para>
    ///
    /// <para>
    /// A beaver (<c>2</c>) maps to <see cref="CubeAction.Take"/>. The taker
    /// half models the accept-or-decline axis and a beaver accepts; the
    /// immediate redouble it carries is a separate action, recorded
    /// separately in <see cref="CubeRecord.BeaverAccepted"/>. Null would
    /// assert "no response recorded", which is false. No beaver appears
    /// anywhere in the local corpus, so this arm is reasoned rather than
    /// pinned by a fixture.
    /// </para>
    /// </summary>
    private static CubeAction? UserTakerActionOf(CubeRecord cube) =>
        UserDoublerActionOf(cube) is not CubeAction.Double
            ? null
            : cube.Taken switch
            {
                1 or 2 => CubeAction.Take,
                0 => CubeAction.Pass,
                _ => null,
            };

    // -----------------------------------------------------------------------
    //  Board helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// A record's position — stored in XG's player-1 frame — seen from the
    /// player on roll, the frame of <see cref="PositionData.Mop"/>: as stored
    /// for player 1, flipped by <see cref="BoardPosition.Flipped"/> for
    /// player 2.
    /// </summary>
    internal static BoardPosition OnRollBoard(PositionEngine position, int activePlayer)
    {
        var stored = position.ToBoardPosition();
        return MatchContext.SeatOf(activePlayer) == Seat.Player1 ? stored : stored.Flipped();
    }

    /// <summary>
    /// Returns the <see cref="CubeOwner"/> from the player on roll's side.
    /// <paramref name="cubePosition"/> uses XG's raw sign convention (+1 =
    /// player 1 owns, −1 = player 2 owns, 0 = centred).
    /// </summary>
    private static CubeOwner CubeOwnerFor(int cubePosition, Seat onRoll)
    {
        if (cubePosition == 0) return CubeOwner.Centered;
        var owner = cubePosition > 0 ? Seat.Player1 : Seat.Player2;
        return owner == onRoll ? CubeOwner.OnRoll : CubeOwner.Opponent;
    }

    /// <summary>
    /// Identifies which candidate is the move the player actually made, by
    /// resulting position: the candidate whose position after the play, as
    /// XG stores it (<see cref="BestMoveAnalysis.PositionsPlayed"/>), is the
    /// position XG stores after the move (<see cref="MoveRecord.FinalPosition"/>)
    /// — the two compared as <see cref="BoardPosition"/> values, the one "same
    /// position". <see langword="null"/> when no candidate reaches it: the
    /// player chose a move XG did not list, or made none (a saved position
    /// with dice but no move stores an empty final position).
    /// </summary>
    private static int? FindUserPlayIndex(BestMoveAnalysis analysis, int candidateCount, PositionEngine finalPosition)
    {
        if (!finalPosition.TryToBoardPosition(out var played))
            return null;
        int scan = Math.Min(candidateCount, analysis.PositionsPlayed.Length);
        for (int i = 0; i < scan; i++)
            if (analysis.PositionsPlayed[i].TryToBoardPosition(out var reached) && reached == played)
                return i;
        return null;
    }

    // -----------------------------------------------------------------------
    //  Match info helper
    // -----------------------------------------------------------------------

    /// <summary>
    /// Scans <paramref name="file"/>'s records for the first
    /// <see cref="MatchHeaderRecord"/> and returns an <see cref="XgMatchInfo"/>
    /// projected from it, or <c>null</c> if no match header is present.
    /// Callers must treat <c>null</c> as "match header unreadable".
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the header states terms this reader will not read (see
    /// <see cref="XgMatchInfo"/>).
    /// </exception>
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
    //  Book enrichment
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves a book-stamped checker-play candidate against the caller's
    /// loaded <see cref="OpeningBook"/>, or returns null when enrichment does
    /// not apply — which is the normal case, checked cheapest-first: no book
    /// supplied, the candidate is not V2-book-stamped
    /// (<paramref name="evalLevel"/> ≠ 998), no stored resulting position, a
    /// decision context outside the proven keying conventions, or a plain
    /// lookup miss. A null return leaves the candidate's bare book facts —
    /// <see cref="AnalysisMode.BookRollout"/>, <see cref="AnalysisLevel.Unknown"/>
    /// and its edition — in <see cref="XgDepthFacts.Resolve"/>.
    ///
    /// <para>
    /// The key is exactly the pane data session 1 proved bitwise:
    /// <c>PositionsPlayed[i]</c> (the candidate's resulting position,
    /// player-1-relative) plus the decision's session, normalized by the
    /// <see cref="OpeningBookKey"/> factories (perspective flip, away-pair
    /// orientation, Jacoby / Crawford). One context guard scopes the lookup
    /// to what those factories cover: the cube must be centred at 1 (the
    /// factories' only supported cube context — the book's turned-cube owner
    /// sign is unverified).
    /// </para>
    ///
    /// <para>
    /// Checker-play candidates only. Cube decisions never reach this helper:
    /// the book's cube-decision keying convention is unproven — the fixture
    /// corpus contains no book-stamped cube decision to pin it against — so
    /// cube book stamps degrade rather than guess (see the cube builder).
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
        if (book == null || evalLevel != XgDepthFacts.BookV2Code)
            return null;
        if (candidateIndex >= analysis.PositionsPlayed.Length)
            return null;
        if (ctx.CubeValue != 1 || ctx.CubePosition != 0)
            return null;

        var positionPlayed = analysis.PositionsPlayed[candidateIndex];
        var key = ctx.SessionFor(MatchContext.SeatOf(activePlayer)).Match(
            money => OpeningBookKey.ForMoneyPlay(positionPlayed, activePlayer, money.Terms.IsJacoby),
            match => OpeningBookKey.ForMatchPlay(
                positionPlayed, activePlayer, match.OnRollNeeds, match.OpponentNeeds, match.IsCrawford));

        return book.TryGetEntry(key, out var entry) ? entry : null;
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds the <see cref="DecisionId"/> stamped onto every record by
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
    /// <paramref name="moveNumber"/>, so a cube decision numbers as the move
    /// its turn goes on to play.
    /// </para>
    ///
    /// <para>
    /// Extension match is case-insensitive invariant — <c>.XG</c> and
    /// <c>.xg</c> on Windows clones both route to the <see cref="XgDecisionId"/>
    /// shape. Any other extension is a producer-side contract violation;
    /// <see cref="Iterate"/> / <see cref="IterateDiagramRequests"/> validate
    /// <c>sourceFile</c> non-null at iteration entry, but the extension is
    /// only verified once a decision reaches a builder.
    /// </para>
    ///
    /// <para>
    /// Internal-not-private so test code can drive the unknown-extension
    /// path directly without synthesizing a full <see cref="XgFile"/>.
    /// Parallel to <see cref="IsSentinelOnlyAnalysis"/>.
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

    /// <summary>
    /// Whether a move record carries an analysis at all: a move count and an
    /// evaluation. The first of the move emission rules the dispatch applies.
    /// </summary>
    private static bool IsAnalysed(MoveRecord move) =>
        move.Analysis.MoveCount > 0 && move.Analysis.Evals.Length > 0;

    /// <summary>
    /// Returns <c>true</c> when any candidate in <paramref name="analysis"/>'s
    /// <c>Moves</c> array is a known XG non-play sentinel. The walk skips
    /// these decisions before any candidate reaches
    /// <see cref="XgMoveTranslator.Translate"/>: feeding a leaf the sentinel
    /// encoding has historically produced an
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
    /// candidates, driving both the walk's skip and (for
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

    // LevelRequest is a setting (what the user asked XG to run); Level is
    // provenance (what produced the pane's stored equities). Gate on Level:
    // what the > 0 predicate excludes is, corpus-measured, the all-zero
    // never-written incidental pane (Level == 0), plus the format-real but
    // marginal queued-never-ran -100 sentinel. Full model with the census:
    // INSTRUCTIONS.md, "Level semantics: LevelRequest vs Level"
    // (halheinrich/backgammon#161).
    private static bool IsAnalysed(CubeRecord cube) =>
        cube.Analysis.Level > 0;

    /// <summary>
    /// Whether a cube pane in the current game can be a decision at all —
    /// the emission rule the dispatch applies beside <see cref="IsAnalysed(CubeRecord)"/>.
    /// In the Crawford game it cannot: doubling is prohibited there, so the
    /// cube pane XG writes for it — and does analyse; the corpus carries such
    /// panes, so this is a format fact the converter drops, not an anomaly
    /// worth a log line — describes no decision. The rule's owner is the wire
    /// type: a <see cref="CubeDecision"/> refuses a Crawford position
    /// (halheinrich/backgammon#201), and this predicate is what keeps the walk
    /// from reaching that guard; it does not restate the rule. Skipping
    /// changes nothing else: a cube's id reads <c>ctx.MoveNumber + 1</c>
    /// without incrementing the counter, so the following plays number as
    /// they always did, and the stop callbacks see only emitted decisions.
    /// </summary>
    private static bool AdmitsCubeDecision(MatchContext ctx) =>
        !ctx.IsCrawford;

    /// <summary>
    /// The cube emission gate the dispatch applies: the record is a decision
    /// when it is analysed, the game admits one, and its position is a
    /// decision position — a side that has borne off all its checkers ends the
    /// game, so no decision is made (Hal's ruling of 2026-09-27,
    /// halheinrich/backgammon#273). That last is ordinary filtering, passed by
    /// silently, and the rule is <see cref="PositionData.IsDecisionPosition"/>'s,
    /// asked of the stored position — its answer does not depend on the frame
    /// — and not restated. Internal-not-private for the same reason as
    /// <see cref="IsSentinelOnlyAnalysis"/>: test code that pairs raw
    /// <c>CubeRecord</c>s with iterator output mirrors the emission filter
    /// through this predicate, driving a <see cref="MatchContext"/> record
    /// by record as the walk does, rather than re-implementing any part.
    /// </summary>
    internal static bool IsCubeDecision(CubeRecord cube, MatchContext ctx) =>
        IsAnalysed(cube) && AdmitsCubeDecision(ctx)
        && PositionData.IsDecisionPosition(cube.Position.ToBoardPosition());

    /// <summary>
    /// Whether a move record states a roll: a move record whose dice are both
    /// 0 poses no checker-play decision. One of the move emission rules
    /// <see cref="ReadCheckerPlay"/> applies.
    /// </summary>
    private static bool StatesRoll(MoveRecord move) => DiceToInt(move.Dice) != 0;

    /// <summary>XG's roll as a two-digit number in rolled order — the form the warnings name it by; 0 for no roll.</summary>
    private static int DiceToInt(int[] dice) =>
        dice.Length >= 2 ? dice[0] * 10 + dice[1] : 0;

    /// <summary>
    /// Converts a raw XG cube value (signed log2 encoding) to the actual cube size.
    /// Raw value 0 means the cube is centred at 1. Positive/negative values encode
    /// which player owns the cube; the magnitude is log2 of the cube size.
    /// </summary>
    internal static int CubeValueActual(int raw) =>
        raw == 0 ? 1 : (int)Math.Pow(2, Math.Abs(raw));
}
