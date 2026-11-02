using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// A forced play is no decision, for every caller of the iterator (Hal's
/// ruling of 2026-11-01, halheinrich/backgammon#514): a checker play at a
/// position with exactly one legal play, by BgMoveGen's
/// <c>MoveGenerator.GeneratePlays</c>, a dance included. It is counted on
/// <see cref="XgIteratorState.ForcedPlayCount"/>, never reported. Problems
/// outrank it: an illegal play or a corrupt candidate at a forced position is
/// an omission, not a count. The rule is the position's: several legal plays
/// with one stored candidate stay a decision. Dropped upstream of the
/// <c>.xgp</c> selection, a forced play lets the analysed cube through.
/// </summary>
[Collection("FileIO")]
public class XgDecisionIteratorForcedPlayTests
{
    private static readonly DiceRoll ThreeOne = new(3, 1);
    private static readonly DiceRoll SixSix = new(6, 6);
    private static readonly Play TwentyFourTwenty = Play.Create(new Move(24, 21), new Move(21, 20));

    [Fact]
    public void ForcedOnlySource_YieldsNothing_CountsEachForcedPlay_AndReportsNoOmission()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(initialPosition: OneCheckerOn24())
            .Play(XgPlayer.Player1, ThreeOne, TwentyFourTwenty)
            .AtPosition(OneCheckerOn24())
            .Play(XgPlayer.Player1, ThreeOne, TwentyFourTwenty);
        var state = new XgIteratorState();
        var logger = new CapturingLogger();

        var rows = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state, logger: logger).ToList();
        var records = XgDecisionIterator.IterateDiagramRequests(builder.Build(), "match.xg").ToList();

        rows.Should().BeEmpty("both surfaces drop a forced play");
        records.Should().BeEmpty();
        state.ForcedPlayCount.Should().Be(2);
        state.Omissions.Should().BeEmpty("a forced play is not a problem");
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Dance_AtAClosedOutPosition_IsForced_WithNoKindOfItsOwn()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(initialPosition: OnTheBarAgainstAClosedBoard()).Dance(XgPlayer.Player1, SixSix);
        var state = new XgIteratorState();
        var logger = new CapturingLogger();

        var rows = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state, logger: logger).ToList();

        rows.Should().BeEmpty();
        state.ForcedPlayCount.Should().Be(1, "a dance is a forced play");
        state.Omissions.Should().BeEmpty();
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Dance_WhereALegalPlayExists_IsACorruptCandidate()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame().Dance(XgPlayer.Player1, SixSix);   // the opening position: 6-6 plays
        var state = new XgIteratorState();

        var rows = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state).ToList();

        rows.Should().BeEmpty();
        state.ForcedPlayCount.Should().Be(0);
        var omission = state.Omissions.Should().ContainSingle().Subject;
        omission.Kind.Should().Be(XgOmissionKind.CorruptCandidate);
        omission.Roll.Should().Be(SixSix);
        omission.Reason.Should().Contain("dance").And.Contain("legal");
    }

    [Fact]
    public void Dance_BesideACorruptCandidate_AtAClosedOutPosition_IsReported_NotCounted()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(initialPosition: OnTheBarAgainstAClosedBoard())
            .DanceBesideACorruptCandidate(XgPlayer.Player1, SixSix, Play.Create(new Move(13, 7)));
        var state = new XgIteratorState();

        var rows = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state).ToList();

        rows.Should().BeEmpty();
        state.ForcedPlayCount.Should().Be(0, "a dance sentinel excuses no other candidate");
        var omission = state.Omissions.Should().ContainSingle().Subject;
        omission.Kind.Should().Be(XgOmissionKind.CorruptCandidate);
        omission.Reason.Should().Contain("candidate 2 (13/7)");
    }

    [Fact]
    public void IllegalPlay_AtAForcedPosition_IsReported_NotCounted()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(initialPosition: OneCheckerOn24()).IllegalPlay(XgPlayer.Player1, ThreeOne);
        var state = new XgIteratorState();

        _ = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state).ToList();

        state.ForcedPlayCount.Should().Be(0, "a problem outranks a forced position");
        state.Omissions.Should().ContainSingle().Which.Kind.Should().Be(XgOmissionKind.IllegalPlay);
    }

    [Fact]
    public void CorruptCandidate_AtAForcedPosition_IsReported_NotCounted()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(initialPosition: OneCheckerOn24())
            .PlayWithCorruptCandidate(XgPlayer.Player1, ThreeOne, TwentyFourTwenty, Play.Create(new Move(13, 10)));
        var state = new XgIteratorState();

        _ = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state).ToList();

        state.ForcedPlayCount.Should().Be(0);
        var omission = state.Omissions.Should().ContainSingle().Subject;
        omission.Kind.Should().Be(XgOmissionKind.CorruptCandidate);
        omission.Reason.Should().Contain("candidate 2 (13/10)");
    }

    [Fact]
    public void SeveralLegalPlays_WithOneStoredCandidate_StayADecision()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(initialPosition: CheckersOn24And13()).Play(XgPlayer.Player1, ThreeOne, TwentyFourTwenty);
        var state = new XgIteratorState();

        var records = XgDecisionIterator.IterateDiagramRequests(builder.Build(), "match.xg", state).ToList();

        records.Should().ContainSingle("the candidate count is no part of the rule")
            .Which.Should().BeOfType<CheckerPlayDecision>().Which.Decision.Plays.Should().HaveCount(1);
        state.ForcedPlayCount.Should().Be(0);
    }

    [Fact]
    public void Xgp_WithAForcedPlayAndAnAnalysedCube_YieldsTheCube()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(initialPosition: OneCheckerOn24())
            .CubeDecision(XgPlayer.Player1, new XgCubeEquities(0.5, 0.6, 1.0), doublerAction: CubeAction.NoDouble)
            .Play(XgPlayer.Player1, ThreeOne, TwentyFourTwenty);
        var state = new XgIteratorState();

        var records = XgDecisionIterator.IterateDiagramRequests(builder.Build(), "position.xgp", state).ToList();

        records.Should().ContainSingle().Which.Kind.Should().Be(DecisionKind.Cube);
        state.ForcedPlayCount.Should().Be(1);
    }

    [Fact]
    public void ForcedPlay_StillCountsAsAMove_SoTheNextDecisionKeepsItsNumber()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(initialPosition: OneCheckerOn24())
            .Play(XgPlayer.Player1, ThreeOne, TwentyFourTwenty)
            .AtPosition(CheckersOn24And13())
            .Play(XgPlayer.Player1, ThreeOne, TwentyFourTwenty);

        var rows = XgDecisionIterator.Iterate(builder.Build(), "match.xg").ToList();

        rows.Should().ContainSingle().Which.MoveNumber.Should().Be(2);
    }

    /// <summary>A checker of each side — a decision position — the mover's on 24: a 3-1 reaches one position only.</summary>
    private static int[] OneCheckerOn24()
    {
        var pts = new int[26];
        pts[24] = 1;
        pts[1] = -1;
        return pts;
    }

    /// <summary>The mover's checkers on 24 and 13 against one of the opponent's on 1: a 3-1 has several plays.</summary>
    private static int[] CheckersOn24And13()
    {
        var pts = new int[26];
        pts[24] = 1;
        pts[13] = 1;
        pts[1] = -1;
        return pts;
    }

    /// <summary>Player 1 on the bar, player 2 holding every point of their home board: no roll enters.</summary>
    private static int[] OnTheBarAgainstAClosedBoard()
    {
        var pts = new int[26];
        pts[25] = 1;
        for (int point = 19; point <= 24; point++)
            pts[point] = -2;
        return pts;
    }
}
