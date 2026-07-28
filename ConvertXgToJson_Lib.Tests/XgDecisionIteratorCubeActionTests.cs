using BgDataTypes_Lib;
using ConvertXgToJson_Lib;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins the played cube action <see cref="XgDecisionIterator.IterateDiagramRequests"/>
/// stamps onto <see cref="DecisionData.UserDoublerAction"/> /
/// <see cref="DecisionData.UserTakerAction"/> from the raw
/// <c>CubeRecord.Doubled</c> / <c>Taken</c> pane state.
///
/// <para>
/// The named-fixture tests key off <c>match_41648777.xg</c>, which happens to
/// carry all four record shapes the mapping distinguishes: an undoubled cube
/// decision, a double that was passed, a double that was taken on a dead
/// equity tie, and a resignation pane where no cube action was played at all.
/// Corpus-wide assertions stay shape-level per the standing convention.
/// </para>
/// </summary>
[Collection("FileIO")]
public class XgDecisionIteratorCubeActionTests
{
    private const string FixtureName = "match_41648777.xg";

    /// <summary>
    /// Returns the cube decision at (<paramref name="game"/>,
    /// <paramref name="moveNumber"/>) from the named fixture. Cube decisions
    /// are stamped at <c>MoveNumber + 1</c>, matching the raw record's
    /// position in the game.
    /// </summary>
    private static DecisionData CubeDecisionAt(int game, int moveNumber)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, FixtureName);
        var decision = XgDecisionIterator
            .IterateDiagramRequests(XgFileReader.ReadFile(path), FixtureName)
            .SingleOrDefault(d =>
                d.Decision.IsCube &&
                d.Descriptive.Game == game &&
                d.Descriptive.MoveNumber == moveNumber);

        decision.Should().NotBeNull(
            $"{FixtureName} should yield an analysed cube decision at g{game}:m{moveNumber}");
        return decision!.Decision;
    }

    // -----------------------------------------------------------------------
    //  Named-fixture pins — one per record shape
    // -----------------------------------------------------------------------

    /// <summary>
    /// g5:m2 is the equity-tie repro the carrier was added for: the record
    /// was doubled and taken, XG scores the double at zero error, yet
    /// <c>NoDoubleEquity == DoubleTakeEquity</c> so no consumer can recover
    /// the played action from the error alone. Both halves must be stamped
    /// from the record, and the tie is asserted alongside them so a fixture
    /// change that dissolves it cannot silently defang this test.
    /// </summary>
    [Fact]
    public void EquityTiedDoubleAndTake_StampsBothHalvesFromTheRecord()
    {
        var decision = CubeDecisionAt(game: 5, moveNumber: 2);

        decision.NoDoubleEquity.Should().BeApproximately(decision.DoubleTakeEquity, 1e-6,
            "g5:m2 is the dead-equity-tie repro — the two cube equities are equal");
        decision.UserDoubleError.Should().Be(0.0,
            "XG scores the played double at zero error on the tie");

        decision.UserDoublerAction.Should().Be(CubeAction.Double);
        decision.UserTakerAction.Should().Be(CubeAction.Take);
    }

    /// <summary>
    /// g1:m11 was doubled and passed — the game footer records the drop.
    /// Pins the taker half's other value, so a mapping that collapsed every
    /// response to Take would fail.
    /// </summary>
    [Fact]
    public void DoubledAndPassed_StampsDoubleAndPass()
    {
        var decision = CubeDecisionAt(game: 1, moveNumber: 11);

        decision.UserDoublerAction.Should().Be(CubeAction.Double);
        decision.UserTakerAction.Should().Be(CubeAction.Pass);
    }

    /// <summary>
    /// g1:m2 is an ordinary undoubled cube decision — the player on roll
    /// declined and rolled. The doubler half records that; the taker half
    /// stays null because no taker decision ever existed, which is the shape
    /// the per-half carrier was chosen to express.
    /// </summary>
    [Fact]
    public void UndoubledCubeDecision_StampsNoDoubleAndNullTaker()
    {
        var decision = CubeDecisionAt(game: 1, moveNumber: 2);

        decision.UserDoublerAction.Should().Be(CubeAction.NoDouble);
        decision.UserTakerAction.Should().BeNull(
            "no double was offered, so no taker decision exists");
    }

    /// <summary>
    /// g4:m46 is the pane XG writes where a game ended by resignation with no
    /// cube action taken (<c>Doubled == -1</c>). The position is analysed —
    /// so the decision is yielded — but nothing was played, and both halves
    /// stay null rather than reporting a No Double the player never chose.
    /// </summary>
    [Fact]
    public void ResignationPane_StampsNeitherHalf()
    {
        var decision = CubeDecisionAt(game: 4, moveNumber: 46);

        decision.UserDoublerAction.Should().BeNull(
            "no cube action was played — the game ended by resignation");
        decision.UserTakerAction.Should().BeNull();
    }

    /// <summary>
    /// The same record also carries <c>ErrorCube</c>'s −1000 not-analysed
    /// sentinel. Pinned separately from the stamp above: the two agree here
    /// by coincidence, and this documents that the action mapping is keyed
    /// off the pane state rather than off the error's presence.
    /// </summary>
    [Fact]
    public void ResignationPane_CarriesTheNotAnalysedErrorSentinel()
    {
        var decision = CubeDecisionAt(game: 4, moveNumber: 46);

        decision.UserDoubleError.Should().BeNull();
        decision.UserTakeError.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    //  Corpus-wide shape assertions
    // -----------------------------------------------------------------------

    /// <summary>
    /// Across the whole .xg corpus: each half stays inside its own action
    /// domain, and the cross-half producer contract holds — a recorded taker
    /// response implies the doubler doubled. <c>DecisionData</c> guards the
    /// halves individually but leaves the cross-half rule to the producer,
    /// so this is the only place it is checked.
    /// </summary>
    [Fact]
    public void CubeActions_StayInTheirHalvesAndHonourTheCrossHalfContract()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            string sourceFile = Path.GetFileName(path);
            var cubes = XgDecisionIterator
                .IterateDiagramRequests(XgFileReader.ReadFile(path), sourceFile)
                .Where(d => d.Decision.IsCube);

            foreach (var d in cubes)
            {
                string where = $"{sourceFile} g{d.Descriptive.Game}:m{d.Descriptive.MoveNumber}";

                if (d.Decision.UserDoublerAction is { } doubler)
                    doubler.Should().BeOneOf([CubeAction.NoDouble, CubeAction.Double], where);

                if (d.Decision.UserTakerAction is { } taker)
                {
                    taker.Should().BeOneOf([CubeAction.Take, CubeAction.Pass], where);

                    d.Decision.UserDoublerAction.Should().Be(CubeAction.Double,
                        $"{where}: a recorded taker response implies the doubler doubled");
                }
            }
        }
    }

    /// <summary>
    /// The take error and the taker half answer the same underlying question
    /// — was a double offered — so a decision that scores a take response
    /// must also record a doubled action. Pins the single-sourced gate: the
    /// error and the stamp read the same predicate.
    /// </summary>
    [Fact]
    public void TakeError_ImpliesADoubledAction()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            string sourceFile = Path.GetFileName(path);
            var scored = XgDecisionIterator
                .IterateDiagramRequests(XgFileReader.ReadFile(path), sourceFile)
                .Where(d => d.Decision.IsCube && d.Decision.UserTakeError is not null);

            foreach (var d in scored)
                d.Decision.UserDoublerAction.Should().Be(CubeAction.Double,
                    $"{sourceFile} g{d.Descriptive.Game}:m{d.Descriptive.MoveNumber}");
        }
    }

    /// <summary>
    /// Both halves are cube-only, like every other cube field on
    /// <c>DecisionData</c>: a checker-play decision leaves them null.
    /// </summary>
    [Fact]
    public void PlayDecisions_CarryNeitherHalf()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            string sourceFile = Path.GetFileName(path);
            var plays = XgDecisionIterator
                .IterateDiagramRequests(XgFileReader.ReadFile(path), sourceFile)
                .Where(d => !d.Decision.IsCube);

            foreach (var d in plays)
            {
                d.Decision.UserDoublerAction.Should().BeNull(
                    $"{sourceFile} g{d.Descriptive.Game}:m{d.Descriptive.MoveNumber}");
                d.Decision.UserTakerAction.Should().BeNull(
                    $"{sourceFile} g{d.Descriptive.Game}:m{d.Descriptive.MoveNumber}");
            }
        }
    }
}
