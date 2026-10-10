using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The frames <see cref="XgGameBuilder"/> writes positions in, read off the
/// records and compared with boards stated by hand (halheinrich/backgammon#290).
/// XG stores a decision's starting position in player 1's frame and every
/// resulting position — each candidate's, the position after the move — in
/// the mover's, for either seat (INSTRUCTIONS.md, "Board format"); the
/// builder writes the same. <see cref="XgFileBuilderTests"/> asserts the
/// builder through its consumers, but no consumer shows a stored frame
/// except the opening-book key, whose agreement with the builder would prove
/// nothing — so these read the records, against references no builder made.
/// </summary>
public class XgGameBuilderFrameTests
{
    // ------------------------------------------------------------------ //
    //  Boards stated by hand: index 0 the opponent's bar, 1–24 the points
    //  from the frame's owner's side, 25 the owner's bar; the owner's
    //  checkers positive.
    // ------------------------------------------------------------------ //

    /// <summary>The standard start — the same in every frame.</summary>
    private static readonly sbyte[] Standard =
        [0, -2, 0, 0, 0, 0, 5, 0, 3, 0, 0, 0, -5, 5, 0, 0, 0, -3, 0, -5, 0, 0, 0, 0, 2, 0];

    /// <summary>13/9 6/5 from the standard start, in the mover's frame.</summary>
    private static readonly sbyte[] After1394And65 =
        [0, -2, 0, 0, 0, 1, 4, 0, 3, 1, 0, 0, -5, 4, 0, 0, 0, -3, 0, -5, 0, 0, 0, 0, 2, 0];

    /// <summary><see cref="After1394And65"/> seen by the other side.</summary>
    private static readonly sbyte[] After1394And65Turned =
        [0, -2, 0, 0, 0, 0, 5, 0, 3, 0, 0, 0, -4, 5, 0, 0, -1, -3, 0, -4, -1, 0, 0, 0, 2, 0];

    /// <summary>24/20 6/5 from the standard start, in the mover's frame.</summary>
    private static readonly sbyte[] After2420And65 =
        [0, -2, 0, 0, 0, 1, 4, 0, 3, 0, 0, 0, -5, 5, 0, 0, 0, -3, 0, -5, 1, 0, 0, 0, 1, 0];

    /// <summary>
    /// The reply 13/7 8/7 to 13/9 6/5, in the replying mover's frame — what a
    /// record stores for the reply, whichever seat made it.
    /// </summary>
    private static readonly sbyte[] Reply137And87 =
        [0, -2, 0, 0, 0, 0, 5, 2, 2, 0, 0, 0, -4, 4, 0, 0, -1, -3, 0, -4, -1, 0, 0, 0, 2, 0];

    /// <summary>The reply 8/2 6/5 to 13/9 6/5, in the replying mover's frame.</summary>
    private static readonly sbyte[] Reply82And65 =
        [0, -2, 1, 0, 0, 1, 4, 0, 2, 0, 0, 0, -4, 5, 0, 0, -1, -3, 0, -4, -1, 0, 0, 0, 2, 0];

    /// <summary><see cref="Reply137And87"/> seen by the first mover — player 1's frame when player 2 replied.</summary>
    private static readonly sbyte[] Reply137And87Turned =
        [0, -2, 0, 0, 0, 1, 4, 0, 3, 1, 0, 0, -4, 4, 0, 0, 0, -2, -2, -5, 0, 0, 0, 0, 2, 0];

    /// <summary>13/11 6/5 from <see cref="Reply137And87Turned"/>, in the mover's frame.</summary>
    private static readonly sbyte[] ThenAfter1311And65 =
        [0, -2, 0, 0, 0, 2, 3, 0, 3, 1, 0, 1, -4, 3, 0, 0, 0, -2, -2, -5, 0, 0, 0, 0, 2, 0];

    private static readonly DiceRoll FourOne = new(4, 1);
    private static readonly DiceRoll SixOne = new(6, 1);
    private static readonly DiceRoll TwoOne = new(2, 1);

    private static Play Play1394And65 => Play.Create(new Move(13, 9), new Move(6, 5));
    private static Play Play2420And65 => Play.Create(new Move(24, 20), new Move(6, 5));
    private static Play Play137And87 => Play.Create(new Move(13, 7), new Move(8, 7));
    private static Play Play82And65 => Play.Create(new Move(8, 2), new Move(6, 5));
    private static Play Play1311And65 => Play.Create(new Move(13, 11), new Move(6, 5));

    private static List<MoveRecord> MoveRecords(XgFile file) => file.Records.OfType<MoveRecord>().ToList();

    // ------------------------------------------------------------------ //
    //  Each play kind, each seat
    // ------------------------------------------------------------------ //

    /// <summary>
    /// An analysed play stores every candidate's resulting position, and the
    /// position after the move, in the mover's frame — player 2's reply
    /// included — while each decision starts from player 1's frame, the game
    /// going on after player 2's play as it should.
    /// </summary>
    [Fact]
    public void AnalysedPlays_StoreResultsInTheMoversFrame_AndTheGameGoesOnAfterAPlayer2Play()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .Play(XgPlayer.Player1, FourOne, Play1394And65,
                [new XgPlayCandidate(Play1394And65, 0.10), new XgPlayCandidate(Play2420And65, 0.05)])
            .Play(XgPlayer.Player2, SixOne, Play137And87,
                [new XgPlayCandidate(Play137And87, 0.10), new XgPlayCandidate(Play82And65, 0.05)])
            .Play(XgPlayer.Player1, TwoOne, Play1311And65);
        var file = builder.Build();
        var moves = MoveRecords(file);

        moves.Should().HaveCount(3);

        moves[0].InitialPosition.Points.Should().Equal(Standard);
        moves[0].Analysis.PositionsPlayed.Should().HaveCount(2);
        moves[0].Analysis.PositionsPlayed[0].Points.Should().Equal(After1394And65);
        moves[0].Analysis.PositionsPlayed[1].Points.Should().Equal(After2420And65);
        moves[0].FinalPosition.Points.Should().Equal(After1394And65);

        moves[1].InitialPosition.Points.Should().Equal(After1394And65, "player 1's frame, which player 1's play left");
        moves[1].Analysis.PositionsPlayed.Should().HaveCount(2);
        moves[1].Analysis.PositionsPlayed[0].Points.Should().Equal(Reply137And87, "player 2's own frame");
        moves[1].Analysis.PositionsPlayed[1].Points.Should().Equal(Reply82And65, "player 2's own frame");
        moves[1].FinalPosition.Points.Should().Equal(Reply137And87, "player 2's own frame");

        moves[2].InitialPosition.Points.Should().Equal(Reply137And87Turned,
            "the game goes on from player 2's play, in player 1's frame");
        moves[2].Analysis.PositionsPlayed.Should().ContainSingle().Which.Points.Should().Equal(ThenAfter1311And65);
        moves[2].FinalPosition.Points.Should().Equal(ThenAfter1311And65);

        // The played candidate is still the one whose stored result is the
        // stored position after the move, for both seats.
        XgDecisionIterator.IterateDiagramRequests(file, "frames.xg").OfType<CheckerPlayDecision>()
            .Select(play => play.Decision.UserPlayIndex).Should().Equal(0, 0, 0);
    }

    /// <summary>An unanalysed play stores the position after it in the mover's frame.</summary>
    [Fact]
    public void UnanalysedPlays_StoreThePositionAfterInTheMoversFrame()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .UnanalysedPlay(XgPlayer.Player1, FourOne, Play1394And65)
            .UnanalysedPlay(XgPlayer.Player2, SixOne, Play137And87)
            .UnanalysedPlay(XgPlayer.Player1, TwoOne, Play1311And65);
        var moves = MoveRecords(builder.Build());

        moves.Select(move => move.InitialPosition.Points)
            .Should().BeEquivalentTo([Standard, After1394And65, Reply137And87Turned], o => o.WithStrictOrdering());
        moves.Select(move => move.FinalPosition.Points)
            .Should().BeEquivalentTo([After1394And65, Reply137And87, ThenAfter1311And65], o => o.WithStrictOrdering());
    }

    /// <summary>
    /// A dance and XG's illegal-play marker leave the position unchanged, and
    /// store it — as the position after the move and as the sentinel
    /// candidate's result — in the mover's frame: turned for player 2,
    /// as tracked for player 1. The game goes on from the unchanged position.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SentinelPlays_StoreTheUnchangedPositionInTheMoversFrame(bool dance)
    {
        XgGameBuilder Sentinel(XgGameBuilder game, XgPlayer player) =>
            dance ? game.Dance(player, new DiceRoll(6, 6)) : game.IllegalPlay(player, new DiceRoll(5, 2));

        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        Sentinel(builder.AddGame().UnanalysedPlay(XgPlayer.Player1, FourOne, Play1394And65), XgPlayer.Player2)
            .UnanalysedPlay(XgPlayer.Player1, TwoOne, Play.Create(new Move(13, 11), new Move(6, 5)));
        Sentinel(builder.AddGame().UnanalysedPlay(XgPlayer.Player2, FourOne, Play1394And65), XgPlayer.Player1);
        var moves = MoveRecords(builder.Build());

        moves.Should().HaveCount(5);

        var player2Sentinel = moves[1];
        player2Sentinel.ActivePlayer.Should().Be(-1);
        player2Sentinel.InitialPosition.Points.Should().Equal(After1394And65, "player 1's frame");
        player2Sentinel.FinalPosition.Points.Should().Equal(After1394And65Turned, "player 2's own frame");
        player2Sentinel.Analysis.PositionsPlayed.Should().ContainSingle().Which.Points.Should().Equal(After1394And65Turned);
        moves[2].InitialPosition.Points.Should().Equal(After1394And65, "the game goes on from the unchanged position");

        var player1Sentinel = moves[4];
        player1Sentinel.ActivePlayer.Should().Be(1);
        player1Sentinel.InitialPosition.Points.Should().Equal(After1394And65Turned, "player 2 opened: player 1's frame");
        player1Sentinel.FinalPosition.Points.Should().Equal(After1394And65Turned, "player 1's own frame");
        player1Sentinel.Analysis.PositionsPlayed.Should().ContainSingle().Which.Points.Should().Equal(After1394And65Turned);
    }
}
