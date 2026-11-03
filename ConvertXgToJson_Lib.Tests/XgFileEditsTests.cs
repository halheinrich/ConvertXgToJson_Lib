using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.TestSupport;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// <see cref="XgFileEdits"/>, read through the walk, which is what every test
/// that uses it relies on: each edit is one omission of its kind and leaves the
/// other decisions as they were (halheinrich/backgammon#514). Synthesized, so it
/// needs no corpus.
/// </summary>
public class XgFileEditsTests
{
    private const string Source = "match.xg";

    private static readonly (DiceRoll Dice, Play Played)[] OpeningMoves =
    [
        (new DiceRoll(3, 1), Play.Create(new Move(8, 5), new Move(6, 5))),
        (new DiceRoll(4, 2), Play.Create(new Move(8, 4), new Move(6, 4))),
        (new DiceRoll(6, 1), Play.Create(new Move(13, 7), new Move(8, 7))),
    ];

    /// <summary>A 7-point match of <paramref name="games"/> games of three decisions each, every decision a real choice.</summary>
    private static XgFile Match(int games)
    {
        var builder = XgFileBuilder.ForMatch(7, "P1", "P2");
        for (int game = 0; game < games; game++)
            Opening(builder.AddGame(score1: game, score2: 0));
        return builder.Build();
    }

    private static XgGameBuilder Opening(XgGameBuilder game)
    {
        for (int i = 0; i < OpeningMoves.Length; i++)
            game.Play(i % 2 == 0 ? XgPlayer.Player1 : XgPlayer.Player2, OpeningMoves[i].Dice, OpeningMoves[i].Played);
        return game;
    }

    private static (List<BgDecisionData> Decisions, IReadOnlyList<XgOmission> Omissions) Walk(XgFile file)
    {
        var state = new XgIteratorState();
        var decisions = XgDecisionIterator.IterateDiagramRequests(file, Source, state).ToList();
        return (decisions, state.Omissions);
    }

    /// <summary>
    /// The decisions as they stand, ids included, compared by the JSON they
    /// write: the edit's read-back makes new objects of equal content.
    /// </summary>
    private static string Written(IEnumerable<BgDecisionData> decisions) =>
        JsonSerializer.Serialize(decisions.ToList());

    // -----------------------------------------------------------------------
    //  A refused game
    // -----------------------------------------------------------------------

    [Fact]
    public void ARefusedGame_IsOneInvalidStandingOmission_AndTheOtherGamesAreUnchanged()
    {
        var file = Match(3);
        var (before, _) = Walk(file);

        var (after, omissions) = Walk(XgFileEdits.WithRefusedGames(file, 2));

        var omission = omissions.Should().ContainSingle().Subject;
        omission.Kind.Should().Be(XgOmissionKind.InvalidStanding);
        omission.Game.Should().Be(2);
        omission.IsWholeGame.Should().BeTrue();
        after.Select(d => d.Id).Should().Equal(before.Where(d => d.Game != 2).Select(d => d.Id),
            "the other games keep their identifiers, game 3 its number");
        Written(after).Should().Be(Written(before.Where(d => d.Game != 2)));
    }

    [Fact]
    public void SeveralRefusedGames_AreAnOmissionEach()
    {
        var (decisions, omissions) = Walk(XgFileEdits.WithRefusedGames(Match(3), 1, 3));

        omissions.Select(o => o.Game).Should().Equal(1, 3);
        omissions.Should().OnlyContain(o => o.Kind == XgOmissionKind.InvalidStanding);
        decisions.Select(d => d.Game).Distinct().Should().Equal(2);
    }

    [Fact]
    public void InAMoneyGame_NothingIsRefused()
    {
        var builder = XgFileBuilder.ForMoneySession("P1", "P2");
        Opening(builder.AddGame());

        var (decisions, omissions) = Walk(XgFileEdits.WithRefusedGames(builder.Build(), 1));

        omissions.Should().BeEmpty("a money game has no standing the session refuses, as the doc says");
        decisions.Should().HaveCount(OpeningMoves.Length);
    }

    // -----------------------------------------------------------------------
    //  A corrupt candidate
    // -----------------------------------------------------------------------

    [Fact]
    public void ACorruptCandidate_IsOneCorruptCandidateOmission_AndTheDecisionsAroundItAreKept()
    {
        var file = Match(3);
        var (before, _) = Walk(file);

        var (after, omissions) = Walk(XgFileEdits.WithCorruptCandidate(file, 2, 2));

        var omission = omissions.Should().ContainSingle().Subject;
        omission.Kind.Should().Be(XgOmissionKind.CorruptCandidate);
        omission.Game.Should().Be(2);
        omission.MoveNumber.Should().Be(2);
        omission.Roll.Should().Be(OpeningMoves[1].Dice);
        var rest = before.Where(d => (d.Game, d.MoveNumber) != (2, 2)).ToList();
        after.Select(d => d.Id).Should().Equal(rest.Select(d => d.Id));
        Written(after).Should().Be(Written(rest));
    }

    [Fact]
    public void ACorruptCandidate_CountsMoveRecords_NotCubeRecords()
    {
        var builder = XgFileBuilder.ForMatch(7, "P1", "P2");
        Opening(builder.AddGame().CubeDecision(XgPlayer.Player1, new XgCubeEquities(0.5, 0.6, 1.0)));

        var (_, omissions) = Walk(XgFileEdits.WithCorruptCandidate(builder.Build(), 1, 1));

        omissions.Should().ContainSingle().Which.MoveNumber.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    //  The two together, and the result as a file
    // -----------------------------------------------------------------------

    [Fact]
    public void BothEditsInOneFile_AreOneOmissionEach_AndTheRestIsUnchanged()
    {
        var file = Match(3);
        var (before, _) = Walk(file);

        var (after, omissions) = Walk(XgFileEdits.WithCorruptCandidate(XgFileEdits.WithRefusedGames(file, 1), 3, 1));

        omissions.Select(o => (o.Kind, o.Game, o.MoveNumber)).Should().Equal(
            (XgOmissionKind.InvalidStanding, 1, null),
            (XgOmissionKind.CorruptCandidate, 3, 1));
        Written(after).Should().Be(Written(before.Where(d => d.Game != 1 && (d.Game, d.MoveNumber) != (3, 1))));
    }

    [Fact]
    public void AnEditedFile_WrittenAsAnXg_ReadsBackToTheSameWalk()
    {
        var edited = XgFileEdits.WithCorruptCandidate(XgFileEdits.WithRefusedGames(Match(3), 1), 3, 1);

        var read = XgFileReader.ReadStream(new MemoryStream(XgFileWriter.ToBytes(edited)));

        var (decisions, omissions) = Walk(edited);
        var (decisionsRead, omissionsRead) = Walk(read);
        omissionsRead.Should().Equal(omissions);
        Written(decisionsRead).Should().Be(Written(decisions));
    }

    [Fact]
    public void TheInputFile_IsUnchanged()
    {
        var file = Match(3);
        string before = XgFileReader.ToJson(file);

        _ = XgFileEdits.WithRefusedGames(file, 1);
        _ = XgFileEdits.WithCorruptCandidate(file, 2, 1);

        XgFileReader.ToJson(file).Should().Be(before);
        Walk(file).Omissions.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    //  A game or move outside the file
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    public void ARefusedGameOutsideTheFile_IsRefusedNamingTheGames(int game)
    {
        var act = () => XgFileEdits.WithRefusedGames(Match(3), 1, game);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Where(e => e.ParamName == "games" && Equals(e.ActualValue, game))
            .WithMessage("*3 games*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    public void ACandidatesGameOutsideTheFile_IsRefusedNamingTheGame(int game)
    {
        var act = () => XgFileEdits.WithCorruptCandidate(Match(3), game, 1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Where(e => e.ParamName == "game" && Equals(e.ActualValue, game))
            .WithMessage("*3 games*");
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 4)]
    [InlineData(3, 4)]
    public void ACandidatesMoveOutsideItsGame_IsRefusedNamingTheMove(int game, int move)
    {
        var act = () => XgFileEdits.WithCorruptCandidate(Match(3), game, move);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Where(e => e.ParamName == "move" && Equals(e.ActualValue, move))
            .WithMessage($"Game {game} holds 3 moves*");
    }

    [Fact]
    public void AnUnanalysedMove_IsRefusedNamingTheMove()
    {
        var builder = XgFileBuilder.ForMatch(7, "P1", "P2");
        builder.AddGame().UnanalysedPlay(XgPlayer.Player1, OpeningMoves[0].Dice, OpeningMoves[0].Played);

        var act = () => XgFileEdits.WithCorruptCandidate(builder.Build(), 1, 1);

        act.Should().Throw<ArgumentException>().Where(e => e.GetType() == typeof(ArgumentException) && e.ParamName == "move")
            .WithMessage("*not analysed*");
    }

    [Fact]
    public void NullArguments_AreRefused()
    {
        FluentActions.Invoking(() => XgFileEdits.WithRefusedGames(null!, 1)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => XgFileEdits.WithRefusedGames(Match(1), null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => XgFileEdits.WithCorruptCandidate(null!, 1, 1)).Should().Throw<ArgumentNullException>();
    }
}
