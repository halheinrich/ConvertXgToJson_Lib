using System.Text.RegularExpressions;
using BgDataTypes_Lib;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The row-path mirror of <see cref="XgDecisionIteratorJacobyStampTests"/>:
/// pins the Jacoby fact <see cref="XgDecisionIterator.Iterate"/> stamps onto
/// <see cref="DecisionRow.IsJacoby"/> at both construction sites (checker
/// plays and cube decisions), and the consequence that motivates it — what
/// <see cref="DecisionRow.MatchScore"/> renders.
///
/// <para>
/// Before the stamp every row the converter produced left <c>IsJacoby</c> at
/// its <see langword="null"/> default, so a money row rendered the
/// honest-unknown bare <c>money</c> even though the file states the rule
/// (halheinrich/backgammon#144). Downstream that lost the fact from CSV
/// exports and made <c>moneyJ</c> / <c>moneyNJ</c> unmatchable on the row
/// path, while the record path — stamped by halheinrich/backgammon#120 —
/// knew it all along.
/// </para>
///
/// <para>
/// Fixtures, all pinned by name out of <see cref="TestPaths.FixtureFilesDir"/>
/// and each carrying both decision kinds: <c>Make20Pt.xg</c> (money, Jacoby
/// on), <c>MoneyTest.xg</c> (money, Jacoby off), <c>MatchTest.xg</c> (match
/// play, where the question does not arise).
/// </para>
/// </summary>
[Collection("FileIO")]
public class XgDecisionIteratorRowJacobyStampTests
{
    private const string MoneyJacobyOnFixture = "Make20Pt.xg";
    private const string MoneyJacobyOffFixture = "MoneyTest.xg";
    private const string MatchFixture = "MatchTest.xg";

    /// <summary>
    /// Reads a named fixture through the real row path and returns every row it
    /// yields, asserting both decision kinds are present — the two
    /// <see cref="DecisionRow"/> construction sites are separate code, so a
    /// fixture carrying only one kind would leave one unproven.
    /// </summary>
    private static List<DecisionRow> ReadBothKinds(string fixtureName)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, fixtureName);
        var rows = XgDecisionIterator
            .Iterate(XgFileReader.ReadFile(path), fixtureName)
            .ToList();

        rows.Should().Contain(r => !r.IsCube,
            $"{fixtureName} must exercise the checker-play stamping site");
        rows.Should().Contain(r => r.IsCube,
            $"{fixtureName} must exercise the cube-decision stamping site");
        return rows;
    }

    // -----------------------------------------------------------------------
    //  The stamp
    // -----------------------------------------------------------------------

    [Fact]
    public void MoneyRow_JacobyOn_StampsTrue_OnBothDecisionKinds()
    {
        var rows = ReadBothKinds(MoneyJacobyOnFixture);

        rows.Should().OnlyContain(r => r.IsJacoby == true,
            "the file's match header has the Jacoby rule in force, and the row takes "
            + "the fact from the same match context the record path reads");
        rows.Should().OnlyContain(r => r.MatchLength == 0 && r.IsMoneyGame,
            "MatchLength — not IsJacoby — is what says this is a money session");
    }

    [Fact]
    public void MoneyRow_JacobyOff_StampsFalse_OnBothDecisionKinds()
    {
        var rows = ReadBothKinds(MoneyJacobyOffFixture);

        rows.Should().OnlyContain(r => r.IsJacoby == false,
            "Jacoby off is a fact the row carries, distinct from null's 'the producer "
            + "did not supply it'");
    }

    /// <summary>
    /// A match row poses no Jacoby question, so it asserts no answer — the same
    /// tri-state ruling leg 2 of halheinrich/backgammon#120 made for the record
    /// path. <see cref="DecisionRow.MatchScore"/> never reaches the money branch
    /// here, so the fact is invisible in the rendering either way; the
    /// <see langword="null"/> is about the wire's honesty.
    /// </summary>
    [Fact]
    public void MatchRow_CarriesNoJacobyFact_OnBothDecisionKinds()
    {
        var rows = ReadBothKinds(MatchFixture);

        rows.Should().OnlyContain(r => r.IsJacoby == null,
            "Jacoby is meaningless off money, so a match row answers nothing");
    }

    // -----------------------------------------------------------------------
    //  The point of the stamp — what MatchScore renders
    // -----------------------------------------------------------------------

    /// <summary>
    /// The regression this leg exists to close. A money row used to render the
    /// bare <c>money</c> token — neither of the two rule-bearing tokens, so
    /// unmatchable by a filter and lossy in a CSV export — even though the file
    /// stated the rule.
    /// </summary>
    [Theory]
    [InlineData(MoneyJacobyOnFixture, "moneyJ")]
    [InlineData(MoneyJacobyOffFixture, "moneyNJ")]
    public void MoneyRow_MatchScore_SpellsTheJacobyFact(string fixtureName, string expected)
    {
        var rows = ReadBothKinds(fixtureName);

        rows.Should().OnlyContain(r => r.MatchScore == expected,
            "a money row whose Jacoby fact is stamped renders the rule as a suffix on "
            + "the money token, never the honest-unknown bare 'money'");
        rows.Should().OnlyContain(r => r.ToCsvLine().Contains(expected, StringComparison.Ordinal),
            "MatchScore is a CSV column, so the export carries the fact too");
    }

    /// <summary>
    /// The match side is untouched: a match row still renders the away-scores
    /// pair, with Crawford — not Jacoby — as its only suffix.
    /// </summary>
    [Fact]
    public void MatchRow_MatchScore_IsTheAwayScorePair_WithNoMoneyToken()
    {
        var rows = ReadBothKinds(MatchFixture);

        rows.Should().OnlyContain(r => r.MatchLength > 0,
            "MatchTest.xg is match play");
        rows.Should().OnlyContain(
            r => Regex.IsMatch(r.MatchScore, @"^\d+a\d+aC?$"),
            "a match row's score is the away-scores pair, optionally Crawford-suffixed");
        rows.Should().OnlyContain(
            r => !r.MatchScore.StartsWith("money", StringComparison.Ordinal),
            "no match row renders a money token, whatever the header says about Jacoby");
    }
}
