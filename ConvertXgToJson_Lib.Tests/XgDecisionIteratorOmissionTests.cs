using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The decision walk leaves out what it cannot read as a legitimate decision,
/// reports each omission, and goes on (halheinrich/backgammon#514, Hal's
/// rulings of 2026-11-01): a game whose header states a standing the session
/// refuses is omitted whole, at its header, and the walk resumes at the next
/// game; a corrupt candidate is omitted alone, and the game goes on from the
/// next record. An illegal play is no omission
/// (<see cref="XgDecisionIteratorIllegalPlayTests"/>). Every identifier the surviving
/// decisions carry is the one it always had. Omissions reach the caller
/// through <see cref="XgIteratorState.Omissions"/> and the logger, each
/// reset per enumeration; recovery is narrow, so a programming failure still
/// propagates. Synthesized through the builders so it runs in CI; the three
/// real files Hal reported are at the bottom, local-only.
/// </summary>
[Collection("FileIO")]
public class XgDecisionIteratorOmissionTests
{
    private static readonly DiceRoll ThreeOne = new(3, 1);
    private static readonly Play MakeFivePoint = Play.Create(new Move(8, 5), new Move(6, 5));
    private static readonly Play TwentyFourTwenty = Play.Create(new Move(24, 21), new Move(21, 20));

    // -----------------------------------------------------------------------
    //  A refused standing omits the game, at its header
    // -----------------------------------------------------------------------

    [Fact]
    public void BadFirstGame_ThenAValidOne_YieldsTheValidGamesDecisions_UnderTheirOwnGameNumber()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGameAsRecorded(7, 0).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);   // player 1 needs 0: refused
        builder.AddGame(1, 0).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint).Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);
        var state = new XgIteratorState();

        var rows = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state).ToList();

        rows.Select(r => (r.Game, r.MoveNumber)).Should().Equal([(2, 1), (2, 2)],
            "the omitted game still counts, so the valid game keeps its number");
        var omission = state.Omissions.Should().ContainSingle().Subject;
        omission.Kind.Should().Be(XgOmissionKind.InvalidStanding);
        omission.Game.Should().Be(1);
        omission.IsWholeGame.Should().BeTrue();
        omission.MoveNumber.Should().BeNull();
        omission.Roll.Should().BeNull();
        omission.SourceFile.Should().Be("match.xg");
        omission.Reason.Should().Be(StandingRefusal(0, 7, isCrawford: false), "the reason is the owner's refusal, exactly");
    }

    /// <summary>
    /// Hal's ruling B of 2026-11-03 (halheinrich/backgammon#514, reversing
    /// ruling C, "keep full message"): the reason is the rule owner's own
    /// refusal, exactly as <see cref="MatchStanding.TryCreate"/> or
    /// <see cref="GameSession.TryCreate"/> gives it for the same values,
    /// naming them — no exception text, no runtime tail. The cases are the
    /// floor (a game recorded past the length), the bound and the Crawford
    /// standing.
    /// </summary>
    [Theory]
    [InlineData(7, 0, false, "Player 1's away score is 0.")]
    [InlineData(-2, -2, false, "The standing is 9-away/9-away. The match length is 7.")]
    [InlineData(3, 3, true, "The standing is 4-away/4-away.")]
    public void ARefusedStandingsReason_IsTheOwnersRefusal_NamingTheRefusedValues(int score1, int score2, bool isCrawford, string values)
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGameAsRecorded(score1, score2, isCrawford).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        var state = new XgIteratorState();

        _ = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state).ToList();

        string reason = state.Omissions.Should().ContainSingle().Subject.Reason;
        int away1 = 7 - score1, away2 = 7 - score2;
        string expected = MatchStanding.TryCreate(away1, away2, isCrawford, out var standing, out var refusal)
            ? PairingRefusal(7, standing)
            : refusal;
        reason.Should().Be(expected, "the reason is the owner's refusal for the same values, exactly");
        reason.Should().EndWith(values, "the refusal names the values it refuses, and nothing follows them");
    }

    [Fact]
    public void NoOmissionsReason_HoldsALineBreak_ForEitherKind()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGameAsRecorded(7, 0).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);                 // the floor
        builder.AddGameAsRecorded(-2, -2).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);               // the bound
        builder.AddGameAsRecorded(3, 3, isCrawford: true).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint); // Crawford
        builder.AddGame().PlayWithCorruptCandidate(XgPlayer.Player1, new DiceRoll(4, 2), Play.Create(new Move(13, 9), new Move(13, 11)), Play.Create(new Move(14, 10)));
        var state = new XgIteratorState();

        _ = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state).ToList();

        state.Omissions.Select(o => o.Kind).Should().Equal(
            XgOmissionKind.InvalidStanding, XgOmissionKind.InvalidStanding, XgOmissionKind.InvalidStanding, XgOmissionKind.CorruptCandidate);
        state.Omissions.Should().OnlyContain(o => !o.Reason.Contains('\n') && !o.Reason.Contains('\r'), "a reason is one line");
    }

    /// <summary>The refusal <see cref="MatchStanding.TryCreate"/> gives for these values; the test fails if it gives none.</summary>
    private static string StandingRefusal(int away1, int away2, bool isCrawford)
    {
        MatchStanding.TryCreate(away1, away2, isCrawford, out _, out var refusal).Should().BeFalse("the standing's rules refuse these values");
        return refusal!;
    }

    /// <summary>The refusal <see cref="GameSession.TryCreate"/> gives for a match of <paramref name="length"/> at <paramref name="standing"/>; the test fails if it gives none.</summary>
    private static string PairingRefusal(int length, MatchStanding standing)
    {
        GameSession.TryCreate(new MatchTerms { Length = length }, standing, out _, out var refusal).Should().BeFalse("the pairing refuses this standing");
        return refusal!;
    }

    [Fact]
    public void ValidGamesAroundABadOne_KeepTheirDecisionsAndNumbers()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(0, 0).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        builder.AddGameAsRecorded(-2, -2)                                                // 9-away in a 7-point match: refused
            .CubeDecision(XgPlayer.Player1, new XgCubeEquities(0.5, 0.6, 1.0))
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        builder.AddGame(1, 2)
            .CubeDecision(XgPlayer.Player2, new XgCubeEquities(0.5, 0.6, 1.0), doublerAction: CubeAction.NoDouble)
            .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);
        var state = new XgIteratorState();

        var records = XgDecisionIterator.IterateDiagramRequests(builder.Build(), "match.xg", state).ToList();

        records.Select(r => (r.Game, r.MoveNumber, r.Kind)).Should().Equal(
            [(1, 1, DecisionKind.CheckerPlay), (3, 1, DecisionKind.Cube), (3, 1, DecisionKind.CheckerPlay)],
            "games 1 and 3 emit under their own numbers; nothing of game 2 does");
        state.Omissions.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Game = 2,
            Kind = XgOmissionKind.InvalidStanding,
            IsWholeGame = true,
        });
        state.Omissions[0].Reason.Should().Contain("The match length is 7", "leg 1 made the refusal name the length");
    }

    [Fact]
    public void BadLastGame_IsOmitted_AndTheEnumerationCompletes()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(0, 0).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        builder.AddGame(0, 1).Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);
        builder.AddGameAsRecorded(0, 8).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);   // player 2 needs -1
        var state = new XgIteratorState();

        var act = () => XgDecisionIterator.Iterate(builder.Build(), "match.xg", state).ToList();

        var rows = act.Should().NotThrow().Subject;
        rows.Select(r => r.Game).Should().Equal(1, 2);
        state.Omissions.Should().ContainSingle().Which.Game.Should().Be(3);
        state.GameInfo.Should().BeNull("the walk ended in an omitted game, which has no usable standing");
    }

    [Fact]
    public void InvalidCrawfordGame_IsOmittedAndReported_ByTheCrawfordRule()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGameAsRecorded(3, 3, isCrawford: true).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        builder.AddGame(6, 3, isCrawford: true).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        var state = new XgIteratorState();

        var rows = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state).ToList();

        rows.Select(r => r.Game).Should().Equal(2);
        var omission = state.Omissions.Should().ContainSingle().Subject;
        omission.Kind.Should().Be(XgOmissionKind.InvalidStanding);
        omission.Game.Should().Be(1);
        omission.Reason.Should().Contain("Crawford");
    }

    [Fact]
    public void AllGamesInvalid_FinishesWithNoDecisions_AndEveryOmissionAvailable()
    {
        var builder = XgFileBuilder.ForMatch(5, "Alice", "Bob");
        builder.AddGameAsRecorded(5, 0).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        builder.AddGameAsRecorded(0, 6).Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);
        builder.AddGameAsRecorded(0, 0, isCrawford: true).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        var state = new XgIteratorState();

        var rows = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state).ToList();

        rows.Should().BeEmpty();
        state.Omissions.Select(o => o.Game).Should().Equal(1, 2, 3);
        state.Omissions.Should().OnlyContain(o => o.Kind == XgOmissionKind.InvalidStanding && o.IsWholeGame);
        state.MatchInfo.Should().NotBeNull("the file decoded and its match header was read");
        state.GameInfo.Should().BeNull();
        state.ForcedPlayCount.Should().Be(0);
    }

    [Fact]
    public void AnOmittedGame_IsNotOfferedToSkipGameAt()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGameAsRecorded(7, 0).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        builder.AddGame(1, 0).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        var offered = new List<XgGameInfo>();
        var callbacks = new XgIteratorCallbacks(SkipGameAt: game => { offered.Add(game); return false; });

        _ = XgDecisionIterator.Iterate(builder.Build(), "match.xg", callbacks: callbacks).ToList();

        offered.Should().ContainSingle("the omitted game has no standing to judge")
            .Which.Standing.Should().BeOfType<MatchStanding>().Which.Away1.Should().Be(6);
    }

    [Fact]
    public void MoneyGameWithANegativeScore_IsAccepted_WithItsScoreKept()
    {
        var builder = XgFileBuilder.ForMoneySession("Alice", "Bob");
        builder.AddGameAsRecorded(-3, 2).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        var state = new XgIteratorState();
        var logger = new CapturingLogger();

        var rows = XgDecisionIterator.Iterate(builder.Build(), "money.xg", state, logger: logger).ToList();

        rows.Should().ContainSingle("a money score is a tally and does not bear on the decision");
        state.Omissions.Should().BeEmpty();
        logger.Entries.Should().BeEmpty();
        state.GameInfo!.Standing.Should().BeOfType<MoneyStanding>().Which.Score1.Should().Be(-3);
    }

    // -----------------------------------------------------------------------
    //  A refused header leaves no usable context
    // -----------------------------------------------------------------------

    [Fact]
    public void MatchContext_AfterARefusedHeader_HasNoGameInfoAndNoSession_ButTheGameNumberAdvanced()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(0, 0).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        builder.AddGameAsRecorded(7, 0);
        var file = builder.Build();
        var context = new MatchContext(file.Records, file.Comments);

        foreach (var record in file.Records)
            context.Update(record);

        context.GameNumber.Should().Be(2);
        context.GameInfo.Should().BeNull("the game before the refused one is not left in place");
        context.GameRefusal.Should().Contain("at least one point");
        var act = () => context.SessionFor(Seat.Player1);
        act.Should().Throw<InvalidDataException>().WithMessage("*refused*at least one point*");
    }

    // -----------------------------------------------------------------------
    //  A bad decision is omitted alone; the game goes on
    // -----------------------------------------------------------------------

    [Fact]
    public void AnIllegalPlayAndACorruptCandidateInOneGame_IsOneOmission_WithDecisionsBeforeAndAfter()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint)
            .IllegalPlay(XgPlayer.Player2, new DiceRoll(5, 2))
            .PlayWithCorruptCandidate(XgPlayer.Player1, new DiceRoll(4, 2), Play.Create(new Move(13, 9), new Move(13, 11)), Play.Create(new Move(14, 10)))
            .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);
        var state = new XgIteratorState();

        var rows = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state).ToList();

        rows.Select(r => r.MoveNumber).Should().Equal(new int?[] { 1, 4 }, "the plays left out keep their move numbers for the rest");
        var omission = state.Omissions.Should().ContainSingle("the illegal play is no omission").Subject;
        omission.Should().Be(new XgOmission("match.xg", 1, 3, new DiceRoll(4, 2), XgOmissionKind.CorruptCandidate, omission.Reason));
        omission.Reason.Should().Contain("candidate 2 (14/10)", "the reason names each invalid candidate");
        state.ForcedPlayCount.Should().Be(0);
    }

    [Fact]
    public void Omissions_AreLoggedAsWarnings_NamingFileGameMoveRollAndReason()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGameAsRecorded(7, 0).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        builder.AddGame().Play(XgPlayer.Player1, ThreeOne, MakeFivePoint).PlayWithCorruptCandidate(XgPlayer.Player2, new DiceRoll(4, 2), Play.Create(new Move(13, 9), new Move(13, 11)), Play.Create(new Move(14, 10)));
        var logger = new CapturingLogger();

        _ = XgDecisionIterator.Iterate(builder.Build(), "match.xg", logger: logger).ToList();

        logger.Entries.Should().OnlyContain(e => e.Level == LogLevel.Warning);
        logger.Warnings.Should().HaveCount(2);
        logger.Warnings[0].Should().StartWith("Invalid standing in match.xg, game 1: ").And.Contain("at least one point");
        logger.Warnings[1].Should().StartWith("Corrupt candidate in match.xg, game 2, move 2, roll 42: ").And.Contain("candidate 2 (14/10)");
    }

    [Fact]
    public void NoStateAndNoLogger_HearsNothing_AndTheWalkStillOmits()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGameAsRecorded(7, 0).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        builder.AddGame().Play(XgPlayer.Player1, ThreeOne, MakeFivePoint).PlayWithCorruptCandidate(XgPlayer.Player2, new DiceRoll(4, 2), Play.Create(new Move(13, 9), new Move(13, 11)), Play.Create(new Move(14, 10)));

        var act = () => XgDecisionIterator.IterateDiagramRequests(builder.Build(), "match.xg").ToList();

        act.Should().NotThrow().Subject.Should().ContainSingle().Which.Game.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    //  The observer resets per enumeration
    // -----------------------------------------------------------------------

    [Fact]
    public void State_ResetsAtEachEnumeration_SoOmissionsAndCountsDoNotLeak()
    {
        var bad = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        bad.AddGameAsRecorded(7, 0).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        bad.AddGame(initialPosition: OneCheckerOn24()).Play(XgPlayer.Player1, ThreeOne, TwentyFourTwenty);
        var clean = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        clean.AddGame().Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        var state = new XgIteratorState();

        _ = XgDecisionIterator.Iterate(bad.Build(), "bad.xg", state).ToList();
        var firstWalk = state.Omissions;
        state.ForcedPlayCount.Should().Be(1);
        firstWalk.Should().ContainSingle();

        _ = XgDecisionIterator.Iterate(clean.Build(), "clean.xg", state).ToList();

        state.Omissions.Should().BeEmpty("the second walk starts afresh");
        state.ForcedPlayCount.Should().Be(0);
        firstWalk.Should().ContainSingle("a list kept from the first walk is left as that walk recorded it");
        state.MatchInfo.Should().NotBeNull();
    }

    [Fact]
    public void State_IsResetAtTheFirstMoveNext_NotAtTheCall()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGameAsRecorded(7, 0).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        var state = new XgIteratorState();
        _ = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state).ToList();

        var deferred = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state);

        state.Omissions.Should().ContainSingle("the walk is lazy: nothing has been reset until it is enumerated");
        _ = deferred.ToList();
        state.Omissions.Should().ContainSingle();
    }

    // -----------------------------------------------------------------------
    //  Recovery is narrow
    // -----------------------------------------------------------------------

    [Fact]
    public void AProgrammingFailureInTheWalk_StillPropagates()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame().Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        var callbacks = new XgIteratorCallbacks(SkipGameAt: _ => throw new InvalidOperationException("the caller's bug"));

        var act = () => XgDecisionIterator.Iterate(builder.Build(), "match.xg", callbacks: callbacks).ToList();

        act.Should().Throw<InvalidOperationException>().WithMessage("the caller's bug");
    }

    /// <summary>
    /// The one catch is for <see cref="ArgumentException"/> at the header's
    /// pairing; a callback's <see cref="ArgumentException"/> is the width a
    /// catch around the walk would wrongly swallow, so it is pinned apart
    /// from the <see cref="InvalidOperationException"/> case.
    /// </summary>
    [Fact]
    public void AnArgumentExceptionFromACallback_StillPropagates()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame().Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        var callbacks = new XgIteratorCallbacks(SkipGameAt: _ => throw new ArgumentException("the caller's bug", "game"));

        var act = () => XgDecisionIterator.Iterate(builder.Build(), "match.xg", callbacks: callbacks).ToList();

        act.Should().Throw<ArgumentException>().WithMessage("the caller's bug*");
    }

    // -----------------------------------------------------------------------
    //  A record that states no roll is passed by
    // -----------------------------------------------------------------------

    [Fact]
    public void ANoRollRecordWithNoMarker_IsStillPassedBySilently()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .PlayAsRecorded(XgPlayer.Player1, 0, 0, MakeFivePoint)
            .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);
        var state = new XgIteratorState();
        var logger = new CapturingLogger();

        var rows = XgDecisionIterator.Iterate(builder.Build(), "match.xg", state, logger: logger).ToList();

        rows.Should().ContainSingle().Which.MoveNumber.Should().Be(2);
        state.Omissions.Should().BeEmpty();
        state.ForcedPlayCount.Should().Be(0);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void AFileWithoutAMatchHeader_StillThrows()
    {
        var file = new XgFile { Header = default!, Records = [], Comments = [] };

        var act = () => XgDecisionIterator.Iterate(file, "match.xg").ToList();

        act.Should().Throw<InvalidDataException>();
    }

    // -----------------------------------------------------------------------
    //  Hal's three files (halheinrich/backgammon#514), local-only
    // -----------------------------------------------------------------------

    public static TheoryData<string, string> RefusedFixtures => new()
    {
        { "2024-07-03_Jonathan_Hardigree_vs_Michael_Mesich_29046.xg", "at least one point" },
        { "2024-11-30_Johan_Norberg_vs_Konrad_Fröschl_34083.xg", "Crawford" },
    };

    /// <summary>
    /// The two files Extract skipped whole (halheinrich/backgammon#514): each
    /// now yields its other games' decisions, and names the game it omits
    /// with the rule that refused it. How many are omitted is the file's
    /// fact, read here, not pinned.
    /// </summary>
    [Theory]
    [MemberData(nameof(RefusedFixtures))]
    [Trait("Category", "RequiresFixtureFiles")]
    public void RefusedFixture_YieldsItsOtherGames_AndNamesTheOmittedOne(string fixture, string reason)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, fixture);
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException($"Expected fixture not present: {path}.");
        var state = new XgIteratorState();

        var rows = XgDecisionIterator.Iterate(XgFileReader.ReadFile(path), fixture, state).ToList();

        rows.Should().NotBeEmpty("the games with a valid standing emit");
        state.Omissions.Should().NotBeEmpty().And.OnlyContain(o => o.Kind == XgOmissionKind.InvalidStanding);
        state.Omissions.Should().OnlyContain(o => o.Reason.Contains(reason));
        rows.Select(r => r.Game!.Value).Should().NotIntersectWith(state.Omissions.Select(o => o.Game),
            "no decision of an omitted game is yielded");
    }

    /// <summary>
    /// MTCH2987.xg is a 17-point match whose first three games start past the
    /// length (-2/-2 and on): games 1 to 3 are omitted and 4 to 12 kept, by
    /// Hal's ruling on the needs rule (halheinrich/backgammon#514). Each
    /// reason is the pairing's refusal, naming the game's standing and the
    /// length (Hal's ruling B, 2026-11-03).
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixtureFiles")]
    public void Mtch2987_OmitsGamesOneToThree_AndKeepsFourToTwelve()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "MTCH2987.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException($"Expected fixture not present: {path}.");
        var state = new XgIteratorState();

        var rows = XgDecisionIterator.Iterate(XgFileReader.ReadFile(path), "MTCH2987.xg", state).ToList();

        state.Omissions.Select(o => o.Game).Should().Equal(1, 2, 3);
        state.Omissions.Should().OnlyContain(o => o.Kind == XgOmissionKind.InvalidStanding && o.Reason.Contains("The match length is 17"));
        state.Omissions.Select(o => o.Reason).Should().Equal(
            PairingRefusal(17, new MatchStanding { Away1 = 19, Away2 = 19, IsCrawford = false }),
            PairingRefusal(17, new MatchStanding { Away1 = 19, Away2 = 17, IsCrawford = false }),
            PairingRefusal(17, new MatchStanding { Away1 = 19, Away2 = 15, IsCrawford = false }));
        state.Omissions.Select(o => o.Reason).Should().SatisfyRespectively(
            r => r.Should().Contain("19-away/19-away"),
            r => r.Should().Contain("19-away/17-away"),
            r => r.Should().Contain("19-away/15-away"));
        rows.Select(r => r.Game).Distinct().Should().BeEquivalentTo(Enumerable.Range(4, 9));
    }

    /// <summary>A checker of each side — a decision position — the mover's on 24: a 3-1 reaches one position only.</summary>
    private static int[] OneCheckerOn24()
    {
        var pts = new int[26];
        pts[24] = 1;
        pts[1] = -1;
        return pts;
    }
}
