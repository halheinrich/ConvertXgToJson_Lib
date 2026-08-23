using BgDataTypes_Lib;
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
/// <see cref="XgDecisionIteratorSentinelTests"/>; this file adds the
/// surrounding-decisions behaviour and the logging contract. (The broadened
/// <c>(-100, X)</c> marker shape seen in tournament files is an encoding
/// fact, pinned by <see cref="XgMoveEncodingTests"/> — the fixture here is
/// built through <see cref="XgFileBuilder"/> and does not spell the bytes.)
/// </summary>
[Collection("FileIO")]
public class XgDecisionIteratorIllegalPlayTests
{
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
    /// One game with three move records: a legal play (roll 3-1), the
    /// illegal-play marker (roll 5-2), then another legal play (roll 3-1).
    /// Synthesized through <see cref="XgFileBuilder"/> — the fixture SSOT;
    /// the marker's raw encoding is the builder's concern, not this test's.
    /// </summary>
    private static XgFile BuildFileWithThreeMoves()
    {
        var builder = XgFileBuilder.ForMatch(7, "P1", "P2");
        builder.AddGame()
            .Play(XgPlayer.Player1, new DiceRoll(3, 1), MakeFivePoint())
            .IllegalPlay(XgPlayer.Player2, new DiceRoll(5, 2))
            .Play(XgPlayer.Player2, new DiceRoll(3, 1), MakeFivePoint());
        return builder.Build();
    }

    /// <summary>8/5 6/5 in the mover's numbering — legal from the opening for either side.</summary>
    private static Play MakeFivePoint()
    {
        var play = new Play();
        play.Add(new Move(8, 5));
        play.Add(new Move(6, 5));
        return play;
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
