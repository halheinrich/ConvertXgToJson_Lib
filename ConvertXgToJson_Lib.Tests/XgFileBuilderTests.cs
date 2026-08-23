using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Behavioural tests for <see cref="XgFileBuilder"/> / <see cref="XgGameBuilder"/>,
/// the one public synthesis path for an in-memory <see cref="XgFile"/>
/// (halheinrich/backgammon#131). Asserted through the public consumers of
/// the result — the decision iterator, the writer, the JSON round-trip —
/// never through the records: the builder's contract is what the match
/// <i>looks like</i> downstream, not which fields it stamped.
/// </summary>
public class XgFileBuilderTests
{
    private const string Xg = "synthetic.xg";

    // ------------------------------------------------------------------ //
    //  Plays used throughout, in the mover's numbering
    // ------------------------------------------------------------------ //

    /// <summary>24/23 from the standard opening.</summary>
    private static Play Play24To23 => Of(new Move(24, 23));

    /// <summary>8/5 6/5 — the classic 3-1.</summary>
    private static Play MakeFivePoint => Of(new Move(8, 5), new Move(6, 5));

    /// <summary>13/10 24/23 — the alternative 3-1.</summary>
    private static Play Split31 => Of(new Move(13, 10), new Move(24, 23));

    private static Play Of(params Move[] moves)
    {
        var play = new Play();
        foreach (var m in moves) play.Add(m);
        return play;
    }

    private static readonly DiceRoll ThreeOne = new(3, 1);

    private static List<DecisionRow> Rows(XgFile file, string source = Xg) =>
        XgDecisionIterator.Iterate(file, source).ToList();

    private static List<BgDecisionData> Requests(XgFile file, string source = Xg) =>
        XgDecisionIterator.IterateDiagramRequests(file, source).ToList();

    // ------------------------------------------------------------------ //
    //  Match level
    // ------------------------------------------------------------------ //

    [Fact]
    public void ForMatch_ZeroGames_IsAHeaderOnlyFile_EverySurfaceAccepts()
    {
        var file = XgFileBuilder.ForMatch(7, "Alice", "Bob").Build();

        var info = XgDecisionIterator.ExtractMatchInfo(file);
        info.Should().NotBeNull();
        info!.MatchLength.Should().Be(7);
        info.Player1.Should().Be("Alice");
        info.Player2.Should().Be("Bob");
        Rows(file).Should().BeEmpty("a match with no games has no decisions");

        // The single record is a complete file for the writer and the JSON path.
        var reread = XgFileReader.ReadStream(new MemoryStream(XgFileWriter.ToBytes(file)));
        XgDecisionIterator.ExtractMatchInfo(reread)!.MatchLength.Should().Be(7);
        Rows(reread).Should().BeEmpty();
    }

    [Fact]
    public void ForMoneySession_IsMoneyGame_WithJacobyStamped()
    {
        var builder = XgFileBuilder.ForMoneySession("Alice", "Bob", jacoby: true, beaver: false);
        builder.AddGame().Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        var file = builder.Build();

        var info = XgDecisionIterator.ExtractMatchInfo(file)!;
        ((IMatchInfo)info).IsMoneyGame.Should().BeTrue();
        info.MatchLength.Should().Be(0);

        var request = Requests(file).Should().ContainSingle().Subject;
        request.Position.IsJacoby.Should().BeTrue("the money-session Jacoby fact is stamped on every decision");
        request.Position.OnRollNeeds.Should().Be(0);
    }

    [Fact]
    public void ForMoneySession_JacobyOff_StampsFalse()
    {
        var builder = XgFileBuilder.ForMoneySession("Alice", "Bob", jacoby: false);
        builder.AddGame().Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);

        Requests(builder.Build()).Single().Position.IsJacoby.Should().BeFalse();
    }

    [Fact]
    public void ForMatch_JacobyStampIsNull_MatchPlayDoesNotPoseTheQuestion()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame().Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);

        Requests(builder.Build()).Single().Position.IsJacoby.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ForMatch_RejectsNonPositiveLength(int length)
    {
        var act = () => XgFileBuilder.ForMatch(length, "Alice", "Bob");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(null, "Bob")]
    [InlineData("", "Bob")]
    [InlineData("Alice", "  ")]
    public void ForMatch_RejectsBlankNames(string? p1, string? p2)
    {
        var act = () => XgFileBuilder.ForMatch(7, p1!, p2!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_SelfIdentifiesAsThisLibrary_AndIsDeterministic()
    {
        var builder = XgFileBuilder.ForMatch(5, "Alice", "Bob");
        builder.AddGame(0, 1).CubeDecision(XgPlayer.Player2, new XgCubeEquities(0.1, 0.0, 1.0));

        byte[] first = XgFileWriter.ToBytes(builder.Build());
        byte[] second = XgFileWriter.ToBytes(builder.Build());

        first.Should().Equal(second, "no timestamps or random ids — two builds write identical bytes");
        var header = (MatchHeaderRecord)XgFileReader.ReadStream(new MemoryStream(first)).Records[0];
        header.Location.Should().Be("ConvertXgToJson_Lib",
            "Location is the ecosystem's producer fingerprint; synthesized files carry the same one XgpExporter writes");
    }

    // ------------------------------------------------------------------ //
    //  Game level
    // ------------------------------------------------------------------ //

    [Fact]
    public void AddGame_NoDecisions_IsAValidEmptyGame()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(score1: 2, score2: 3);

        var state = new XgIteratorState();
        Rows(builder.Build()).Should().BeEmpty();

        // The game header is still there for the fast path to report.
        var bytes = XgFileWriter.ToBytes(builder.Build());
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xg");
        File.WriteAllBytes(path, bytes);
        try
        {
            var games = XgFileReader.ReadGameHeaders(path, state).ToList();
            var game = games.Should().ContainSingle().Subject;
            game.Away1.Should().Be(5);
            game.Away2.Should().Be(4);
            game.IsStandardStart.Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AddGame_ScoresBecomeAwayScores_FromTheDecisionMakersSide()
    {
        var builder = XgFileBuilder.ForMatch(5, "Alice", "Bob");
        var game = builder.AddGame(score1: 0, score2: 1);
        game.CubeDecision(XgPlayer.Player1, new XgCubeEquities(0, 0, 1));
        game.CubeDecision(XgPlayer.Player2, new XgCubeEquities(0, 0, 1));

        var rows = Rows(builder.Build());

        rows.Should().HaveCount(2);
        rows[0].Player.Should().Be("Alice");
        (rows[0].OnRollNeeds, rows[0].OpponentNeeds).Should().Be((5, 4));
        rows[1].Player.Should().Be("Bob");
        (rows[1].OnRollNeeds, rows[1].OpponentNeeds).Should().Be((4, 5),
            "the on-roll tuple is anchored to the decision-maker, not the header slot");
    }

    [Fact]
    public void AddGame_GamesAreNumberedInOrder_AndMoveNumbersRestart()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame().Play(XgPlayer.Player1, ThreeOne, MakeFivePoint)
                         .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);
        builder.AddGame(1, 0).Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);

        var rows = Rows(builder.Build());

        rows.Select(r => (r.Game, r.MoveNumber)).Should().Equal((1, 1), (1, 2), (2, 1));
    }

    [Fact]
    public void AddGame_Crawford_IsStampedOnDecisions()
    {
        var builder = XgFileBuilder.ForMatch(5, "Alice", "Bob");
        builder.AddGame(score1: 4, score2: 2, isCrawford: true).Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);

        Rows(builder.Build()).Single().IsCrawford.Should().BeTrue();
    }

    [Theory]
    [InlineData(7, -1, 0)]
    [InlineData(7, 0, 7)]
    [InlineData(7, 9, 0)]
    public void AddGame_RejectsScoresOutsideTheMatch(int length, int s1, int s2)
    {
        var act = () => XgFileBuilder.ForMatch(length, "Alice", "Bob").AddGame(s1, s2);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddGame_MoneySession_AllowsAnyNonNegativeScore()
    {
        var act = () => XgFileBuilder.ForMoneySession("Alice", "Bob").AddGame(12, 30);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(5, 0, 0)]   // neither one-away
    [InlineData(5, 4, 4)]   // both one-away
    public void AddGame_RejectsCrawfordAtANonCrawfordScore(int length, int s1, int s2)
    {
        var act = () => XgFileBuilder.ForMatch(length, "Alice", "Bob").AddGame(s1, s2, isCrawford: true);
        act.Should().Throw<ArgumentException>().WithParameterName("isCrawford");
    }

    [Fact]
    public void AddGame_RejectsCrawfordInMoney()
    {
        var act = () => XgFileBuilder.ForMoneySession("Alice", "Bob").AddGame(isCrawford: true);
        act.Should().Throw<ArgumentException>().WithParameterName("isCrawford");
    }

    [Fact]
    public void AddGame_DefaultStart_IsTheStandardOpening()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame().Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);

        var row = Rows(builder.Build()).Single();
        row.IsStandardStart.Should().BeTrue();
        row.Board.Should().Equal(BackgammonConstants.StandardOpeningPosition.Select(p => (int)p));
    }

    [Fact]
    public void AddGame_CustomStart_IsNotAStandardStart_AndIsTheDecisionBoard()
    {
        int[] oneCheckerOn24 = new int[26];
        oneCheckerOn24[24] = 1;
        oneCheckerOn24[1] = -1;
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame(initialPosition: oneCheckerOn24).Play(XgPlayer.Player1, ThreeOne, Play24To23);

        var row = Rows(builder.Build()).Single();
        row.IsStandardStart.Should().BeFalse();
        row.Board.Should().Equal(oneCheckerOn24);
    }

    public static TheoryData<string, int[]> InvalidPositions => new()
    {
        { "wrong length", new int[25] },
        { "count above 15", Cells((5, 16)) },
        { "player 1 on player 2's bar", Cells((0, 1)) },
        { "player 2 on player 1's bar", Cells((25, -1)) },
        { "more than 15 checkers", Cells((6, 15), (8, 1)) },
    };

    [Theory]
    [MemberData(nameof(InvalidPositions))]
    public void AddGame_RejectsAnInvalidPosition(string because, int[] position)
    {
        var act = () => XgFileBuilder.ForMatch(7, "Alice", "Bob").AddGame(initialPosition: position);
        act.Should().Throw<ArgumentException>(because).WithParameterName("initialPosition");
    }

    private static int[] Cells(params (int Index, int Count)[] cells)
    {
        var board = new int[26];
        foreach (var (i, c) in cells) board[i] = c;
        return board;
    }

    // ------------------------------------------------------------------ //
    //  Checker plays
    // ------------------------------------------------------------------ //

    [Fact]
    public void Play_Minimal_EmitsOneDecision_WithThePlayedMoveAsItsOnlyCandidate()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame().Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        var file = builder.Build();

        var row = Rows(file).Should().ContainSingle().Subject;
        row.Roll.Should().Be(31);
        row.Player.Should().Be("Alice");
        row.Error.Should().Be(0.0);
        row.AnalysisLevel.Should().Be(AnalysisLevel.Ply1);

        var request = Requests(file).Single();
        var candidate = request.Decision.Plays.Should().ContainSingle().Subject;
        candidate.MoveNotation.Should().Be("8/5 6/5");
        candidate.Play.Should().Be(MakeFivePoint);
        request.Decision.UserPlayIndex.Should().Be(0, "the played move is the candidate");
        request.Decision.UserPlayError.Should().Be(0.0);
    }

    [Fact]
    public void Play_WithCandidates_BestIsByEquity_AndTheErrorIsThePlayedMovesLoss()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame().Play(XgPlayer.Player1, ThreeOne, Split31,
        [
            new XgPlayCandidate(Split31, equity: 0.10, ply: 2),
            new XgPlayCandidate(MakeFivePoint, equity: 0.25, ply: 3),
        ]);
        var file = builder.Build();

        var row = Rows(file).Single();
        row.Equity.Should().BeApproximately(0.25, 1e-6, "the best candidate, regardless of list order");
        row.Error.Should().BeApproximately(0.15, 1e-6);
        row.AnalysisLevel.Should().Be(AnalysisLevel.Ply3, "depth follows the best candidate");

        var decision = Requests(file).Single().Decision;
        decision.Plays.Select(p => p.MoveNotation).Should().Equal("8/5 6/5", "24/23 13/10");
        decision.Plays[0].AnalysisLevel.Should().Be(AnalysisLevel.Ply3);
        decision.Plays[1].AnalysisLevel.Should().Be(AnalysisLevel.Ply2);
        decision.UserPlayIndex.Should().Be(1);
        decision.UserPlayError.Should().BeApproximately(0.15, 1e-6);
    }

    [Fact]
    public void Play_PlayedMoveOutsideCandidates_HasNoScoredError()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame().Play(XgPlayer.Player1, ThreeOne, Split31,
            [new XgPlayCandidate(MakeFivePoint, equity: 0.25)]);

        var decision = Requests(builder.Build()).Single().Decision;
        decision.UserPlayIndex.Should().Be(-1);
        decision.UserPlayError.Should().BeNull();
    }

    [Fact]
    public void Play_RejectsAnEmptyCandidateList()
    {
        var game = XgFileBuilder.ForMatch(7, "Alice", "Bob").AddGame();
        var act = () => game.Play(XgPlayer.Player1, ThreeOne, MakeFivePoint, []);
        act.Should().Throw<ArgumentException>().WithParameterName("candidates");
    }

    [Fact]
    public void Play_TracksThePosition_SoTheNextDecisionSeesIt()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint)
            .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);

        var rows = Rows(builder.Build());

        // Player 2's board is on-roll-relative: their own 8/5 6/5 is still to
        // come, but player 1's made 5-point shows as the opponent's point 20.
        var second = rows[1].Board;
        second[5].Should().Be(0, "player 2 has not made the 5-point yet");
        second[20].Should().Be(-2, "player 1's new 5-point is player 2's 20-point, two opposing checkers");
        second[6].Should().Be(5);
        second[8].Should().Be(3);
        rows[1].IsStandardStart.Should().BeTrue("the game still started from the opening");
    }

    [Fact]
    public void Play_ByPlayer2_IsReportedFromPlayer2sSide()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame().Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);

        var request = Requests(builder.Build()).Single();
        request.Descriptive.OnRollName.Should().Be("Bob");
        request.Descriptive.OpponentName.Should().Be("Alice");
        request.Position.Mop.Should().Equal(BackgammonConstants.StandardOpeningPosition.Select(p => (int)p),
            "the opening is symmetric, so the on-roll board reads the same from either side");
        request.Decision.Plays.Single().MoveNotation.Should().Be("8/5 6/5");
        request.Outcome.AfterPlayerBoard[20].Should().Be(-2,
            "after-boards are from the new on-roll player's (player 1's) side: Bob's 5-point is Alice's 20");
    }

    [Fact]
    public void Play_Hit_RequiresAndSendsTheBlotToTheBar()
    {
        int[] blotOnFive = (int[])BackgammonConstants.StandardOpeningPosition.Select(p => (int)p).ToArray();
        blotOnFive[5] = -1;                       // player 2 blot on player 1's 5-point
        blotOnFive[19] = -4;                      // taken from their 19 (keeps 15 checkers)
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        var game = builder.AddGame(initialPosition: blotOnFive);

        var notAHit = () => game.Play(XgPlayer.Player1, ThreeOne, Of(new Move(8, 5)));
        notAHit.Should().Throw<ArgumentException>("landing on a blot must be encoded as a hit");

        game.Play(XgPlayer.Player1, ThreeOne, Of(new Move(8, -5), new Move(6, 5)));
        var request = Requests(builder.Build()).Single();
        request.Decision.Plays.Single().MoveNotation.Should().Be("8/5* 6/5");
        request.Outcome.AfterPlayerBoard[25].Should().Be(1, "the hit checker sits on the new on-roll player's bar");
    }

    public static TheoryData<string, Move> IllegalMoves => new()
    {
        { "no checker on the from-point", new Move(7, 4) },
        { "blocked destination", new Move(13, 12) },      // player 2 holds 12 with 5
        { "hit flag without a blot", new Move(8, -5) },
        { "moving backwards", new Move(6, 8) },
        { "from-point out of range", new Move(26, 20) },
        { "to-point out of range", new Move(8, 25) },
    };

    [Theory]
    [MemberData(nameof(IllegalMoves))]
    public void Play_RejectsAPlayTheBoardCannotMake(string because, Move move)
    {
        var game = XgFileBuilder.ForMatch(7, "Alice", "Bob").AddGame();
        var act = () => game.Play(XgPlayer.Player1, ThreeOne, Of(move));
        act.Should().Throw<ArgumentException>(because).WithParameterName("played");
    }

    [Fact]
    public void Play_WithACheckerOnTheBar_MustEnterFirst()
    {
        int[] onBar = (int[])BackgammonConstants.StandardOpeningPosition.Select(p => (int)p).ToArray();
        onBar[25] = 1;
        onBar[13] = 4;                            // keeps player 1 at 15 checkers
        var game = XgFileBuilder.ForMatch(7, "Alice", "Bob").AddGame(initialPosition: onBar);

        var ignoresBar = () => game.Play(XgPlayer.Player1, ThreeOne, Of(new Move(8, 5)));
        ignoresBar.Should().Throw<ArgumentException>();

        var enters = () => game.Play(XgPlayer.Player1, ThreeOne, Of(new Move(25, 22), new Move(8, 7)));
        enters.Should().NotThrow();
    }

    [Fact]
    public void Play_RejectsACandidateTheBoardCannotMake_NamingTheCandidatesParameter()
    {
        var game = XgFileBuilder.ForMatch(7, "Alice", "Bob").AddGame();
        var act = () => game.Play(XgPlayer.Player1, ThreeOne, MakeFivePoint,
            [new XgPlayCandidate(Of(new Move(7, 4)), 0.0)]);
        act.Should().Throw<ArgumentException>().WithParameterName("candidates");
    }

    [Fact]
    public void AtPosition_ResetsTheTrackedPosition()
    {
        int[] oneCheckerOn24 = new int[26];
        oneCheckerOn24[24] = 1;
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint)
            .AtPosition(oneCheckerOn24)
            .Play(XgPlayer.Player1, ThreeOne, Play24To23);

        var rows = Rows(builder.Build());
        rows[1].Board.Should().Equal(oneCheckerOn24);
        rows[1].IsStandardStart.Should().BeTrue("the game header is untouched by a mid-game override");
    }

    [Fact]
    public void UnanalysedPlay_IsSkipped_ButAdvancesThePosition()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .UnanalysedPlay(XgPlayer.Player1, ThreeOne, MakeFivePoint)
            .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);

        var rows = Rows(builder.Build());
        var row = rows.Should().ContainSingle("the unanalysed play is not a decision").Subject;
        row.MoveNumber.Should().Be(2, "it still counts as a move of the game");
        row.Board[20].Should().Be(-2, "the unanalysed play was made");
    }

    [Fact]
    public void Dance_IsSkippedSilently_AndLeavesThePositionAlone()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .Dance(XgPlayer.Player1, new DiceRoll(6, 6))
            .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);

        var rows = Rows(builder.Build());
        rows.Should().ContainSingle().Which.MoveNumber.Should().Be(2);
        rows[0].Board.Should().Equal(BackgammonConstants.StandardOpeningPosition.Select(p => (int)p));
    }

    [Fact]
    public void IllegalPlay_IsSkipped_AndLeavesThePositionAlone()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .IllegalPlay(XgPlayer.Player1, new DiceRoll(5, 2))
            .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);

        var rows = Rows(builder.Build());
        rows.Should().ContainSingle().Which.MoveNumber.Should().Be(2);
        rows[0].Board.Should().Equal(BackgammonConstants.StandardOpeningPosition.Select(p => (int)p));
    }

    // ------------------------------------------------------------------ //
    //  Cube decisions
    // ------------------------------------------------------------------ //

    [Fact]
    public void CubeDecision_EmitsOneCubeRow_FromTheDoublersSide()
    {
        var builder = XgFileBuilder.ForMatch(5, "Alice", "Bob");
        builder.AddGame(0, 1).CubeDecision(XgPlayer.Player2, new XgCubeEquities(0.30, 0.45, 1.0), ply: 3);
        var file = builder.Build();

        var row = Rows(file).Should().ContainSingle().Subject;
        row.Player.Should().Be("Bob");
        row.Roll.Should().Be(0);
        row.AnalysisLevel.Should().Be(AnalysisLevel.Ply3);

        var request = Requests(file).Single();
        request.Decision.IsCube.Should().BeTrue();
        request.Decision.CubeDepth.Should().Be("3-ply");
        request.Decision.NoDoubleEquity.Should().BeApproximately(0.30, 1e-6);
        request.Decision.DoubleTakeEquity.Should().BeApproximately(0.45, 1e-6);
        request.Decision.CubeAnalysisLevel.Should().Be(AnalysisLevel.Ply3);
        request.Decision.UserDoublerAction.Should().BeNull("no action was recorded");
        request.Decision.UserTakerAction.Should().BeNull();
        request.Decision.UserDoubleError.Should().BeNull();
    }

    [Theory]
    [InlineData(CubeAction.NoDouble, null, CubeAction.NoDouble, null)]
    [InlineData(CubeAction.Double, null, CubeAction.Double, null)]
    [InlineData(CubeAction.Double, CubeAction.Take, CubeAction.Double, CubeAction.Take)]
    [InlineData(CubeAction.Double, CubeAction.Pass, CubeAction.Double, CubeAction.Pass)]
    public void CubeDecision_PlayedActions_RoundTripThroughTheIterator(
        CubeAction? doubler, CubeAction? taker, CubeAction? expectedDoubler, CubeAction? expectedTaker)
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame().CubeDecision(XgPlayer.Player1, new XgCubeEquities(0.2, 0.3, 1.0),
            doublerAction: doubler, takerAction: taker);

        var decision = Requests(builder.Build()).Single().Decision;
        decision.UserDoublerAction.Should().Be(expectedDoubler);
        decision.UserTakerAction.Should().Be(expectedTaker);
    }

    [Fact]
    public void CubeDecision_ErrorsAreDerivedFromEquitiesAndActions()
    {
        // Double/take is proper (0.45 > 0.30); not doubling costs 0.15,
        // passing instead of taking costs 0.55.
        var equities = new XgCubeEquities(NoDouble: 0.30, DoubleTake: 0.45, DoubleDrop: 1.0);

        var noDouble = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        noDouble.AddGame().CubeDecision(XgPlayer.Player1, equities, doublerAction: CubeAction.NoDouble);
        Requests(noDouble.Build()).Single().Decision.UserDoubleError.Should().BeApproximately(0.15, 1e-6);

        var doublePass = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        doublePass.AddGame().CubeDecision(XgPlayer.Player1, equities,
            doublerAction: CubeAction.Double, takerAction: CubeAction.Pass);
        var d = Requests(doublePass.Build()).Single().Decision;
        d.UserDoubleError.Should().Be(0.0);
        d.UserTakeError.Should().BeApproximately(0.55, 1e-6);

        var doubleTake = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        doubleTake.AddGame().CubeDecision(XgPlayer.Player1, equities,
            doublerAction: CubeAction.Double, takerAction: CubeAction.Take);
        Requests(doubleTake.Build()).Single().Decision.UserTakeError.Should().Be(0.0);
    }

    [Fact]
    public void CubeDecision_TakenDouble_RaisesTheCubeToTheTaker()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .CubeDecision(XgPlayer.Player1, new XgCubeEquities(0.2, 0.3, 1.0),
                doublerAction: CubeAction.Double, takerAction: CubeAction.Take)
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);

        var play = Requests(builder.Build()).Last();
        play.Decision.IsCube.Should().BeFalse();
        play.Position.CubeSize.Should().Be(2);
        play.Position.CubeOwner.Should().Be(CubeOwner.Opponent, "Bob took, so Bob owns the 2-cube");
    }

    [Fact]
    public void CubeDecision_Pass_EndsTheGame_FurtherDecisionsThrow()
    {
        var game = XgFileBuilder.ForMatch(7, "Alice", "Bob").AddGame()
            .CubeDecision(XgPlayer.Player1, new XgCubeEquities(0.2, 0.3, 1.0),
                doublerAction: CubeAction.Double, takerAction: CubeAction.Pass);

        var act = () => game.Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(CubeAction.Take, null)]            // wrong half
    [InlineData(CubeAction.Pass, null)]
    [InlineData(null, CubeAction.Double)]          // wrong half
    [InlineData(null, CubeAction.Take)]            // reply without a double
    [InlineData(CubeAction.NoDouble, CubeAction.Take)]
    public void CubeDecision_RejectsInconsistentActions(CubeAction? doubler, CubeAction? taker)
    {
        var game = XgFileBuilder.ForMatch(7, "Alice", "Bob").AddGame();
        var act = () => game.CubeDecision(XgPlayer.Player1, new XgCubeEquities(0, 0, 1),
            doublerAction: doubler, takerAction: taker);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(1)]   // level 0: invisible to the iterator's analysed-cube gate
    [InlineData(8)]
    public void CubeDecision_RejectsPlyOutsideTheTaxonomy(int ply)
    {
        var game = XgFileBuilder.ForMatch(7, "Alice", "Bob").AddGame();
        var act = () => game.CubeDecision(XgPlayer.Player1, new XgCubeEquities(0, 0, 1), ply);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void XgPlayCandidate_RejectsPlyOutsideTheTaxonomy(int ply)
    {
        var act = () => new XgPlayCandidate(MakeFivePoint, 0.0, ply);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UnanalysedCube_IsSkipped_ButStillMovesTheCube()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .UnanalysedCube(XgPlayer.Player2, CubeAction.Double, CubeAction.Take)
            .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);

        var requests = Requests(builder.Build());
        var play = requests.Should().ContainSingle("the unanalysed cube is not a decision").Subject;
        play.Position.CubeSize.Should().Be(2);
        play.Position.CubeOwner.Should().Be(CubeOwner.Opponent, "Alice took Bob's double");
    }

    // ------------------------------------------------------------------ //
    //  The result is a real XgFile: every consumer path agrees
    // ------------------------------------------------------------------ //

    private static XgFile FullMatch()
    {
        var builder = XgFileBuilder.ForMatch(5, "Alice", "Bob");
        builder.AddGame()
            .UnanalysedCube(XgPlayer.Player1, CubeAction.NoDouble)
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint,
                [new XgPlayCandidate(MakeFivePoint, 0.2, 3), new XgPlayCandidate(Split31, 0.1, 3)])
            .CubeDecision(XgPlayer.Player2, new XgCubeEquities(-0.1, -0.3, 1.0), 3, CubeAction.NoDouble)
            .Play(XgPlayer.Player2, new DiceRoll(6, 1), Of(new Move(13, 7), new Move(8, 7)))
            .Dance(XgPlayer.Player1, new DiceRoll(5, 5));
        builder.AddGame(0, 1)
            .CubeDecision(XgPlayer.Player2, new XgCubeEquities(0.6, 0.7, 1.0), 2, CubeAction.Double, CubeAction.Take)
            .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);
        return builder.Build();
    }

    [Fact]
    public void Build_WriterRoundTrip_YieldsTheSameDecisions()
    {
        var built = FullMatch();
        var reread = XgFileReader.ReadStream(new MemoryStream(XgFileWriter.ToBytes(built)));

        Requests(reread).Select(Fingerprint).Should().Equal(Requests(built).Select(Fingerprint),
            "the synthesized records are complete enough for the writer's binary round-trip");
        Rows(built).Should().HaveCount(5);
    }

    [Fact]
    public void Build_JsonRoundTrip_YieldsTheSameDecisions()
    {
        var built = FullMatch();
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, XgFileReader.ToJson(built));
        try
        {
            var reread = XgFileReader.ReadJson(path);
            Requests(reread, "synthetic.json").Select(Fingerprint)
                .Should().Equal(Requests(built, "synthetic.json").Select(Fingerprint));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Build_AsAnXgpSource_FollowsTheSingleDecisionPolicy()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .CubeDecision(XgPlayer.Player1, new XgCubeEquities(0, 0, 1))
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);

        var rows = Rows(builder.Build(), "synthetic.xgp");
        rows.Should().ContainSingle().Which.Roll.Should().Be(31, "an .xgp yields its analysed play over its cube");
    }

    [Fact]
    public void Build_SliceExport_CarriesTheAnalysedDecision()
    {
        var built = FullMatch();
        var sliced = XgFileReader.ReadStream(new MemoryStream(XgpExporter.ToBytes(built, game: 1, moveNumber: 1, isCube: false)));

        var request = Requests(sliced, "slice.xgp").Should().ContainSingle().Subject;
        request.Decision.Plays.Select(p => p.MoveNotation).Should().Equal("8/5 6/5", "24/23 13/10");
    }

    private static string Fingerprint(BgDecisionData d) =>
        $"{d.Id}|{d.Xgid}|{d.Decision.IsCube}|{string.Join(",", d.Decision.Plays.Select(p => $"{p.MoveNotation}@{p.Equity}"))}" +
        $"|{d.Decision.NoDoubleEquity}|{d.Decision.DoubleTakeEquity}|{d.Decision.UserDoublerAction}|{d.Decision.UserTakerAction}" +
        $"|{d.Position.CubeSize}|{d.Position.CubeOwner}|{d.Position.OnRollNeeds}|{d.Position.OpponentNeeds}";
}
