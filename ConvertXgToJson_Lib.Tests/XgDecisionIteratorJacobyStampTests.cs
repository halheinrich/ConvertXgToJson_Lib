using BgDataTypes_Lib;
using ConvertXgToJson_Lib;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins the Jacoby fact <see cref="XgDecisionIterator.IterateDiagramRequests"/>
/// stamps onto <see cref="PositionData.IsJacoby"/> at both construction sites
/// (checker plays and cube decisions), and the consequence that motivates it:
/// a money record read from a real file now derives a
/// <see cref="ProblemKey"/>.
///
/// <para>
/// Before the stamp every money record left <c>IsJacoby</c> at its
/// <see langword="null"/> default, which is <see cref="ProblemKey"/>'s
/// no-key rung — so <see cref="ProblemKey.TryDerive"/> failed on every
/// money decision this converter produced, silently: dedupe passed the item
/// through unmerged and stats never recorded it. The
/// <c>DerivesAProblemKey</c> tests below are the ones that would have failed
/// before the stamp; the fixtures are unchanged.
/// </para>
///
/// <para>
/// Fixtures, all pinned by name out of <see cref="TestPaths.FixtureFilesDir"/>
/// and each carrying both decision kinds: <c>Make20Pt.xg</c> (money, Jacoby
/// on), <c>MoneyTest.xg</c> (money, Jacoby off), <c>MatchTest.xg</c>
/// (5-point match).
/// </para>
/// </summary>
[Collection("FileIO")]
public class XgDecisionIteratorJacobyStampTests
{
    private const string MoneyJacobyOnFixture = "Make20Pt.xg";
    private const string MoneyJacobyOffFixture = "MoneyTest.xg";
    private const string MatchFixture = "MatchTest.xg";

    /// <summary>
    /// Reads a named fixture through the real converter path and returns
    /// every decision it yields, asserting that both kinds are present —
    /// the two <see cref="PositionData"/> construction sites are separate
    /// code, so a fixture carrying only one kind would leave one unproven.
    /// </summary>
    private static List<BgDecisionData> ReadBothKinds(string fixtureName)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, fixtureName);
        var decisions = XgDecisionIterator
            .IterateDiagramRequests(XgFileReader.ReadFile(path), fixtureName)
            .ToList();

        decisions.Should().Contain(d => !d.Decision.IsCube,
            $"{fixtureName} must exercise the checker-play stamping site");
        decisions.Should().Contain(d => d.Decision.IsCube,
            $"{fixtureName} must exercise the cube-decision stamping site");
        return decisions;
    }

    // -----------------------------------------------------------------------
    //  The stamp
    // -----------------------------------------------------------------------

    [Fact]
    public void MoneyRecord_JacobyOn_StampsTrue_OnBothDecisionKinds()
    {
        var decisions = ReadBothKinds(MoneyJacobyOnFixture);

        decisions.Should().OnlyContain(d => d.Position.IsJacoby == true,
            "the file's match header has the Jacoby rule in force, and the fact " +
            "is stamped from the match context rather than left unknown");
        decisions.Should().OnlyContain(
            d => d.Position.OnRollNeeds == 0 && d.Position.OpponentNeeds == 0,
            "the away-scores pair — not IsJacoby — is what says this is a money game");
    }

    [Fact]
    public void MoneyRecord_JacobyOff_StampsFalse_OnBothDecisionKinds()
    {
        var decisions = ReadBothKinds(MoneyJacobyOffFixture);

        decisions.Should().OnlyContain(d => d.Position.IsJacoby == false,
            "Jacoby off is a fact the record carries, distinct from null's " +
            "'the producer did not supply it'");
    }

    /// <summary>
    /// A match record poses no Jacoby question, so it asserts no answer.
    /// <see langword="null"/> here is the ruled wire form (leg 2 of
    /// halheinrich/backgammon#120): consumers ignore the member on match
    /// records either way, so the choice is about the wire's honesty, and a
    /// stamped <c>false</c> would claim the rule was not in force in a game
    /// it cannot apply to.
    /// </summary>
    [Fact]
    public void MatchRecord_CarriesNoJacobyFact_OnBothDecisionKinds()
    {
        var decisions = ReadBothKinds(MatchFixture);

        decisions.Should().OnlyContain(d => d.Position.IsJacoby == null,
            "Jacoby is meaningless off money, so a match record answers nothing");
    }

    // -----------------------------------------------------------------------
    //  The point of the stamp — money records key again
    // -----------------------------------------------------------------------

    /// <summary>
    /// The regression this leg exists to close. Every money decision the
    /// converter produced used to fall on the no-key rung; now each derives,
    /// and the money key spells the fact in its score field.
    /// </summary>
    [Theory]
    [InlineData(MoneyJacobyOnFixture, "/0a0j/")]
    [InlineData(MoneyJacobyOffFixture, "/0a0nj/")]
    public void MoneyRecord_DerivesAProblemKey_SpellingTheJacobyFact(
        string fixtureName, string expectedScoreField)
    {
        foreach (var decision in ReadBothKinds(fixtureName))
        {
            ProblemKey.TryDerive(decision, out var key).Should().BeTrue(
                "a money record whose Jacoby fact is stamped is no longer on " +
                "ProblemKey's no-key rung ({0})", decision.Id);
            key!.ToString().Should().Contain(expectedScoreField,
                "the money key's score field carries the Jacoby token");
            key.IsCubeDecision.Should().Be(decision.Decision.IsCube);
        }
    }

    /// <summary>
    /// The match side is untouched by the amendment: match keys stay
    /// byte-identical, carrying no Jacoby token at all.
    /// </summary>
    [Fact]
    public void MatchRecord_StillDerivesAProblemKey_WithNoJacobyToken()
    {
        foreach (var decision in ReadBothKinds(MatchFixture))
        {
            ProblemKey.TryDerive(decision, out var key).Should().BeTrue(
                "a match record derives as it always did ({0})", decision.Id);
            key!.ToString().Should().NotContain("0a0",
                "a match key's score field is the away-scores pair, never money");
            key.ToString().Should().NotContain("nj");
        }
    }
}
