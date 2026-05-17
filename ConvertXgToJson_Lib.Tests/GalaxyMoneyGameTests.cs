using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Backgammon Galaxy exports money games by abusing <c>MatchLength</c> as a
/// cube-size limit (a real, even value) and setting an illegal Crawford flag,
/// instead of XG's 99999 money sentinel — so without detection they parse as
/// rated matches. <see cref="SaveRecordParser"/> detects them and overwrites
/// <c>MatchLength</c> to 0; it also forces <c>IsMoneyMatch</c> true, because
/// the raw XG money byte reads false on these files.
/// </summary>
[Collection("FileIO")]
public class GalaxyMoneyGameTests
{
    // ------------------------------------------------------------------ //
    //  Detection truth table
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData("BackgammonGalaxy", 16, true)]    // the reproducing shape (match38254396)
    [InlineData("BackgammonGalaxy", 2, true)]     // smallest even cube-limit value
    [InlineData(" BackgammonGalaxy ", 16, true)]  // location compare is trimmed
    public void IsGalaxyMoneyGame_True_WhenGalaxyEvenAndCrawford(
        string location, int matchLength, bool crawford)
    {
        SaveRecordParser.IsGalaxyMoneyGame(location, matchLength, crawford)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("BackgammonGalaxy", 7, true)]     // odd length — a genuine Galaxy rated match
    [InlineData("BackgammonGalaxy", 16, false)]   // no Crawford flag
    [InlineData("Monaco", 16, true)]              // not a Galaxy file
    [InlineData("backgammongalaxy", 16, true)]    // location compare is ordinal (case-sensitive)
    [InlineData("", 16, true)]                    // empty location
    public void IsGalaxyMoneyGame_False_WhenAnyCriterionMissing(
        string location, int matchLength, bool crawford)
    {
        SaveRecordParser.IsGalaxyMoneyGame(location, matchLength, crawford)
            .Should().BeFalse();
    }

    // ------------------------------------------------------------------ //
    //  Parser effects — synthetic files
    // ------------------------------------------------------------------ //

    [Fact]
    public void Parser_GalaxyMoneyGame_MatchLengthOverwrittenToZero_IsMoneyMatchTrue()
    {
        var file = XgFileReader.ReadStream(
            XgFileBuilder.BuildMinimalXgFile(matchLength: 16, location: "BackgammonGalaxy"));
        var header = file.Records.OfType<MatchHeaderRecord>().First();

        header.MatchLength.Should().Be(0,
            "a Galaxy money game's match length is a cube-limit kludge, normalized to 0");
        header.IsMoneyMatch.Should().BeTrue(
            "a detected Galaxy money game forces IsMoneyMatch true even when the raw XG byte is false");
    }

    [Fact]
    public void Parser_RatedMatch_LeftUnchanged()
    {
        // A genuine 16-point rated match at a non-Galaxy site is not money.
        var file = XgFileReader.ReadStream(
            XgFileBuilder.BuildMinimalXgFile(matchLength: 16, location: "Monaco"));
        var header = file.Records.OfType<MatchHeaderRecord>().First();

        header.MatchLength.Should().Be(16, "a non-Galaxy match length is left untouched");
        header.IsMoneyMatch.Should().BeFalse("a rated match is not flagged as money");
    }

    [Fact]
    public void Parser_GalaxyOddLength_NotTreatedAsMoneyGame()
    {
        // Galaxy location but an odd length: a genuine Galaxy rated match.
        var file = XgFileReader.ReadStream(
            XgFileBuilder.BuildMinimalXgFile(matchLength: 7, location: "BackgammonGalaxy"));
        var header = file.Records.OfType<MatchHeaderRecord>().First();

        header.MatchLength.Should().Be(7,
            "an odd length at Galaxy is a real rated match, not a money game");
        header.IsMoneyMatch.Should().BeFalse();
    }

    [Fact]
    public void Parser_SentinelMoneyGame_MatchLengthLeftAsSentinel()
    {
        // XG's native 99999 money sentinel is not Galaxy's kludge; the parser
        // leaves it raw on the record (consumers normalize the sentinel).
        var file = XgFileReader.ReadStream(
            XgFileBuilder.BuildMinimalXgFile(matchLength: 99999, location: "Monaco"));
        var header = file.Records.OfType<MatchHeaderRecord>().First();

        header.MatchLength.Should().Be(99999, "the 99999 sentinel is left raw on the record");
    }

    // ------------------------------------------------------------------ //
    //  Real fixture — the reported reproducing case
    // ------------------------------------------------------------------ //

    /// <summary>
    /// <c>match38254396.xg</c> is the reported Backgammon Galaxy money game,
    /// exported with <c>MatchLength = 16</c>. Pins that the full parse and the
    /// <see cref="XgFileReader.ReadMatchInfo"/> fast path both surface a
    /// normalized match length of 0.
    /// </summary>
    [Fact]
    public void RealFixture_Match38254396_IsDetectedAsGalaxyMoneyGame()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "match38254396.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected Galaxy money-game fixture not present: {path}. " +
                "This test depends on match38254396.xg being in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        var header = file.Records.OfType<MatchHeaderRecord>().First();

        header.MatchLength.Should().Be(0,
            "match38254396 is a Backgammon Galaxy money game; its kludged match length normalizes to 0");
        header.IsMoneyMatch.Should().BeTrue(
            "a detected Galaxy money game forces IsMoneyMatch true");

        XgFileReader.ReadMatchInfo(path)!.MatchLength.Should().Be(0,
            "the ReadMatchInfo fast path must agree with the full parse");
    }
}
