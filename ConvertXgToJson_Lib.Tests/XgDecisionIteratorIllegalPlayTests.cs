using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// An illegal play is no problem (Hal's rulings, halheinrich/backgammon#514):
/// a record carrying XG's illegal-play marker yields no decision and nothing
/// says anything about it — no omission, no log line at any level, no forced
/// count — whatever the position or the dice, and the decisions around it
/// keep their numbers. It stays one kind of the one classification, read
/// first, so the marker never reaches a move leaf and the historical
/// <see cref="IndexOutOfRangeException"/> can't fire. Dance ((0, 0)) and the
/// regression cases stay covered by <see cref="XgDecisionIteratorSentinelTests"/>,
/// the marker at a forced position by
/// <see cref="XgDecisionIteratorForcedPlayTests"/>. (The broadened
/// <c>(-100, X)</c> marker shape seen in tournament files is an encoding
/// fact, pinned by <see cref="XgMoveEncodingTests"/> — the fixture here is
/// built through <see cref="XgFileBuilder"/> and does not spell the bytes.)
/// </summary>
[Collection("FileIO")]
public class XgDecisionIteratorIllegalPlayTests
{
    private static readonly DiceRoll ThreeOne = new(3, 1);

    [Fact]
    public void Iterate_IllegalPlaySurroundedByLegalMoves_SkipsOnlyIllegalAndEmitsRest()
    {
        var logger = new CapturingLogger();
        var file = BuildFileWithThreeMoves();

        var rows = XgDecisionIterator.Iterate(file, "synthetic.xg", logger: logger).ToList();

        rows.Should().HaveCount(2, "the two legal moves emit; the illegal one is skipped");
        rows.Select(r => r.MoveNumber).Should().Equal(new int?[] { 1, 3 },
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
    public void IllegalPlay_BetweenDecisions_LeavesNoOmission_NoForcedCount_AndNoLogLine()
    {
        var state = new XgIteratorState();
        var logger = new CapturingLogger();

        var rows = XgDecisionIterator.Iterate(BuildFileWithThreeMoves(), "synthetic.xg", state, logger: logger).ToList();

        rows.Select(r => r.MoveNumber).Should().Equal(new int?[] { 1, 3 });
        state.Omissions.Should().BeEmpty("an illegal play is not a problem");
        state.ForcedPlayCount.Should().Be(0);
        logger.Entries.Should().BeEmpty("nothing is said of it, at any level");
    }

    /// <summary>
    /// The other surface takes the same path; pinned apart because the
    /// silence is the contract each app relies on.
    /// </summary>
    [Fact]
    public void IterateDiagramRequests_IllegalPlay_IsAsSilent()
    {
        var state = new XgIteratorState();
        var logger = new CapturingLogger();

        var records = XgDecisionIterator.IterateDiagramRequests(BuildFileWithThreeMoves(), "synthetic.xg", state, logger: logger).ToList();

        records.Select(r => r.MoveNumber).Should().Equal(1, 3);
        state.Omissions.Should().BeEmpty();
        state.ForcedPlayCount.Should().Be(0);
        logger.Entries.Should().BeEmpty();
    }

    /// <summary>
    /// The marker is read before the roll, so dice that are no roll beside
    /// it are never used: neither both 0, nor the shapes that are refused
    /// loud where the dice are used.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    [InlineData(7, 3)]
    public void IllegalPlay_OnARecordWhoseDiceAreNoRoll_PassesBySilently(int die1, int die2)
    {
        var builder = XgFileBuilder.ForMatch(7, "P1", "P2");
        builder.AddGame()
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint())
            .IllegalPlayAsRecorded(XgPlayer.Player2, die1, die2)
            .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint());
        var state = new XgIteratorState();
        var logger = new CapturingLogger();

        var rows = XgDecisionIterator.Iterate(builder.Build(), "synthetic.xg", state, logger: logger).ToList();

        rows.Select(r => r.MoveNumber).Should().Equal(new int?[] { 1, 3 });
        state.Omissions.Should().BeEmpty();
        state.ForcedPlayCount.Should().Be(0);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Iterate_NoLogger_SkipsIllegalPlaySilentlyWithoutThrowing()
    {
        var file = BuildFileWithThreeMoves();

        var act = () => XgDecisionIterator.Iterate(file, "synthetic.xg").ToList();

        act.Should().NotThrow("the walk neither needs a logger nor has anything to tell one");
        act().Should().HaveCount(2);
    }

    /// <summary>
    /// The marker stays one kind of the one classification, asked in the
    /// same order: ahead of the roll, so dice that are no roll do not make
    /// it <see cref="XgDecisionIterator.CheckerPlayKind.NoRoll"/>, and ahead
    /// of the forced test.
    /// </summary>
    [Theory]
    [InlineData(3, 1, false)]
    [InlineData(3, 1, true)]
    [InlineData(0, 0, false)]
    [InlineData(0, 5, false)]
    [InlineData(7, 3, false)]
    public void TheMarker_IsItsOwnKind_WhateverTheDiceAndThePosition(int die1, int die2, bool forcedPosition)
    {
        var builder = XgFileBuilder.ForMatch(7, "P1", "P2");
        var game = forcedPosition ? builder.AddGame(initialPosition: OneCheckerOn24()) : builder.AddGame();
        game.IllegalPlayAsRecorded(XgPlayer.Player1, die1, die2);
        var move = builder.Build().Records.OfType<MoveRecord>().Should().ContainSingle().Subject;

        XgDecisionIterator.ClassifyCheckerPlay(move).Should().Be(XgDecisionIterator.CheckerPlayKind.IllegalPlay);
    }

    /// <summary>The position is asked first, as it always was: a marker where a side has borne off is simply not a decision position.</summary>
    [Fact]
    public void TheMarker_AtAPositionThatIsNoDecision_IsThatKind()
    {
        var builder = XgFileBuilder.ForMatch(7, "P1", "P2");
        builder.AddGame(initialPosition: OnlyPlayer1()).IllegalPlay(XgPlayer.Player1, ThreeOne);
        var move = builder.Build().Records.OfType<MoveRecord>().Should().ContainSingle().Subject;

        XgDecisionIterator.ClassifyCheckerPlay(move).Should().Be(XgDecisionIterator.CheckerPlayKind.NotADecisionPosition);
    }

    // -----------------------------------------------------------------------
    //  The three illegal plays in the fixtures, local-only
    // -----------------------------------------------------------------------

    private const string AchimMueller = "Achim Mueller (8.36) - Mario Sequeira (7.13) 23pt Monte Carlo WC 2008 SF.xg";
    private const string AviCohen = "Avi Cohen (6.86) - Max Stockslager (10.55) 2023-02-09_18122.xg";

    public static TheoryData<string, int, int> IllegalPlayRecords => new()
    {
        { AchimMueller, 10, 39 },
        { AchimMueller, 16, 13 },
        { AviCohen, 6, 25 },
    };

    /// <summary>
    /// Real records of XG's marker, none of them forced: each yields no
    /// decision, and its file holds no omission
    /// (halheinrich/backgammon#514, the three shown to Hal).
    /// </summary>
    [Theory]
    [MemberData(nameof(IllegalPlayRecords))]
    [Trait("Category", "RequiresFixtureFiles")]
    public void FixtureIllegalPlay_YieldsNoDecision_AndNoOmission(string fixture, int game, int move)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, fixture);
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException($"Expected fixture not present: {path}.");
        var file = XgFileReader.ReadFile(path);
        var state = new XgIteratorState();

        var records = XgDecisionIterator.IterateDiagramRequests(file, fixture, state).ToList();

        IllegalPlaysOf(file).Should().Contain((game, move), "the fixture is chosen for this record");
        records.Should().NotContain(r => r.Kind == DecisionKind.CheckerPlay && r.Game == game && r.MoveNumber == move);
        records.Should().NotBeEmpty("the file's other decisions emit");
        state.Omissions.Should().BeEmpty();
    }

    /// <summary>Where <paramref name="file"/> holds the marker: each analysed record of that kind, by game and move, driving a context as the walk does.</summary>
    private static List<(int Game, int Move)> IllegalPlaysOf(XgFile file)
    {
        var found = new List<(int, int)>();
        var context = new MatchContext(file.Records, file.Comments);
        foreach (var record in file.Records)
        {
            context.Update(record);
            if (record is MoveRecord move && XgDecisionIterator.IsAnalysed(move)
                && XgDecisionIterator.ClassifyCheckerPlay(move) == XgDecisionIterator.CheckerPlayKind.IllegalPlay)
                found.Add((context.GameNumber, context.MoveNumber));
        }
        return found;
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
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint())
            .IllegalPlay(XgPlayer.Player2, new DiceRoll(5, 2))
            .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint());
        return builder.Build();
    }

    /// <summary>8/5 6/5 in the mover's numbering — legal from the opening for either side.</summary>
    private static Play MakeFivePoint() => Play.Create(new Move(8, 5), new Move(6, 5));

    /// <summary>A checker of each side — a decision position — the mover's on 24: a 3-1 reaches one position only.</summary>
    private static int[] OneCheckerOn24()
    {
        var pts = new int[26];
        pts[24] = 1;
        pts[1] = -1;
        return pts;
    }

    /// <summary>A position where only player 1 has checkers: player 2 has borne off all fifteen.</summary>
    private static int[] OnlyPlayer1()
    {
        var pts = new int[26];
        pts[6] = 5;
        pts[5] = 5;
        pts[4] = 5;
        return pts;
    }
}
