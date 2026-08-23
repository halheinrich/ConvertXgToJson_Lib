using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;

namespace ConvertXgToJson_Lib;

/// <summary>
/// One game of an <see cref="XgFileBuilder"/> match: records the game's
/// decisions in play order. Obtained from <see cref="XgFileBuilder.AddGame"/>;
/// every method returns the builder for chaining and validates eagerly, so
/// a mistake surfaces at the call that made it.
/// </summary>
/// <remarks>
/// The game tracks its position and cube through the decisions recorded:
/// a play advances the position (<see cref="AtPosition"/> overrides it),
/// and a taken double raises the cube to the taker. Checker-play
/// decisions come in four shapes — an analysed play
/// (<see cref="Play(XgPlayer, DiceRoll, BgDataTypes_Lib.Play)"/> and its
/// explicit-candidates overload), an unanalysed play
/// (<see cref="UnanalysedPlay"/>; the iterator skips it), a dance
/// (<see cref="Dance"/>) and XG's illegal-play marker
/// (<see cref="IllegalPlay"/>) — and cube decisions in two
/// (<see cref="CubeDecision"/>, <see cref="UnanalysedCube"/>). See
/// <see cref="XgFileBuilder"/> for the position frame and the validation
/// contract.
/// </remarks>
public sealed class XgGameBuilder
{
    private const int BoardSize = 26;
    private const int CheckersPerSide = 15;
    private const int BarPoint = 25;
    private const int MoveListSlots = 8;
    private const string PreRollDiceDisplay = "11";   // XG's placeholder on a pre-roll cube pane

    /// <summary>
    /// The shallowest cube analysis the builder can express as an analysed
    /// decision. XG's level code is <c>ply − 1</c>, and the iterator gates
    /// "is analysed" on <c>Level &gt; 0</c>, so a 1-ply cube pane (level 0)
    /// is indistinguishable from an unanalysed one downstream — the builder
    /// refuses it rather than let a decision vanish silently.
    /// </summary>
    private const int MinCubePly = 2;

    private readonly XgFileBuilder _match;
    private readonly List<SaveRecord> _records = [];
    private sbyte[] _position;           // player-1 frame
    private int _cubeSize = 1;
    private int _cubeOwnerSign;          // +1 player 1, −1 player 2, 0 centred
    private bool _ended;                 // a pass ended the game

    internal XgGameBuilder(
        XgFileBuilder match, int gameNumber, int score1, int score2, bool isCrawford, sbyte[] initialPosition)
    {
        _match = match;
        GameNumber = gameNumber;
        Score1 = score1;
        Score2 = score2;
        IsCrawford = isCrawford;
        _position = initialPosition;
        _records.Add(XgRecordFactory.GameHeader(
            (sbyte[])initialPosition.Clone(), score1, score2, isCrawford, gameNumber));
    }

    /// <summary>1-based game number within the match.</summary>
    public int GameNumber { get; }

    /// <summary>Player 1's score entering the game.</summary>
    public int Score1 { get; }

    /// <summary>Player 2's score entering the game.</summary>
    public int Score2 { get; }

    /// <summary>Whether this is the Crawford game.</summary>
    public bool IsCrawford { get; }

    /// <summary>Number of decisions recorded so far (plays and cube actions).</summary>
    public int DecisionCount => _records.Count - 1;

    internal int RecordCount => _records.Count;

    internal void AppendRecords(List<SaveRecord> records) => records.AddRange(_records);

    // ------------------------------------------------------------------ //
    //  Position
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Sets the position the next decision is made at — for a problem
    /// position that does not follow from the plays recorded so far. The
    /// board is in the player-1 frame described on <see cref="XgFileBuilder"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="position"/> is not a valid 26-element board.</exception>
    public XgGameBuilder AtPosition(IReadOnlyList<int> position)
    {
        _position = ValidatePosition(position, nameof(position));
        return this;
    }

    // ------------------------------------------------------------------ //
    //  Checker plays
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Records an analysed checker play whose analysis holds the played
    /// move as its only candidate — at 1-ply with equity 0, the minimal
    /// analysed decision (what XG stores for a forced play). The iterator
    /// emits it as a decision.
    /// </summary>
    /// <param name="player">Who rolled.</param>
    /// <param name="dice">The roll.</param>
    /// <param name="played">The play made, in the mover's numbering.</param>
    /// <exception cref="ArgumentException">The play cannot be made from the current position.</exception>
    /// <exception cref="InvalidOperationException">The game already ended on a pass.</exception>
    public XgGameBuilder Play(XgPlayer player, DiceRoll dice, BgDataTypes_Lib.Play played) =>
        Play(player, dice, played, [new XgPlayCandidate(played, equity: 0.0)]);

    /// <summary>
    /// Records an analysed checker play with an explicit candidate list.
    /// The played move need not be among the candidates (XG caps the list
    /// it stores); when it is, the decision's error is its equity loss
    /// against the best candidate.
    /// </summary>
    /// <param name="player">Who rolled.</param>
    /// <param name="dice">The roll.</param>
    /// <param name="played">The play made, in the mover's numbering.</param>
    /// <param name="candidates">The analysed candidates; at least one.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="candidates"/> is empty, or a play cannot be made
    /// from the current position.
    /// </exception>
    /// <exception cref="InvalidOperationException">The game already ended on a pass.</exception>
    public XgGameBuilder Play(XgPlayer player, DiceRoll dice, BgDataTypes_Lib.Play played, IReadOnlyList<XgPlayCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
            throw new ArgumentException(
                "An analysed play needs at least one candidate; use UnanalysedPlay for a play without analysis.",
                nameof(candidates));
        ThrowIfEnded();

        int sign = player.ToSign();
        var before = new PositionEngine { Points = (sbyte[])_position.Clone() };
        var applied = ApplyPlay(played, sign, nameof(played));

        int n = candidates.Count;
        var positionsPlayed = new PositionEngine[n];
        var moves = new sbyte[n][];
        var levels = new EvalLevel[n];
        var evals = new EvalResult[n];
        int paneLevel = 0;
        double bestEquity = double.NegativeInfinity;
        double? playedEquity = null;
        for (int i = 0; i < n; i++)
        {
            var candidate = candidates[i] ?? throw new ArgumentException(
                $"Candidate {i} is null.", nameof(candidates));
            var result = ApplyPlay(candidate.Play, sign, nameof(candidates));
            positionsPlayed[i] = new PositionEngine { Points = result.After };
            moves[i] = result.Encoded;
            levels[i] = new EvalLevel { Level = ToLevelCode(candidate.Ply) };
            evals[i] = new EvalResult { Equity = (float)candidate.Equity };
            paneLevel = Math.Max(paneLevel, ToLevelCode(candidate.Ply));
            bestEquity = Math.Max(bestEquity, candidate.Equity);
            if (playedEquity is null && result.After.AsSpan().SequenceEqual(applied.After))
                playedEquity = candidate.Equity;
        }

        // XG stores the played move's error as its (non-positive) equity
        // loss; a play outside the stored candidate list has no scored error.
        double moveError = playedEquity is { } e ? e - bestEquity : XgRecordFactory.UnanalysedError;

        _records.Add(new MoveRecord
        {
            EntryType = RecordType.Move,
            InitialPosition = before,
            FinalPosition = new PositionEngine { Points = applied.After },
            ActivePlayer = sign,
            MoveList = ToMoveList(applied.Encoded),
            Dice = [dice.High, dice.Low],
            CubeValue = CubeValueRaw,
            ErrorMove = moveError,
            CandidateCount = n,
            Analysis = new BestMoveAnalysis
            {
                Position = before,
                Dice = [dice.High, dice.Low],
                Level = paneLevel,
                Score = [Score1, Score2],
                Cube = _cubeSize,
                CubePosition = _cubeOwnerSign,
                Crawford = IsCrawford ? 1 : 0,
                Jacoby = _match.IsMoneySession && _match.IsJacoby ? 1 : 0,
                MoveCount = n,
                PositionsPlayed = positionsPlayed,
                Moves = moves,
                EvalLevels = levels,
                Evals = evals,
            },
            Played = true,
            MoveError = moveError,
            LuckError = XgRecordFactory.UnanalysedError,
            RolloutIndices = XgRecordFactory.NoRolloutIndices(),
            AnalyzeLevel = paneLevel,
            AnalyzeLevelLuck = -1,
            TutorMoveIndex = -1,
            ErrorTutorMove = XgRecordFactory.UnanalysedError,
            CommentIndex = -1,
        });
        _position = applied.After;
        return this;
    }

    /// <summary>
    /// Records a checker play that was made but never analysed — XG's
    /// never-analysed move pane. The position advances; the iterator skips
    /// the decision.
    /// </summary>
    /// <param name="player">Who rolled.</param>
    /// <param name="dice">The roll.</param>
    /// <param name="played">The play made, in the mover's numbering.</param>
    /// <exception cref="ArgumentException">The play cannot be made from the current position.</exception>
    /// <exception cref="InvalidOperationException">The game already ended on a pass.</exception>
    public XgGameBuilder UnanalysedPlay(XgPlayer player, DiceRoll dice, BgDataTypes_Lib.Play played)
    {
        ThrowIfEnded();
        int sign = player.ToSign();
        var before = new PositionEngine { Points = (sbyte[])_position.Clone() };
        var applied = ApplyPlay(played, sign, nameof(played));

        _records.Add(XgRecordFactory.UnanalysedMoveRecord(
            sign, before, finalPosition: new PositionEngine { Points = applied.After },
            moveList: ToMoveList(applied.Encoded), played: true,
            CubeValueRaw, dice.High, dice.Low));
        _position = applied.After;
        return this;
    }

    /// <summary>
    /// Records a dance: the player rolled and had no legal move. Stored as
    /// XG stores it — an analysed pane whose only candidate is the
    /// no-legal-move sentinel — so the iterator skips it silently and the
    /// position is unchanged.
    /// </summary>
    /// <param name="player">Who rolled.</param>
    /// <param name="dice">The roll.</param>
    /// <exception cref="InvalidOperationException">The game already ended on a pass.</exception>
    public XgGameBuilder Dance(XgPlayer player, DiceRoll dice) =>
        SentinelPlay(player, dice, [0, 0]);

    /// <summary>
    /// Records XG's illegal-play marker: the play recorded in the source
    /// was illegal, and XG stamped the marker in place of a candidate when
    /// it forced the next position. The iterator skips the decision with a
    /// warning; the tracked position is left unchanged, as the real result
    /// is unknowable.
    /// </summary>
    /// <param name="player">Who rolled.</param>
    /// <param name="dice">The roll.</param>
    /// <exception cref="InvalidOperationException">The game already ended on a pass.</exception>
    public XgGameBuilder IllegalPlay(XgPlayer player, DiceRoll dice) =>
        SentinelPlay(player, dice, [XgMoveEncoding.IllegalPlayMarker, XgMoveEncoding.IllegalPlayMarker]);

    private XgGameBuilder SentinelPlay(XgPlayer player, DiceRoll dice, ReadOnlySpan<sbyte> sentinelPair)
    {
        ThrowIfEnded();
        int sign = player.ToSign();
        var position = new PositionEngine { Points = (sbyte[])_position.Clone() };
        var candidate = TerminatorFilled();
        sentinelPair.CopyTo(candidate);

        _records.Add(new MoveRecord
        {
            EntryType = RecordType.Move,
            InitialPosition = position,
            FinalPosition = position,
            ActivePlayer = sign,
            MoveList = ToMoveList(TerminatorFilled()),
            Dice = [dice.High, dice.Low],
            CubeValue = CubeValueRaw,
            ErrorMove = XgRecordFactory.UnanalysedError,
            CandidateCount = 1,
            Analysis = new BestMoveAnalysis
            {
                Position = position,
                Dice = [dice.High, dice.Low],
                Level = ToLevelCode(1),
                Score = [Score1, Score2],
                Cube = _cubeSize,
                CubePosition = _cubeOwnerSign,
                Crawford = IsCrawford ? 1 : 0,
                Jacoby = _match.IsMoneySession && _match.IsJacoby ? 1 : 0,
                MoveCount = 1,
                PositionsPlayed = [position],
                Moves = [candidate],
                EvalLevels = [new EvalLevel { Level = ToLevelCode(1) }],
                Evals = [new EvalResult()],
            },
            Played = true,
            MoveError = XgRecordFactory.UnanalysedError,
            LuckError = XgRecordFactory.UnanalysedError,
            RolloutIndices = XgRecordFactory.NoRolloutIndices(),
            AnalyzeLevel = ToLevelCode(1),
            AnalyzeLevelLuck = -1,
            TutorMoveIndex = -1,
            ErrorTutorMove = XgRecordFactory.UnanalysedError,
            CommentIndex = -1,
        });
        return this;
    }

    // ------------------------------------------------------------------ //
    //  Cube actions
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Records an analysed cube decision by <paramref name="doubler"/> at
    /// the current position, with the played actions if any were recorded.
    /// The errors XG stores are derived from the equities and the actions.
    /// A take raises the cube to the taker for the decisions that follow; a
    /// pass ends the game.
    /// </summary>
    /// <param name="doubler">The player on roll, deciding whether to double.</param>
    /// <param name="equities">The cubeful equities, from the doubler's perspective.</param>
    /// <param name="ply">
    /// Evaluation depth, 2–7 plies. A 1-ply cube analysis cannot be
    /// expressed: XG's level code for it is 0, which the iterator's
    /// analysed-cube gate (<c>Level &gt; 0</c>) cannot tell from an
    /// unanalysed pane, so the builder refuses it rather than synthesize a
    /// decision that silently never emits.
    /// </param>
    /// <param name="doublerAction">
    /// The doubler's played action — <see cref="CubeAction.NoDouble"/> or
    /// <see cref="CubeAction.Double"/>; null when no action was recorded.
    /// </param>
    /// <param name="takerAction">
    /// The opponent's reply — <see cref="CubeAction.Take"/> or
    /// <see cref="CubeAction.Pass"/>; null when none was recorded. Only
    /// valid after a <see cref="CubeAction.Double"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ply"/> is outside 2–7.</exception>
    /// <exception cref="ArgumentException">
    /// An action is from the wrong half of the decision, or a reply is
    /// given without a double.
    /// </exception>
    /// <exception cref="InvalidOperationException">The game already ended on a pass.</exception>
    public XgGameBuilder CubeDecision(
        XgPlayer doubler, XgCubeEquities equities, int ply = MinCubePly,
        CubeAction? doublerAction = null, CubeAction? takerAction = null)
    {
        if (ply is < MinCubePly or > XgPlayCandidate.MaxPly)
            throw new ArgumentOutOfRangeException(nameof(ply), ply,
                $"A cube analysis depth must be {MinCubePly}–{XgPlayCandidate.MaxPly} plies " +
                "(a 1-ply cube pane is indistinguishable from an unanalysed one downstream).");
        int levelCode = ToLevelCode(ply);
        var (doubled, taken) = EncodeActions(doublerAction, takerAction);
        ThrowIfEnded();

        int sign = doubler.ToSign();
        var position = new PositionEngine { Points = (sbyte[])_position.Clone() };

        // XG scores the doubler's action as a non-negative loss and the
        // taker's as a non-positive one (corpus convention; the iterator
        // reads magnitudes). An unrecorded action has no error to score.
        double errorCube = doublerAction switch
        {
            CubeAction.NoDouble => Math.Max(0.0, equities.DoubleEquity - equities.NoDouble),
            CubeAction.Double => Math.Max(0.0, equities.NoDouble - equities.DoubleEquity),
            _ => XgRecordFactory.UnanalysedError,
        };
        double errorTake = takerAction switch
        {
            CubeAction.Take => -Math.Max(0.0, equities.DoubleTake - equities.DoubleDrop),
            CubeAction.Pass => -Math.Max(0.0, equities.DoubleDrop - equities.DoubleTake),
            _ => doublerAction is null ? XgRecordFactory.UnanalysedError : 0.0,
        };

        _records.Add(new CubeRecord
        {
            EntryType = RecordType.Cube,
            ActivePlayer = sign,
            Doubled = doubled,
            Taken = taken,
            BeaverAccepted = -1,
            RaccoonAccepted = -1,
            CubeValue = CubeValueRaw,
            Position = position,
            Analysis = new DoubleActionAnalysis
            {
                Position = position,
                Level = levelCode,
                Score = [Score1, Score2],
                Cube = _cubeSize,
                CubePosition = _cubeOwnerSign,
                Jacoby = _match.IsMoneySession && _match.IsJacoby ? 1 : 0,
                Crawford = (short)(IsCrawford ? 1 : 0),
                FlagDouble = (short)(equities.ShouldDouble ? 1 : 0),
                EquityNoDouble = (float)equities.NoDouble,
                EquityDoubleTake = (float)equities.DoubleTake,
                EquityDoubleDrop = (float)equities.DoubleDrop,
                LevelRequest = (short)levelCode,
            },
            ErrorCube = errorCube,
            DiceRolled = PreRollDiceDisplay,
            ErrorTake = errorTake,
            RolloutIndex = -1,
            AnalyzeLevel = levelCode,
            ErrorBeaver = XgRecordFactory.UnanalysedError,
            ErrorRaccoon = XgRecordFactory.UnanalysedError,
            AnalyzeLevelRequested = levelCode,
            TutorCube = -1,
            TutorTake = -1,
            ErrorTutorCube = XgRecordFactory.UnanalysedError,
            ErrorTutorTake = XgRecordFactory.UnanalysedError,
            CommentIndex = -1,
        });
        AdvanceCube(doubler, doublerAction, takerAction);
        return this;
    }

    /// <summary>
    /// Records a cube decision that was never analysed — XG's incidental
    /// cube pane, which it writes before every roll. The iterator skips
    /// it; the played actions still move the cube.
    /// </summary>
    /// <param name="doubler">The player on roll, deciding whether to double.</param>
    /// <param name="doublerAction">As on <see cref="CubeDecision"/>.</param>
    /// <param name="takerAction">As on <see cref="CubeDecision"/>.</param>
    /// <exception cref="ArgumentException">
    /// An action is from the wrong half of the decision, or a reply is
    /// given without a double.
    /// </exception>
    /// <exception cref="InvalidOperationException">The game already ended on a pass.</exception>
    public XgGameBuilder UnanalysedCube(
        XgPlayer doubler, CubeAction? doublerAction = null, CubeAction? takerAction = null)
    {
        var (doubled, taken) = EncodeActions(doublerAction, takerAction);
        ThrowIfEnded();

        _records.Add(XgRecordFactory.UnanalysedCubeRecord(
            doubler.ToSign(),
            new PositionEngine { Points = (sbyte[])_position.Clone() },
            CubeValueRaw, doubled, taken, PreRollDiceDisplay));
        AdvanceCube(doubler, doublerAction, takerAction);
        return this;
    }

    /// <summary>
    /// Maps the played actions onto XG's pane state
    /// (<see cref="CubeRecord.Doubled"/> / <see cref="CubeRecord.Taken"/>):
    /// the inverse of the iterator's <c>UserDoublerAction</c> /
    /// <c>UserTakerAction</c> mapping. −1 means "not recorded".
    /// </summary>
    private static (int Doubled, int Taken) EncodeActions(CubeAction? doublerAction, CubeAction? takerAction)
    {
        int doubled = doublerAction switch
        {
            null => -1,
            CubeAction.NoDouble => 0,
            CubeAction.Double => 1,
            _ => throw new ArgumentException(
                $"The doubler's action must be NoDouble or Double, not {doublerAction}.", nameof(doublerAction)),
        };
        int taken = takerAction switch
        {
            null => -1,
            CubeAction.Take => 1,
            CubeAction.Pass => 0,
            _ => throw new ArgumentException(
                $"The taker's action must be Take or Pass, not {takerAction}.", nameof(takerAction)),
        };
        if (taken >= 0 && doubled != 1)
            throw new ArgumentException(
                "A take or pass is a reply to a double; give doublerAction: CubeAction.Double.", nameof(takerAction));
        return (doubled, taken);
    }

    private void AdvanceCube(XgPlayer doubler, CubeAction? doublerAction, CubeAction? takerAction)
    {
        if (doublerAction != CubeAction.Double)
            return;
        switch (takerAction)
        {
            case CubeAction.Take:
                _cubeSize *= 2;
                _cubeOwnerSign = doubler.Opponent().ToSign();
                break;
            case CubeAction.Pass:
                _ended = true;
                break;
        }
    }

    private int CubeValueRaw => XgRecordFactory.EncodeCube(_cubeSize, _cubeOwnerSign);

    /// <summary>
    /// XG's PLAYERLEVEL code for an N-ply evaluation is <c>N − 1</c>
    /// (0 = 1-ply … 6 = 7-ply); <c>XgDecisionIterator.LevelInfo</c> is the
    /// decode direction.
    /// </summary>
    private static short ToLevelCode(int ply) => (short)(ply - 1);

    private void ThrowIfEnded()
    {
        if (_ended)
            throw new InvalidOperationException(
                $"Game {GameNumber} ended when the double was passed; no further decision can be recorded in it.");
    }

    // ------------------------------------------------------------------ //
    //  Plays against the tracked position
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Validates <paramref name="play"/> against the current position from
    /// the mover's side, applies it through the ecosystem's
    /// <see cref="BoardState"/>, and returns the resulting position in the
    /// player-1 frame plus the play in XG's candidate encoding.
    /// </summary>
    private (sbyte[] After, sbyte[] Encoded) ApplyPlay(BgDataTypes_Lib.Play play, int moverSign, string paramName)
    {
        if (play.Count > MoveListSlots / 2)
            throw new ArgumentException($"A play holds at most {MoveListSlots / 2} moves.", paramName);

        int[] moverPov = new int[BoardSize];
        for (int i = 0; i < BoardSize; i++)
            moverPov[i] = _position[i];
        if (moverSign < 0)
            moverPov = BackgammonConstants.Flip(moverPov);
        var board = BoardState.FromMop(moverPov);

        var encoded = TerminatorFilled();
        for (int i = 0; i < play.Count; i++)
        {
            var move = play[i];
            ValidateMove(board, move, paramName);
            board.ApplyMove(move);
            encoded[2 * i] = move.FrPt == BarPoint ? XgMoveEncoding.Bar : (sbyte)(move.FrPt - 1);
            encoded[2 * i + 1] = move.ToPt == 0 ? XgMoveEncoding.Terminator : (sbyte)(Math.Abs(move.ToPt) - 1);
        }

        var after = new sbyte[BoardSize];
        var afterMoverPov = moverSign < 0 ? BackgammonConstants.Flip(board.Points) : board.Points;
        for (int i = 0; i < BoardSize; i++)
            after[i] = (sbyte)afterMoverPov[i];
        return (after, encoded);
    }

    private static void ValidateMove(BoardState board, Move move, string paramName)
    {
        if (move.FrPt is < 1 or > BarPoint)
            throw new ArgumentException(
                $"Move {move}: the from-point must be 1–24 or {BarPoint} (the bar).", paramName);
        if (board.Points[move.FrPt] <= 0)
            throw new ArgumentException(
                $"Move {move}: the mover has no checker on point {move.FrPt} at this position.", paramName);
        if (board.Points[BarPoint] > 0 && move.FrPt != BarPoint)
            throw new ArgumentException(
                $"Move {move}: the mover has a checker on the bar, which must enter first.", paramName);

        if (move.ToPt == 0)
            return;   // bear-off; home-board and dice legality are not checked

        int to = Math.Abs(move.ToPt);
        if (to is < 1 or > 24)
            throw new ArgumentException(
                $"Move {move}: the to-point must be 1–24, 0 (bear-off), or its negative (hit).", paramName);
        if (to >= move.FrPt)
            throw new ArgumentException(
                $"Move {move}: checkers move toward point 1; {move.FrPt} → {to} goes the wrong way.", paramName);
        int occupant = board.Points[to];
        if (occupant <= -2)
            throw new ArgumentException(
                $"Move {move}: point {to} is blocked by {-occupant} opposing checkers.", paramName);
        bool blot = occupant == -1;
        if (move.ToPt < 0 && !blot)
            throw new ArgumentException(
                $"Move {move}: encoded as a hit, but there is no opposing blot on point {to}.", paramName);
        if (move.ToPt > 0 && blot)
            throw new ArgumentException(
                $"Move {move}: lands on an opposing blot on point {to}; encode the hit as ToPt -{to}.", paramName);
    }

    private static sbyte[] TerminatorFilled()
    {
        var slots = new sbyte[MoveListSlots];
        Array.Fill(slots, XgMoveEncoding.Terminator);
        return slots;
    }

    private static int[] ToMoveList(sbyte[] encoded)
    {
        var list = new int[MoveListSlots];
        for (int i = 0; i < MoveListSlots; i++)
            list[i] = encoded[i];
        return list;
    }

    /// <summary>
    /// Validates a caller-supplied board in the player-1 frame — 26 cells,
    /// counts within ±15, bars on the right side, at most 15 checkers per
    /// player — and returns it as XG's signed-byte array.
    /// </summary>
    internal static sbyte[] ValidatePosition(IReadOnlyList<int> position, string paramName)
    {
        ArgumentNullException.ThrowIfNull(position, paramName);
        if (position.Count != BoardSize)
            throw new ArgumentException(
                $"A position has {BoardSize} cells (got {position.Count}).", paramName);

        var points = new sbyte[BoardSize];
        int player1 = 0, player2 = 0;
        for (int i = 0; i < BoardSize; i++)
        {
            int count = position[i];
            if (count is < -CheckersPerSide or > CheckersPerSide)
                throw new ArgumentException(
                    $"Position[{i}] = {count} is not a valid checker count.", paramName);
            if (count > 0) player1 += count; else player2 -= count;
            points[i] = (sbyte)count;
        }
        if (points[0] > 0)
            throw new ArgumentException(
                "Position[0] is player 2's bar and cannot hold player 1 checkers.", paramName);
        if (points[BarPoint] < 0)
            throw new ArgumentException(
                $"Position[{BarPoint}] is player 1's bar and cannot hold player 2 checkers.", paramName);
        if (player1 > CheckersPerSide || player2 > CheckersPerSide)
            throw new ArgumentException(
                $"A player has at most {CheckersPerSide} checkers (player 1: {player1}, player 2: {player2}).",
                paramName);
        return points;
    }
}
