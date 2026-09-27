using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The two kinds of analysed source decision that build no record
/// (halheinrich/backgammon#273), each found without catching anything:
/// <list type="bullet">
///   <item><description>
///     <b>Not a decision position</b> — a side has borne off all its
///     checkers (Hal's ruling of 2026-09-27). Legitimate XG data, passed by
///     silently as ordinary filtering: no record, no warning, for either
///     decision kind; <see cref="PositionData.IsDecisionPosition"/> answers.
///   </description></item>
///   <item><description>
///     <b>A corrupt candidate</b> — a candidate invalid from its own position,
///     which the record would refuse. Found with
///     <see cref="BoardState.TryApplyPlay"/> on a fresh board per candidate;
///     the decision is skipped with a warning naming the file, game, move,
///     roll and the invalid candidate, as XG's illegal-play marker is.
///   </description></item>
/// </list>
/// In both, the rest of the file still emits, and in an <c>.xgp</c> a skipped
/// play leaves the analysed cube to be emitted. The files are synthesized
/// through the builders; the builder writes only valid candidates by design,
/// so the corrupt one is made by rewriting one candidate's move bytes in the
/// synthesized file — the one place a test here steps below the builder.
/// </summary>
public class XgDecisionIteratorFilterTests
{
    private const string Xg = "synthetic.xg";
    private static readonly DiceRoll ThreeOne = new(3, 1);
    private static readonly Play MakeFivePoint = Play.Create(new Move(8, 5), new Move(6, 5));
    private static readonly Play Split31 = Play.Create(new Move(13, 10), new Move(24, 23));

    /// <summary>A position where only player 1 has checkers: player 2 has borne off all fifteen.</summary>
    private static int[] OnlyPlayer1() => Board((6, 5), (5, 5), (4, 5));

    /// <summary>A position where only player 2 has checkers: player 1 has borne off all fifteen.</summary>
    private static int[] OnlyPlayer2() => Board((19, -5), (20, -5), (21, -5));

    private static int[] Board(params (int Point, int Count)[] counts)
    {
        var board = new int[26];
        foreach (var (point, count) in counts)
            board[point] = count;
        return board;
    }

    // -----------------------------------------------------------------------
    //  Not a decision position
    // -----------------------------------------------------------------------

    /// <summary>
    /// A match whose first game is played on after player 2 has borne off
    /// (player 1 still plays), whose second has player 1 borne off (a cube
    /// decision is still recorded for player 1), and whose third is ordinary.
    /// </summary>
    private static XgFile TerminalGamesThenAnOrdinaryOne()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(initialPosition: OnlyPlayer1())
            .Play(XgPlayer.Player1, ThreeOne, Play.Create(new Move(6, 3), new Move(4, 3)));
        builder.AddGame(initialPosition: OnlyPlayer2())
            .CubeDecision(XgPlayer.Player1, new XgCubeEquities(0.5, 0.6, 1.0));
        builder.AddGame()
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        return builder.Build();
    }

    [Fact]
    public void NotADecisionPosition_YieldsNoRecordAndNoWarning_WhileTheRestOfTheFileEmits()
    {
        var file = TerminalGamesThenAnOrdinaryOne();
        var logger = new CapturingLogger();

        var records = XgDecisionIterator.IterateDiagramRequests(file, Xg, logger: logger).ToList();

        records.Should().ContainSingle("only the ordinary game's play is a decision")
            .Which.Game.Should().Be(3);
        logger.Entries.Should().BeEmpty("a position that is not a decision is ordinary data, passed by silently");
    }

    [Fact]
    public void NotADecisionPosition_IsPassedByOnTheRowSurfaceToo()
    {
        var logger = new CapturingLogger();

        var rows = XgDecisionIterator.Iterate(TerminalGamesThenAnOrdinaryOne(), Xg, logger: logger).ToList();

        rows.Should().ContainSingle().Which.Game.Should().Be(3);
        logger.Entries.Should().BeEmpty();
    }

    /// <summary>
    /// The cube rule is the cube emission gate's, which the tests' emission
    /// mirror consults: the terminal game's cube pane is not a cube decision.
    /// </summary>
    [Fact]
    public void NotADecisionPosition_IsPartOfTheCubeEmissionGate()
    {
        EmissionMirror.CubeDecisions(TerminalGamesThenAnOrdinaryOne(), Xg).Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    //  A corrupt candidate
    // -----------------------------------------------------------------------

    /// <summary>
    /// A match whose first decision — player 1's opening 3-1 — holds a second
    /// candidate rewritten to 13/12, which lands on the opponent's
    /// five-checker point: invalid from the decision's position. Player 2's
    /// reply follows, ordinary.
    /// </summary>
    private static XgFile CorruptFirstDecisionThenAnOrdinaryOne()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint,
                [new XgPlayCandidate(MakeFivePoint, 0.15, ply: 3), new XgPlayCandidate(Split31, 0.10, ply: 3)])
            .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);
        var file = builder.Build();
        Corrupt(file.Records.OfType<MoveRecord>().First(), candidate: 1, from: 13, to: 12);
        return file;
    }

    /// <summary>Rewrites one candidate's move bytes to the single hop <paramref name="from"/>/<paramref name="to"/>, in XG's encoding.</summary>
    private static void Corrupt(MoveRecord move, int candidate, int from, int to)
    {
        var bytes = move.Analysis.Moves[candidate];
        Array.Fill(bytes, (sbyte)-1);
        bytes[0] = (sbyte)(from - 1);
        bytes[1] = (sbyte)(to - 1);
    }

    [Fact]
    public void CorruptCandidate_SkipsItsDecisionWithAWarning_WhileTheRestOfTheFileEmits()
    {
        var logger = new CapturingLogger();

        var records = XgDecisionIterator
            .IterateDiagramRequests(CorruptFirstDecisionThenAnOrdinaryOne(), Xg, logger: logger).ToList();

        records.Should().ContainSingle("the corrupt decision is skipped; its successor still emits")
            .Which.MoveNumber.Should().Be(2);
        var warning = logger.Warnings.Should().ContainSingle().Subject;
        warning.Should().Contain(Xg, "the warning names the file");
        warning.Should().Contain("game 1");
        warning.Should().Contain("move 1");
        warning.Should().Contain("roll 31");
        warning.Should().Contain("candidate 2 (13/12)", "the warning names the invalid candidate, as the play it spells");
    }

    [Fact]
    public void CorruptCandidate_IsSkippedOnTheRowSurfaceToo()
    {
        var logger = new CapturingLogger();

        var rows = XgDecisionIterator.Iterate(CorruptFirstDecisionThenAnOrdinaryOne(), Xg, logger: logger).ToList();

        rows.Should().ContainSingle().Which.MoveNumber.Should().Be(2);
        logger.Warnings.Should().ContainSingle();
    }

    [Fact]
    public void CorruptCandidate_WithNoLogger_IsSkippedWithoutThrowing()
    {
        var act = () => XgDecisionIterator.Iterate(CorruptFirstDecisionThenAnOrdinaryOne(), Xg).ToList();

        act.Should().NotThrow("the default NullLogger suppresses the warning; the skip still applies");
        act().Should().ContainSingle();
    }

    /// <summary>
    /// Each candidate is tested on a fresh board. <see cref="BoardState.TryApplyPlay"/>
    /// applies a valid play and turns the board to the next mover, so a shared
    /// board would test the second candidate from the wrong position: here the
    /// first candidate makes player 1's 5-point, and the second, 24/20, is
    /// valid from the decision's position but would land on that point from
    /// the turned board.
    /// </summary>
    [Fact]
    public void EachCandidate_IsTestedFromTheDecisionsOwnPosition()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame().Play(XgPlayer.Player1, ThreeOne, MakeFivePoint,
            [new XgPlayCandidate(MakeFivePoint, 0.15), new XgPlayCandidate(Play.Create(new Move(24, 20)), 0.10)]);
        var logger = new CapturingLogger();

        var records = XgDecisionIterator.IterateDiagramRequests(builder.Build(), Xg, logger: logger).ToList();

        records.Should().ContainSingle("both candidates are valid from the decision's position");
        logger.Warnings.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    //  An .xgp: both filters act upstream of the single-decision policy
    // -----------------------------------------------------------------------

    /// <summary>
    /// An <c>.xgp</c> holding an analysed cube pane and a play with a corrupt
    /// candidate: the play is skipped before the single-decision policy, so it
    /// does not suppress the file's analysed cube.
    /// </summary>
    [Fact]
    public void Xgp_ASkippedCorruptPlay_LeavesTheAnalysedCubeToBeEmitted()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .CubeDecision(XgPlayer.Player1, new XgCubeEquities(-0.2, -0.4, 1.0), doublerAction: CubeAction.NoDouble)
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint,
                [new XgPlayCandidate(MakeFivePoint, 0.15), new XgPlayCandidate(Split31, 0.10)]);
        var file = builder.Build();
        Corrupt(file.Records.OfType<MoveRecord>().Single(), candidate: 1, from: 13, to: 12);
        var logger = new CapturingLogger();

        var records = XgDecisionIterator.IterateDiagramRequests(file, "position.xgp", logger: logger).ToList();
        var rows = XgDecisionIterator.Iterate(file, "position.xgp").ToList();

        records.Should().ContainSingle().Which.Kind.Should().Be(DecisionKind.Cube);
        rows.Should().ContainSingle().Which.Kind.Should().Be(DecisionKind.Cube);
        logger.Warnings.Should().ContainSingle().Which.Should().Contain("position.xgp");
    }

    /// <summary>
    /// The control: the same file with the play intact emits the play, as the
    /// policy says — so the cube above is emitted because the play was skipped.
    /// </summary>
    [Fact]
    public void Xgp_AnIntactPlay_IsEmittedOverTheCube()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .CubeDecision(XgPlayer.Player1, new XgCubeEquities(-0.2, -0.4, 1.0), doublerAction: CubeAction.NoDouble)
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint,
                [new XgPlayCandidate(MakeFivePoint, 0.15), new XgPlayCandidate(Split31, 0.10)]);

        XgDecisionIterator.IterateDiagramRequests(builder.Build(), "position.xgp")
            .Should().ContainSingle().Which.Kind.Should().Be(DecisionKind.CheckerPlay);
    }

    /// <summary>
    /// An <c>.xgp</c> saved at a position that is not a decision: both panes
    /// share the position, so neither is a decision, and nothing is logged.
    /// </summary>
    [Fact]
    public void Xgp_AtAPositionThatIsNotADecision_YieldsNothingAndLogsNothing()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(initialPosition: OnlyPlayer1())
            .CubeDecision(XgPlayer.Player1, new XgCubeEquities(0.9, 1.4, 1.0), doublerAction: CubeAction.NoDouble)
            .Play(XgPlayer.Player1, ThreeOne, Play.Create(new Move(6, 3), new Move(4, 3)));
        var logger = new CapturingLogger();

        XgDecisionIterator.IterateDiagramRequests(builder.Build(), "position.xgp", logger: logger).Should().BeEmpty();
        logger.Entries.Should().BeEmpty();
    }
}
