using BgDataTypes_Lib;
using ConvertXgToJson_Lib;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins the session <see cref="XgDecisionIterator.IterateDiagramRequests"/>
/// builds for both decision kinds from the match header's terms — a money
/// session's Jacoby rule above all — and the consequence that motivates it: a
/// money record read from a real file keys (<see cref="ProblemKey.From"/>),
/// its key spelling the rule.
///
/// <para>
/// Money versus match is the session's kind (halheinrich/backgammon#273):
/// a money record states its rules in its <see cref="MoneyTerms"/>, so there
/// is no unknown rule for the key to fall on, and a match record states no
/// Jacoby fact at all. Before halheinrich/backgammon#120 every money record
/// left the rule unknown and fell on the key's no-key rung, silently; the
/// session kinds removed that rung with the state it handled.
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
    /// every decision it yields, asserting that both kinds are present — the
    /// two record kinds are built by separate code, so a fixture carrying only
    /// one kind would leave one unproven.
    /// </summary>
    private static List<BgDecisionData> ReadBothKinds(string fixtureName)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, fixtureName);
        var decisions = XgDecisionIterator
            .IterateDiagramRequests(XgFileReader.ReadFile(path), fixtureName)
            .ToList();

        decisions.Should().Contain(d => d.Kind == DecisionKind.CheckerPlay,
            $"{fixtureName} must exercise the checker-play builder");
        decisions.Should().Contain(d => d.Kind == DecisionKind.Cube,
            $"{fixtureName} must exercise the cube-decision builder");
        return decisions;
    }

    // -----------------------------------------------------------------------
    //  The session
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(MoneyJacobyOnFixture, true)]
    [InlineData(MoneyJacobyOffFixture, false)]
    public void MoneyRecord_StatesTheHeadersJacobyRule_OnBothDecisionKinds(string fixtureName, bool jacoby)
    {
        var decisions = ReadBothKinds(fixtureName);

        decisions.Should().OnlyContain(d => d.Session is MoneySession,
            "XG's money-sentinel length is read as money terms");
        decisions.Should().OnlyContain(d => ((MoneySession)d.Session).Terms.IsJacoby == jacoby,
            "the rule is the match header's, carried on every decision's money terms");
    }

    /// <summary>
    /// A match record poses no Jacoby question, so it states no answer: its
    /// session is a match, whose terms are its length alone.
    /// </summary>
    [Fact]
    public void MatchRecord_IsAMatchSession_OnBothDecisionKinds()
    {
        var decisions = ReadBothKinds(MatchFixture);

        decisions.Should().OnlyContain(d => d.Session is MatchSession
            && ((MatchSession)d.Session).Terms.Length == 5);
    }

    // -----------------------------------------------------------------------
    //  The point of the rule — money records key, spelling it
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every money decision keys, and the money key spells the rule in its
    /// score field.
    /// </summary>
    [Theory]
    [InlineData(MoneyJacobyOnFixture, "/0a0j/")]
    [InlineData(MoneyJacobyOffFixture, "/0a0nj/")]
    public void MoneyRecord_KeysSpellingTheJacobyRule(string fixtureName, string expectedScoreField)
    {
        foreach (var decision in ReadBothKinds(fixtureName))
        {
            var key = ProblemKey.From(decision);
            key.ToString().Should().Contain(expectedScoreField,
                "the money key's score field carries the Jacoby token");
            key.IsCubeDecision.Should().Be(decision.Kind == DecisionKind.Cube);
        }
    }

    /// <summary>
    /// Match keys carry no Jacoby token at all: their score field is the
    /// away-scores pair.
    /// </summary>
    [Fact]
    public void MatchRecord_KeysWithNoJacobyToken()
    {
        foreach (var decision in ReadBothKinds(MatchFixture))
        {
            var key = ProblemKey.From(decision).ToString();
            key.Should().NotContain("0a0", "a match key's score field is the away-scores pair, never money");
            key.Should().NotContain("nj");
        }
    }
}
