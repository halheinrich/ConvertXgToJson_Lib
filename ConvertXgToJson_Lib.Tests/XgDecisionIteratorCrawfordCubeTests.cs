using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The Crawford emission rule (halheinrich/backgammon#201): a cube pane in
/// the Crawford game is not a decision, and neither surface emits one. XG
/// writes and analyses such panes — the rule is the game's, not the
/// format's — so the converter drops them at the one dispatch site both
/// surfaces share (<c>XgDecisionIterator.AdmitsCubeDecision</c>), before
/// either builder runs; the wire types would refuse the record otherwise
/// (BgDataTypes_Lib's <c>CrawfordRule</c>). The synthetic pair below is the
/// gating coverage; the fixture the issue measured is pinned local-only.
/// </summary>
[Collection("FileIO")]
public class XgDecisionIteratorCrawfordCubeTests
{
    private const string Xg = "synthetic.xg";
    private static readonly DiceRoll ThreeOne = new(3, 1);
    private static readonly Play MakeFivePoint = Play.Create(new Move(8, 5), new Move(6, 5));
    private static readonly XgCubeEquities Equities = new(0.30, 0.45, 1.0);

    /// <summary>
    /// A 5-point match at 4–2 — the one score shape the builder admits as
    /// Crawford — holding an analysed cube pane and then a play, with the
    /// flag as given. The same game either way, so the flag is the only
    /// difference between the gate and its control.
    /// </summary>
    private static XgFile CubeThenPlay(bool isCrawford)
    {
        var builder = XgFileBuilder.ForMatch(5, "Alice", "Bob");
        builder.AddGame(score1: 4, score2: 2, isCrawford: isCrawford)
            .CubeDecision(XgPlayer.Player1, Equities, ply: 3)
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        return builder.Build();
    }

    [Fact]
    public void Iterate_CrawfordGame_EmitsThePlayAndNoCube()
    {
        var rows = XgDecisionIterator.Iterate(CubeThenPlay(isCrawford: true), Xg).ToList();

        var row = rows.Should().ContainSingle().Subject;
        row.IsCube.Should().BeFalse();
        row.IsCrawford.Should().BeTrue();
        row.MoveNumber.Should().Be(1,
            "dropping the cube pane does not renumber the play that follows it");
    }

    [Fact]
    public void IterateDiagramRequests_CrawfordGame_EmitsThePlayAndNoCube()
    {
        var requests = XgDecisionIterator
            .IterateDiagramRequests(CubeThenPlay(isCrawford: true), Xg)
            .ToList();

        var request = requests.Should().ContainSingle().Subject;
        request.Decision.IsCube.Should().BeFalse();
        request.Position.IsCrawford.Should().BeTrue();
        request.Descriptive.MoveNumber.Should().Be(1);
    }

    /// <summary>
    /// The control: the same game without the flag emits its cube on both
    /// surfaces, numbered as it always was — the cube and the play that
    /// follows it share move number 1, told apart by <c>IsCube</c>.
    /// </summary>
    [Fact]
    public void BothSurfaces_NonCrawfordGame_EmitTheCube()
    {
        var file = CubeThenPlay(isCrawford: false);

        var rows = XgDecisionIterator.Iterate(file, Xg).ToList();
        var requests = XgDecisionIterator.IterateDiagramRequests(file, Xg).ToList();

        rows.Select(r => (r.IsCube, r.MoveNumber))
            .Should().Equal((true, 1), (false, 1));
        requests.Select(r => (r.Decision.IsCube, r.Descriptive.MoveNumber))
            .Should().Equal((true, 1), (false, 1));
        rows.Should().OnlyContain(r => !r.IsCrawford);
    }

    /// <summary>
    /// The stop callbacks see only emitted rows: a callback that would stop
    /// the game on any cube never fires in a Crawford game, so the play
    /// after the dropped pane is still reached.
    /// </summary>
    [Fact]
    public void Iterate_CrawfordGame_StopCallbacksNeverSeeTheDroppedCube()
    {
        var seen = new List<IDecisionFilterData>();
        var callbacks = new XgIteratorCallbacks(
            StopGameAfter: row => { seen.Add(row); return row.IsCube; });

        var rows = XgDecisionIterator
            .Iterate(CubeThenPlay(isCrawford: true), Xg, callbacks: callbacks)
            .ToList();

        rows.Should().ContainSingle().Which.IsCube.Should().BeFalse();
        seen.Should().Equal(rows);
    }

    /// <summary>
    /// The fixture the issue measured (2026-09-11): game 3 of the match is
    /// the Crawford game, and its one analysed cube pane — move 41, the
    /// decision the quiz served — is the only cube among the file's 38
    /// Crawford decisions. The other 37 are plays and still emit; the pane
    /// does not, on either surface.
    ///
    /// <para>
    /// Local-only by the TestData rule: the fixture lives in the gitignored
    /// <c>TestData/FixtureFiles/</c>, so this test carries the CI-excluded
    /// <c>RequiresFixtureFiles</c> trait and nothing gating depends on it.
    /// The gating coverage is the synthetic pair above.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixtureFiles")]
    public void Fixture44741679_CrawfordGame_EmitsItsPlaysAndNotItsCubePane()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "hairpaint007_linalaks_23082026_44741679.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on the halheinrich/backgammon#201 fixture in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        string source = Path.GetFileName(path);
        var rows = XgDecisionIterator.Iterate(file, source).ToList();
        var requests = XgDecisionIterator.IterateDiagramRequests(file, source).ToList();

        var crawfordRows = rows.Where(r => r.IsCrawford).ToList();
        crawfordRows.Should().HaveCount(37,
            "the issue measured 38 Crawford decisions, exactly one of them the cube pane");
        crawfordRows.Should().OnlyContain(r => !r.IsCube && r.Game == 3);
        rows.Should().NotContain(r => r.Game == 3 && r.MoveNumber == 41 && r.IsCube,
            "g3:m41:cube is the decision the quiz served");

        requests.Where(r => r.Position.IsCrawford).Should().HaveCount(37);
        requests.Should().OnlyContain(r => !(r.Position.IsCrawford && r.Decision.IsCube));
        requests.Count.Should().Be(rows.Count, "both surfaces drop the same pane");
    }
}
