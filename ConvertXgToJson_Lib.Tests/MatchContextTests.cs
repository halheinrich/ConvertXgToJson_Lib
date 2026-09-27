using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Unit tests for <see cref="MatchContext"/>: the session it builds for a
/// decision from the match header's terms and the current game's standing
/// (<see cref="Session.Create"/>, never an orientation of its own), the
/// Crawford game the walk's cube rule asks about, and the names and comments
/// it reads as a record states them — <see langword="null"/> for none.
/// </summary>
public class MatchContextTests
{
    private static MatchContext Context(
        int matchLength, bool jacoby = false, bool beaver = false, List<string>? comments = null,
        string player1 = "Alice", string player2 = "Bob") =>
        new(
            [new MatchHeaderRecord
            {
                MatchLength = matchLength,
                CubeLimit = XgMatchInfo.DefaultCubeLimitExponent,
                Jacoby = jacoby,
                Beaver = beaver,
                Player1 = player1,
                Player2 = player2,
            }],
            comments ?? []);

    private static GameHeaderRecord Game(int score1 = 0, int score2 = 0, bool crawford = false, bool standard = true) => new()
    {
        Score1 = score1,
        Score2 = score2,
        CrawfordApplies = crawford,
        InitialPosition = standard
            ? new PositionEngine { Points = XgGameBuilder.PointsOf(BoardPosition.Standard) }
            : new PositionEngine(),
    };

    private const int Money = MatchHeaderRecord.MoneyMatchLengthSentinel;

    // -----------------------------------------------------------------------
    //  The session: terms and standing, turned to the seat on roll
    // -----------------------------------------------------------------------

    [Fact]
    public void Match_SessionFor_TurnsTheStandingToTheSeatOnRoll()
    {
        var ctx = Context(matchLength: 7);
        ctx.Update(Game(score1: 2, score2: 5));

        ctx.SessionFor(Seat.Player1).Should().Be(
            Session.Create(new MatchTerms { Length = 7 }, new MatchStanding { Away1 = 5, Away2 = 2, IsCrawford = false }, Seat.Player1));
        var player2 = (MatchSession)ctx.SessionFor(Seat.Player2);
        player2.OnRollNeeds.Should().Be(2);
        player2.OpponentNeeds.Should().Be(5);
        player2.IsCrawford.Should().BeFalse();
    }

    [Fact]
    public void Match_CrawfordGame_IsTheStandingsFact()
    {
        var ctx = Context(matchLength: 7);
        ctx.Update(Game(score1: 6, score2: 3, crawford: true));

        ctx.IsCrawford.Should().BeTrue();
        ((MatchSession)ctx.SessionFor(Seat.Player2)).IsCrawford.Should().BeTrue();

        ctx.Update(Game(score1: 6, score2: 4));
        ctx.IsCrawford.Should().BeFalse("the flag is each game's own");
    }

    /// <summary>
    /// A match header's Jacoby and beaver flags have no place in a match
    /// session: its terms are its length alone, so nothing of them reaches a
    /// record.
    /// </summary>
    [Fact]
    public void Match_HeaderRuleFlags_DoNotReachTheSession()
    {
        var ctx = Context(matchLength: 7, jacoby: true, beaver: true);
        ctx.Update(Game());

        ctx.MatchInfo.Terms.Should().Be(new MatchTerms { Length = 7 });
        ctx.SessionFor(Seat.Player1).Should().BeOfType<MatchSession>();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Money_SessionFor_StatesTheRulesAndTheOrientedScores(bool jacoby, bool beaver)
    {
        var ctx = Context(Money, jacoby, beaver);
        ctx.Update(Game(score1: 3, score2: 1));

        var session = (MoneySession)ctx.SessionFor(Seat.Player2);
        session.Terms.Should().Be(new MoneyTerms { IsJacoby = jacoby, IsBeaver = beaver, CubeLimit = 1024 });
        session.OnRollScore.Should().Be(1);
        session.OpponentScore.Should().Be(3);
    }

    /// <summary>
    /// A money session has no Crawford game: a (malformed) game header
    /// claiming one leaves no trace, since a money standing has no flag to
    /// carry it.
    /// </summary>
    [Fact]
    public void Money_SpuriousCrawfordFlag_IsNoCrawfordGame()
    {
        var ctx = Context(Money, jacoby: true);
        ctx.Update(Game(crawford: true));

        ctx.IsCrawford.Should().BeFalse();
        ctx.GameInfo!.Standing.Should().BeOfType<MoneyStanding>();
    }

    [Fact]
    public void SessionFor_BeforeAnyGameHeader_IsRefused()
    {
        FluentActions.Invoking(() => Context(matchLength: 7).SessionFor(Seat.Player1))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Construction_WithoutALeadingMatchHeader_IsRefused()
    {
        FluentActions.Invoking(() => new MatchContext([new GameHeaderRecord()], []))
            .Should().Throw<InvalidDataException>();
    }

    // -----------------------------------------------------------------------
    //  The game's metadata, per game
    // -----------------------------------------------------------------------

    /// <summary>
    /// The current game's metadata is rebuilt at each game header, so a
    /// standard start in game 1 does not leak into a saved-position game 2.
    /// </summary>
    [Fact]
    public void GameInfo_IsRebuiltAtEachGameHeader()
    {
        var ctx = Context(matchLength: 7);
        ctx.GameInfo.Should().BeNull("no game has begun");

        ctx.Update(Game(standard: true));
        ctx.GameInfo!.IsStandardStart.Should().BeTrue();
        ctx.GameNumber.Should().Be(1);

        ctx.Update(Game(standard: false));
        ctx.GameInfo!.IsStandardStart.Should().BeFalse("the start is each game's own");
        ctx.GameNumber.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    //  None recorded is null
    // -----------------------------------------------------------------------

    [Fact]
    public void NameOf_IsTheSeatsName_OrNullWhenTheHeaderRecordsNone()
    {
        var ctx = Context(matchLength: 7, player1: "Alice", player2: "  ");

        ctx.NameOf(Seat.Player1).Should().Be("Alice");
        ctx.NameOf(Seat.Player2).Should().BeNull("a record states no name rather than an empty one");
    }

    [Fact]
    public void SeatOf_ReadsXgsActivePlayerSign()
    {
        MatchContext.SeatOf(1).Should().Be(Seat.Player1);
        MatchContext.SeatOf(0).Should().Be(Seat.Player1);
        MatchContext.SeatOf(-1).Should().Be(Seat.Player2);
    }

    /// <summary>
    /// XG's no-comment index, an index past the table and a comment whose
    /// text is empty are all none: <see langword="null"/>, never an empty
    /// comment, and never another entry of the table.
    /// </summary>
    [Fact]
    public void CommentAt_IsTheCommentsText_OrNullWhenThereIsNone()
    {
        var ctx = Context(matchLength: 7, comments: ["first note", "", "  "]);

        ctx.CommentAt(0).Should().Be("first note");
        ctx.CommentAt(1).Should().BeNull();
        ctx.CommentAt(2).Should().BeNull();
        ctx.CommentAt(-1).Should().BeNull();
        ctx.CommentAt(3).Should().BeNull();
    }
}
