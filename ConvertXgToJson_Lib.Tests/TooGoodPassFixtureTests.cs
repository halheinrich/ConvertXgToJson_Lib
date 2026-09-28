using BgDataTypes_Lib;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Permanent real input reaching the Too Good / Pass branch through the
/// production path (halheinrich/backgammon#273; SPEC-scoring §3, "Reaching
/// it"): the iterator reads a named fixture, builds the cube record, and
/// BgDataTypes_Lib's rule, applied to the numbers XG stored, derives
/// <see cref="CubeClaimPair.TooGoodPass"/> for it — in a money session and in
/// a match.
///
/// <para>
/// What this shows, and what it does not. It shows that our rule, applied to
/// XG's stored numbers, reaches that branch on real input. It is not
/// agreement with an XG Too Good label: XG's files carry no analysis text, and
/// the cube choice fields XG does store are undocumented, so none is read
/// here. The rule itself is BgDataTypes_Lib's, verified there by synthetic
/// tests; the inputs are held to XG's stored facts by
/// <see cref="XgCorpusAgreementTests"/>.
/// </para>
/// <para>
/// The fixtures, chosen by the umbrella's probe of 2026-09-27 and measured
/// again when this test was written (2026-09-28, UTC): 129 cube records across ten
/// <c>.xg</c> fixtures derive Too Good / Pass, and <c>MoneyTest.xg</c> game 1
/// move 16 is the only money one. The match is <c>match35253054.xg</c> game 1
/// move 28 (no double +1.3660, double/take +2.7966), a 5-point match fixture
/// other tests already name.
/// </para>
/// <para>
/// Local-only by the TestData rule: the fixtures live in the gitignored
/// <c>TestData/FixtureFiles/</c>, which is append-only, so naming them is
/// stable. The test carries the CI-excluded <c>RequiresFixtureFiles</c> trait,
/// and it fails when a named file is missing, so it never passes on an empty
/// folder.
/// </para>
/// </summary>
[Collection("FileIO")]
public class TooGoodPassFixtureTests
{
    [Theory]
    [Trait("Category", "RequiresFixtureFiles")]
    [InlineData("MoneyTest.xg", 1, 16, typeof(MoneySession))]
    [InlineData("match35253054.xg", 1, 28, typeof(MatchSession))]
    public void Fixture_CubeRecord_DerivesTooGoodPass(string fixture, int game, int moveNumber, Type session)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, fixture);
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on the halheinrich/backgammon#273 Too Good / Pass fixtures in TestData/FixtureFiles/.");

        var id = XgDecisionIterator.BuildDecisionId(fixture, game, moveNumber, isCube: true);
        var records = XgDecisionIterator.IterateDiagramRequests(XgFileReader.ReadFile(path), fixture).ToList();

        var cube = records.OfType<CubeDecision>().Should().ContainSingle(c => c.Id == id,
            $"{id} is an analysed cube decision the production path builds").Subject;
        cube.Session.Should().BeOfType(session);
        cube.Decision.BestClaimPair.Should().Be(CubeClaimPair.TooGoodPass,
            $"the rule, applied to the equities XG stored for {id}, reaches the Too Good / Pass branch");
    }
}
