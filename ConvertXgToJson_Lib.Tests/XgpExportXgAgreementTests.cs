using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Ground-truth agreement tests: XG itself saved a position from a match we
/// also hold as a source <c>.xg</c>, so XG's own <c>.xgp</c> bytes are the
/// oracle for what a correct export of that decision looks like. The
/// round-trip suite proves self-consistency (writer agrees with reader);
/// this suite proves agreement with XG.
///
/// <para>
/// Fixture pairs (FixtureFiles is append-only, so pinning names is safe):
/// <c>MTCH4064_1_22.xgp</c> ← game 1, move 22 of <c>MTCH4064.xg</c>;
/// <c>match35253054_2_37.xgp</c> ← game 2, move 37 of
/// <c>match35253054.xg</c> (a Galaxy-sourced match saved by XG with player 2
/// on roll — exercising XG's preserved-perspective form).
/// </para>
///
/// <para>
/// Comparison altitude: <b>semantic field equality under perspective
/// normalization</b>, never byte identity. XG's save is a verbatim slice of
/// the source match — original player order, <c>ActivePlayer = ±1</c>, real
/// game number, carried analysis, source metadata (event, date, elo,
/// location) — while our export normalizes on-roll → player 1, writes clean
/// unanalyzed panes, and self-identifies in Location. What must agree is
/// everything the position/decision itself determines: board (on-roll
/// relative), dice, cube size + owner (on-roll relative), scores (on-roll
/// relative), match length, and the rule flags.
/// </para>
/// </summary>
[Collection("FileIO")]
public class XgpExportXgAgreementTests
{
    public static TheoryData<string, string, int, int> FixturePairs => new()
    {
        { "MTCH4064_1_22.xgp", "MTCH4064.xg", 1, 22 },
        { "match35253054_2_37.xgp", "match35253054.xg", 2, 37 },
    };

    private static string Fixture(string name) => Path.Combine(TestPaths.FixtureFilesDir, name);

    // ------------------------------------------------------------------ //
    //  Reader-side agreement: XG's saved position and the source decision
    //  normalize to the same BgDecisionData semantics through our iterator.
    //  (Also validates the fixture pairing itself.)
    // ------------------------------------------------------------------ //

    [Theory]
    [MemberData(nameof(FixturePairs))]
    public void XgSavedPosition_MatchesSourceDecision_ThroughOurReader(
        string xgpName, string xgName, int game, int moveNumber)
    {
        var truth = XgDecisionIterator
            .IterateDiagramRequests(XgFileReader.ReadFile(Fixture(xgpName)), xgpName)
            .Single();
        var source = FindSourceDecision(xgName, game, moveNumber, truth.IsCube);

        truth.Xgid.Should().Be(source.Xgid, "the XGID digests position, cube, dice, and match state");
        truth.Position.Mop.Should().Equal(source.Position.Mop);
        truth.Decision.Dice.Should().Equal(source.Decision.Dice);
        truth.Position.CubeSize.Should().Be(source.Position.CubeSize);
        truth.Position.CubeOwner.Should().Be(source.Position.CubeOwner);
        truth.Position.OnRollNeeds.Should().Be(source.Position.OnRollNeeds);
        truth.Position.OpponentNeeds.Should().Be(source.Position.OpponentNeeds);
        truth.Descriptive.MatchLength.Should().Be(source.Descriptive.MatchLength);
        truth.Descriptive.OnRollName.Should().Be(source.Descriptive.OnRollName);
        truth.Descriptive.OpponentName.Should().Be(source.Descriptive.OpponentName);
    }

    // ------------------------------------------------------------------ //
    //  Writer-side agreement: exporting the source decision produces
    //  records that agree, field-for-field, with what XG itself wrote.
    // ------------------------------------------------------------------ //

    [Theory]
    [MemberData(nameof(FixturePairs))]
    public void ExportOfSourceDecision_AgreesWithXgsOwnSave(
        string xgpName, string xgName, int game, int moveNumber)
    {
        var xgSaved = XgFileReader.ReadFile(Fixture(xgpName));
        bool isCube = XgDecisionIterator
            .IterateDiagramRequests(xgSaved, xgpName)
            .Single().IsCube;
        var source = FindSourceDecision(xgName, game, moveNumber, isCube);

        using var ms = new MemoryStream(XgpExporter.ToBytes(source));
        var exported = XgFileReader.ReadStream(ms);

        var xgMh = xgSaved.Records.OfType<MatchHeaderRecord>().Single();
        var xgGh = xgSaved.Records.OfType<GameHeaderRecord>().Single();
        var xgCube = xgSaved.Records.OfType<CubeRecord>().Single();
        var ourMh = exported.Records.OfType<MatchHeaderRecord>().Single();
        var ourGh = exported.Records.OfType<GameHeaderRecord>().Single();
        var ourCube = exported.Records.OfType<CubeRecord>().Single();

        // XG preserves the source match's perspective; ours is normalized to
        // on-roll = player 1. Compare through the on-roll lens.
        int active = xgCube.ActivePlayer >= 0 ? 1 : -1;

        // Match header — the rule/state fields the position determines.
        ourMh.MatchLength.Should().Be(xgMh.MatchLength);
        ourMh.Crawford.Should().Be(xgMh.Crawford);
        ourMh.Jacoby.Should().Be(xgMh.Jacoby);
        ourMh.Beaver.Should().Be(xgMh.Beaver);
        ourMh.Version.Should().Be(xgMh.Version);
        ourMh.Magic.Should().Be(xgMh.Magic);
        ourMh.Invert.Should().Be(xgMh.Invert);
        ourMh.CubeLimit.Should().Be(xgMh.CubeLimit);
        ourMh.Player1.Should().Be(active >= 0 ? xgMh.Player1 : xgMh.Player2,
            "our player 1 is the on-roll player");
        ourMh.Player2.Should().Be(active >= 0 ? xgMh.Player2 : xgMh.Player1);

        // Game header — scores through the on-roll lens; rule flag as-is.
        ourGh.Score1.Should().Be(active >= 0 ? xgGh.Score1 : xgGh.Score2);
        ourGh.Score2.Should().Be(active >= 0 ? xgGh.Score2 : xgGh.Score1);
        ourGh.CrawfordApplies.Should().Be(xgGh.CrawfordApplies);

        // Cube record — board, cube encoding, and (for plays) the roll.
        ourCube.ActivePlayer.Should().Be(1);
        NormalizedBoard(ourCube.Position, ourCube.ActivePlayer)
            .Should().Equal(NormalizedBoard(xgCube.Position, active));
        (ourCube.CubeValue * ourCube.ActivePlayer).Should().Be(xgCube.CubeValue * active,
            "signed-log2 cube ownership must agree relative to the on-roll player");
        if (!isCube)
            ourCube.DiceRolled.Should().Be(xgCube.DiceRolled);

        // Move record (play decisions): dice and starting board.
        if (!isCube)
        {
            var xgMove = xgSaved.Records.OfType<MoveRecord>().Single();
            var ourMove = exported.Records.OfType<MoveRecord>().Single();
            ourMove.Dice.Should().Equal(xgMove.Dice);
            NormalizedBoard(ourMove.InitialPosition, ourMove.ActivePlayer)
                .Should().Equal(NormalizedBoard(xgMove.InitialPosition, active));
        }
    }

    // ------------------------------------------------------------------ //
    //  Slice-export agreement: a slice of the source decision reproduces
    //  XG's own save-from-match records — analysis panes included — with
    //  one documented exception: the tutor family (ErrorTutorCube/Take/
    //  Move, TutorPosition). XG initializes those at runtime when a match
    //  is open (-1000 sentinels, a computed tutor board), while imported
    //  source files carry zeros there; the slice stays source-verbatim,
    //  which XG demonstrably accepts (it accepted the source .xg).
    // ------------------------------------------------------------------ //

    [Fact]
    public void SlicedPlayDecision_ReproducesXgsOwnSave()
    {
        var source = XgFileReader.ReadFile(Fixture("MTCH4064.xg"));
        var xgSaved = XgFileReader.ReadFile(Fixture("MTCH4064_1_22.xgp"));

        using var ms = new MemoryStream(XgpExporter.ToBytes(source, game: 1, moveNumber: 22, isCube: false));
        var sliced = XgFileReader.ReadStream(ms);

        sliced.Records.Select(r => r.EntryType).Should().Equal(xgSaved.Records.Select(r => r.EntryType));
        sliced.Header.SaveName.Should().Be(xgSaved.Header.SaveName);
        sliced.Header.GameId.Should().Be(xgSaved.Header.GameId);

        sliced.Records.OfType<MatchHeaderRecord>().Single().Should().BeEquivalentTo(
            xgSaved.Records.OfType<MatchHeaderRecord>().Single(),
            "XG's save copies the source match header verbatim, and so does the slice");
        sliced.Records.OfType<GameHeaderRecord>().Single().Should().BeEquivalentTo(
            xgSaved.Records.OfType<GameHeaderRecord>().Single());
        sliced.Records.OfType<CubeRecord>().Single().Should().BeEquivalentTo(
            xgSaved.Records.OfType<CubeRecord>().Single(),
            o => o.Excluding(c => c.ErrorTutorCube).Excluding(c => c.ErrorTutorTake));
        sliced.Records.OfType<MoveRecord>().Single().Should().BeEquivalentTo(
            xgSaved.Records.OfType<MoveRecord>().Single(),
            o => o.Excluding(m => m.TutorPosition).Excluding(m => m.ErrorTutorMove));
    }

    [Fact]
    public void SlicedRolledOutCubeDecision_ReproducesXgsOwnSave_IncludingRolloutPair()
    {
        var source = XgFileReader.ReadFile(Fixture("match35253054.xg"));
        var xgSaved = XgFileReader.ReadFile(Fixture("match35253054_2_37.xgp"));

        using var ms = new MemoryStream(XgpExporter.ToBytes(source, game: 2, moveNumber: 37, isCube: true));
        var sliced = XgFileReader.ReadStream(ms);

        sliced.Records.Select(r => r.EntryType).Should().Equal(xgSaved.Records.Select(r => r.EntryType));
        sliced.Header.SaveName.Should().Be(xgSaved.Header.SaveName);

        sliced.Records.OfType<MatchHeaderRecord>().Single().Should().BeEquivalentTo(
            xgSaved.Records.OfType<MatchHeaderRecord>().Single());
        sliced.Records.OfType<GameHeaderRecord>().Single().Should().BeEquivalentTo(
            xgSaved.Records.OfType<GameHeaderRecord>().Single());
        // RolloutIndex is deliberately inside this comparison: XG carries
        // the adjacent context pair and points at the second leg, and the
        // slice must land on the same index.
        sliced.Records.OfType<CubeRecord>().Single().Should().BeEquivalentTo(
            xgSaved.Records.OfType<CubeRecord>().Single(),
            o => o.Excluding(c => c.ErrorTutorCube).Excluding(c => c.ErrorTutorTake));
        sliced.Rollouts.Should().BeEquivalentTo(xgSaved.Rollouts, o => o.WithStrictOrdering(),
            "both legs of the cube's rollout pair travel with the slice");
    }

    // ------------------------------------------------------------------ //
    //  Helpers
    // ------------------------------------------------------------------ //

    private static BgDecisionData FindSourceDecision(
        string xgName, int game, int moveNumber, bool isCube)
    {
        var file = XgFileReader.ReadFile(Fixture(xgName));
        return XgDecisionIterator
            .IterateDiagramRequests(file, xgName)
            .Single(d => d.Descriptive.Game == game
                      && d.Descriptive.MoveNumber == moveNumber
                      && d.IsCube == isCube);
    }

    /// <summary>Record position normalized to the on-roll player's perspective.</summary>
    private static sbyte[] NormalizedBoard(PositionEngine position, int activePlayer) =>
        activePlayer >= 0 ? position.Points : BackgammonConstants.Flip(position.Points);
}
