using BgDataTypes_Lib;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Permanent real input reaching the fourth cube answer, read as Too good,
/// through the production path (halheinrich/backgammon#273,
/// halheinrich/backgammon#326; SPEC-scoring §3, "Reaching it" and the
/// four-answer amendments): the iterator reads a named fixture, builds the cube
/// record, and BgDataTypes_Lib's rule, applied to the numbers XG stored, gives
/// it the truth <see cref="CubeAnswer.NoDoublePass"/>, which the decision
/// reads as <see cref="CubeClaim.TooGood"/> because gammons are possible there
/// (<see cref="CubeDecision.GammonsPossible"/>, <see cref="CubeDecision.ClaimOf"/>)
/// — in a money session and in a match.
///
/// <para>
/// What this shows, and what it does not. It shows that our rule, applied to
/// XG's stored numbers, reaches the fourth answer on real input, and under
/// which of its labels. It is not agreement with an XG Too Good label: XG's
/// files carry no analysis text, and the cube choice fields XG does store are
/// undocumented, so none is read here. The rule and the reading are
/// BgDataTypes_Lib's, verified there by synthetic tests; the inputs are held
/// to XG's stored facts by <see cref="XgCorpusAgreementTests"/>.
/// </para>
/// <para>
/// The fixtures, chosen by the umbrella's probe of 2026-09-27. Measured
/// 2026-10-02 (UTC) over the local <c>.xg</c> fixtures: all 21 parsed, none
/// failed. Among them 133 cube records, across eleven of the files, have the
/// fourth answer as their truth; 129 of them read Too good and 4 No double /
/// Pass, and <c>MoneyTest.xg</c> game 1 move 16 is the only money one. The
/// match is <c>match35253054.xg</c> game 1 move 28 (no double +1.3660,
/// double/take +2.7966), a 5-point match fixture other tests already name. Measured at both: the cube is 1 and centred, the opponent
/// has nothing borne off, and gammons are possible (so the money session is
/// not under the Jacoby rule), and the answer reads Too good; this test pins
/// that reading.
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
    public void Fixture_CubeRecord_ReachesTheFourthAnswer_ReadAsTooGood(string fixture, int game, int moveNumber, Type session)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, fixture);
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on the halheinrich/backgammon#273 Too good fixtures in TestData/FixtureFiles/.");

        var id = XgDecisionIterator.BuildDecisionId(fixture, game, moveNumber, isCube: true);
        var records = XgDecisionIterator.IterateDiagramRequests(XgFileReader.ReadFile(path), fixture).ToList();

        var cube = records.OfType<CubeDecision>().Should().ContainSingle(c => c.Id == id,
            $"{id} is an analysed cube decision the production path builds").Subject;
        cube.Session.Should().BeOfType(session);
        cube.Decision.BestAnswer.Should().Be(CubeAnswer.NoDoublePass,
            $"the rule, applied to the equities XG stored for {id}, makes the fourth answer the truth");
        cube.GammonsPossible.Should().BeTrue(
            $"at {id} the opponent has nothing borne off, the cube is centred at 1, and neither the Jacoby rule nor the match score shuts gammons out, as measured");
        cube.ClaimOf(cube.Decision.BestAnswer).Should().Be(CubeClaim.TooGood,
            $"the fourth answer reads Too good where gammons are possible, as at {id}");
    }
}
