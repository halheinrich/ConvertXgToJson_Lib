using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins the ranking the iterator judges decisions under
/// (<see cref="XgIteratorOptions.Ranking"/>, SPEC-scoring §2a): every row
/// <see cref="XgDecisionIterator.Iterate"/> yields is its record's projection
/// for that ranking (<see cref="DecisionRow.From"/>), the post-yield callbacks
/// of both surfaces see each decision through a view for it, and an
/// application that states none gets the default, <see cref="PlayRanking.Equity"/>.
/// A row and its record therefore cannot disagree — the agreement the two
/// parallel constructions once needed a pin for is structural.
///
/// <para>
/// The fixture is synthesized through the builders: one checker play whose
/// two rankings disagree — a shallow candidate rated higher than a deeper one,
/// the deeper one played — beside one cube decision.
/// </para>
/// </summary>
public class XgDecisionIteratorRankingTests
{
    private static readonly Play Shallow = Play.Create(new Move(13, 10), new Move(13, 11));
    private static readonly Play Deep = Play.Create(new Move(8, 5), new Move(6, 5));

    /// <summary>
    /// A 3-1 from the opening: 13/10 13/11 at 1-ply rated above 8/5 6/5 at
    /// 3-ply, and 8/5 6/5 played — equity's best is the shallow play, depth
    /// first's the deep one — then the opponent's cube decision.
    /// </summary>
    private static XgFile Fixture()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .Play(XgPlayer.Player1, new DiceRoll(3, 1), Deep,
                [new XgPlayCandidate(Shallow, 0.20, ply: 1), new XgPlayCandidate(Deep, 0.15, ply: 3)])
            .CubeDecision(XgPlayer.Player2, new XgCubeEquities(-0.3, -0.5, 1.0), doublerAction: CubeAction.NoDouble);
        return builder.Build();
    }

    [Fact]
    public void Iterate_WithoutOptions_BuildsEveryRowForTheDefaultRanking()
    {
        XgDecisionIterator.Iterate(Fixture(), "ranking.xg")
            .Should().OnlyContain(r => r.Ranking == PlayRanking.Equity);
    }

    /// <summary>
    /// Each row is exactly its record's projection for the options' ranking —
    /// the same row <see cref="DecisionRow.From"/> builds from the record the
    /// other surface yields, compared whole through the row's CSV and JSON.
    /// </summary>
    [Theory]
    [InlineData(PlayRanking.Equity)]
    [InlineData(PlayRanking.DepthFirst)]
    public void Iterate_BuildsEachRowFromItsRecord_ForTheOptionsRanking(PlayRanking ranking)
    {
        var file = Fixture();
        var options = new XgIteratorOptions(Ranking: ranking);

        var rows = XgDecisionIterator.Iterate(file, "ranking.xg", options: options).ToList();
        var fromRecords = XgDecisionIterator.IterateDiagramRequests(file, "ranking.xg", options: options)
            .Select(r => DecisionRow.From(r, ranking)).ToList();

        rows.Should().HaveCount(2).And.HaveSameCount(fromRecords);
        rows.Select(r => r.ToCsvLine()).Should().Equal(fromRecords.Select(r => r.ToCsvLine()));
        rows.Select(r => JsonSerializer.Serialize(r)).Should().Equal(fromRecords.Select(r => JsonSerializer.Serialize(r)));
        rows.Should().OnlyContain(r => r.Ranking == ranking);
    }

    /// <summary>
    /// The two rankings judge the played move differently, so the ranking the
    /// row states is the one it was built for: under equity the deep play
    /// gives up 0.05; under depth first it is the best, with no error.
    /// </summary>
    [Fact]
    public void Iterate_TheRankingDecidesThePlayersError()
    {
        var file = Fixture();

        var equity = XgDecisionIterator.Iterate(file, "ranking.xg").First(r => r.Kind == DecisionKind.CheckerPlay);
        var depthFirst = XgDecisionIterator.Iterate(file, "ranking.xg", options: new XgIteratorOptions(Ranking: PlayRanking.DepthFirst))
            .First(r => r.Kind == DecisionKind.CheckerPlay);

        equity.Result.Should().Be(PlayerResultKind.Scored);
        equity.Error.Should().BeApproximately(0.05, 1e-6);
        depthFirst.Result.Should().Be(PlayerResultKind.Scored);
        depthFirst.Error.Should().Be(0.0);
    }

    /// <summary>
    /// The post-yield callbacks judge under the same ranking the rows are built
    /// for, on the record surface too, where the record itself does not depend
    /// on it: each sees a view built for the options' ranking.
    /// </summary>
    [Theory]
    [InlineData(PlayRanking.Equity)]
    [InlineData(PlayRanking.DepthFirst)]
    public void Callbacks_SeeEachDecisionThroughAViewForTheOptionsRanking(PlayRanking ranking)
    {
        var file = Fixture();
        var options = new XgIteratorOptions(Ranking: ranking);
        var seenByRows = new List<IDecisionFilterData>();
        var seenByRecords = new List<IDecisionFilterData>();

        _ = XgDecisionIterator.Iterate(file, "ranking.xg",
            callbacks: new XgIteratorCallbacks(StopGameAfter: d => { seenByRows.Add(d); return false; }),
            options: options).ToList();
        _ = XgDecisionIterator.IterateDiagramRequests(file, "ranking.xg",
            callbacks: new XgIteratorCallbacks(StopGameAfter: d => { seenByRecords.Add(d); return false; }),
            options: options).ToList();

        seenByRows.Should().HaveCount(2).And.OnlyContain(d => d.Ranking == ranking);
        seenByRecords.Should().HaveCount(2).And.OnlyContain(d => d.Ranking == ranking);
        seenByRecords.Select(d => d.PlayerResult).Should().Equal(seenByRows.Select(d => d.PlayerResult));
    }

    /// <summary>
    /// An undefined ranking is refused when the options are made, by
    /// construction and by <c>with</c> alike — an invalid options object is
    /// unrepresentable, as every door BgDataTypes_Lib gives the ranking refuses
    /// one.
    /// </summary>
    [Fact]
    public void Options_RefuseAnUndefinedRanking()
    {
        var undefined = (PlayRanking)7;

        FluentActions.Invoking(() => new XgIteratorOptions(Ranking: undefined))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("Ranking");
        FluentActions.Invoking(() => new XgIteratorOptions() with { Ranking = undefined })
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("Ranking");
    }

    [Fact]
    public void Options_DefaultToTheEquityRanking()
    {
        new XgIteratorOptions().Ranking.Should().Be(PlayRanking.Equity);
        new XgIteratorOptions(OpeningBook: null).Ranking.Should().Be(PlayRanking.Equity);
    }
}
