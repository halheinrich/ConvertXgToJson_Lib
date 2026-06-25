using ConvertXgToJson_Lib.Models;
using Microsoft.Extensions.Logging;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Behavioural tests for the illegal-play path through
/// <see cref="XgDecisionIterator.Iterate"/> /
/// <see cref="XgDecisionIterator.IterateDiagramRequests"/>: the decision
/// carrying XG's illegal-play marker is skipped (never reaching a move leaf,
/// so the historical <see cref="IndexOutOfRangeException"/> can't fire), the
/// decisions surrounding it still emit, and a contextual <c>Warning</c> is
/// logged. Dance ((0, 0)) and the regression cases stay covered by
/// <see cref="XgDecisionIteratorSentinelTests"/>; this file adds the broadened
/// <c>(-100, X)</c> marker and the logging contract.
/// </summary>
[Collection("FileIO")]
public class XgDecisionIteratorIllegalPlayTests
{
    // The marker XG stamps at the user-play slot when the recorded play was
    // illegal — first element -100, partner a stray index. Real shape seen in
    // tournament files; broader than the (-100, -100) regression pair.
    private static readonly sbyte[] IllegalPlayCandidate = [-100, 10, 10, 7, 6, 7, 6, 7];

    [Fact]
    public void Iterate_IllegalPlaySurroundedByLegalMoves_SkipsOnlyIllegalAndEmitsRest()
    {
        var logger = new CapturingLogger();
        var file = BuildFileWithThreeMoves();

        var rows = XgDecisionIterator.Iterate(file, "synthetic.xg", logger: logger).ToList();

        rows.Should().HaveCount(2, "the two legal moves emit; the illegal one is skipped");
        rows.Select(r => r.MoveNumber).Should().Equal(new[] { 1, 3 },
            "the skipped illegal play is move 2 — the surrounding moves keep their numbers");
    }

    [Fact]
    public void IterateDiagramRequests_IllegalPlaySurroundedByLegalMoves_SkipsOnlyIllegalAndEmitsRest()
    {
        var logger = new CapturingLogger();
        var file = BuildFileWithThreeMoves();

        var requests = XgDecisionIterator.IterateDiagramRequests(file, "synthetic.xg", logger: logger).ToList();

        requests.Should().HaveCount(2, "the two legal moves emit; the illegal one is skipped");
    }

    [Fact]
    public void Iterate_IllegalPlay_LogsContextualWarning()
    {
        var logger = new CapturingLogger();
        var file = BuildFileWithThreeMoves();

        _ = XgDecisionIterator.Iterate(file, "synthetic.xg", logger: logger).ToList();

        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        warnings.Should().ContainSingle("exactly one decision carries the illegal-play marker");

        string message = warnings[0].Message;
        message.Should().Contain("synthetic.xg", "the source file pinpoints which file to inspect");
        message.Should().Contain("game 1");
        message.Should().Contain("move 2", "the illegal play is the second move of the game");
        message.Should().Contain("roll 52", "the illegal move was rolled 5-2");
    }

    [Fact]
    public void Iterate_NoLogger_SkipsIllegalPlaySilentlyWithoutThrowing()
    {
        var file = BuildFileWithThreeMoves();

        var act = () => XgDecisionIterator.Iterate(file, "synthetic.xg").ToList();

        act.Should().NotThrow("the default NullLogger suppresses the warning but the skip still applies");
        act().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    //  Fixture
    // -----------------------------------------------------------------------

    /// <summary>
    /// One game with three analysed move records: a legal move (roll 3-1),
    /// the illegal-play marker (roll 5-2), then another legal move (roll 3-1).
    /// </summary>
    private static XgFile BuildFileWithThreeMoves()
    {
        return new XgFile
        {
            Records =
            {
                new MatchHeaderRecord { EntryType = RecordType.HeaderMatch, MatchLength = 7, Player1 = "P1", Player2 = "P2" },
                new GameHeaderRecord
                {
                    EntryType = RecordType.HeaderGame,
                    InitialPosition = new PositionEngine { Points = StandardOpening() },
                },
                MakeMove([23, 22, -1, -1, -1, -1, -1, -1], dice: [3, 1]),
                MakeMove(IllegalPlayCandidate,               dice: [5, 2]),
                MakeMove([23, 22, -1, -1, -1, -1, -1, -1], dice: [3, 1]),
            },
        };
    }

    private static MoveRecord MakeMove(sbyte[] moves, int[] dice)
    {
        var pos = new PositionEngine { Points = OneCheckerOn24() };
        return new MoveRecord
        {
            EntryType = RecordType.Move,
            InitialPosition = pos,
            FinalPosition = pos,            // unused on the skip path; matches PositionsPlayed[0]
            ActivePlayer = 1,
            Dice = dice,
            CubeValue = 0,
            MoveError = -1000.0,            // unanalysed-error sentinel
            Analysis = new BestMoveAnalysis
            {
                MoveCount = 1,
                Evals = [new EvalResult { Equity = 0.0f }],
                Moves = [moves],
                EvalLevels = [new EvalLevel { Level = 1 }],
                PositionsPlayed = [pos],
            },
            RolloutIndices = new int[32].Select(_ => -1).ToArray(),
        };
    }

    /// <summary>One active checker on point 24, so a legal (23, 22) move decodes
    /// without underflowing. The illegal/skip path never reads it.</summary>
    private static sbyte[] OneCheckerOn24()
    {
        var pts = new sbyte[26];
        pts[24] = 1;
        return pts;
    }

    private static sbyte[] StandardOpening()
    {
        var pts = new sbyte[26];
        pts[6]  = -5; pts[8]  = -3; pts[13] =  5; pts[24] = -2;
        pts[19] =  5; pts[17] =  3; pts[12] = -5; pts[1]  =  2;
        return pts;
    }

    /// <summary>
    /// Minimal <see cref="ILogger"/> that captures the level and fully
    /// formatted message of each entry — enough to assert the illegal-play
    /// warning carries file / game / move / roll context.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        public readonly List<(LogLevel Level, string Message)> Entries = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
