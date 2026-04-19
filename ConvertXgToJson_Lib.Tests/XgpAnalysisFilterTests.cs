using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests that <see cref="XgDecisionIterator.Iterate"/> and
/// <see cref="XgDecisionIterator.IterateDiagramRequests"/> skip unanalysed
/// decisions in <c>.xgp</c> files entirely (the "skip" semantic — unanalysed
/// rows do not appear at all).
///
/// Fixtures live under <c>../TestData/FixtureFiles/</c>:
///   <list type="bullet">
///   <item><c>NoAnalysis.xgp</c> — cube only, never analysed
///         (<c>Level=-100, LevelRequest=0</c>).</item>
///   <item><c>DoubleAnalysis.xgp</c>, <c>TakeAnalysis.xgp</c>,
///         <c>BothAnalysis.xgp</c> — cube only, fully analysed at level 1002.</item>
///   <item><c>PlayAnalysis.xgp</c> — analysed move + unanalysed cube (the cube
///         portion of an .xgp is always emitted by XG even when it has no
///         meaningful analysis).</item>
///   <item><c>Opening 32 65 64 31 65.xgp</c> — analysed move + cube whose
///         analysis was <em>requested</em> (<c>LevelRequest=1002</c>) but
///         <em>never ran</em> (<c>Level=-100</c>). Real-world reproducer for
///         the phantom-cube-slide bug.</item>
///   </list>
/// </summary>
[Collection("FileIO")]
public class XgpAnalysisFilterTests
{
    private static string FixtureDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            @"..\..\..\..\..\TestData\FixtureFiles"));

    private static string Fixture(string name) => Path.Combine(FixtureDir, name);

    // -----------------------------------------------------------------------
    //  Per-fixture row counts — the "skip" semantic
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("NoAnalysis.xgp",                 0)]
    [InlineData("DoubleAnalysis.xgp",             1)]
    [InlineData("TakeAnalysis.xgp",               1)]
    [InlineData("PlayAnalysis.xgp",               1)]
    [InlineData("BothAnalysis.xgp",               1)]
    [InlineData("Opening 32 65 64 31 65.xgp",     1)]
    public void Iterate_YieldsOnlyAnalysedDecisions(string fileName, int expected)
    {
        var file = XgFileReader.ReadFile(Fixture(fileName));
        XgDecisionIterator.Iterate(file, fileName).Count().Should().Be(expected);
    }

    [Theory]
    [InlineData("NoAnalysis.xgp",                 0)]
    [InlineData("DoubleAnalysis.xgp",             1)]
    [InlineData("TakeAnalysis.xgp",               1)]
    [InlineData("PlayAnalysis.xgp",               1)]
    [InlineData("BothAnalysis.xgp",               1)]
    [InlineData("Opening 32 65 64 31 65.xgp",     1)]
    public void IterateDiagramRequests_YieldsOnlyAnalysedDecisions(string fileName, int expected)
    {
        var file = XgFileReader.ReadFile(Fixture(fileName));
        XgDecisionIterator.IterateDiagramRequests(file, fileName).Count().Should().Be(expected);
    }

    // -----------------------------------------------------------------------
    //  PlayAnalysis: the one row must be the move, never the cube
    // -----------------------------------------------------------------------

    [Fact]
    public void PlayAnalysis_YieldsMoveRowNotCubeRow()
    {
        var file = XgFileReader.ReadFile(Fixture("PlayAnalysis.xgp"));
        var rows = XgDecisionIterator.Iterate(file, "PlayAnalysis.xgp").ToList();
        rows.Should().ContainSingle();
        rows[0].IsCube.Should().BeFalse(
            "PlayAnalysis.xgp contains an analysed move and an unanalysed cube; " +
            "only the move row should be yielded");
    }

    [Fact]
    public void Opening_3265643165_YieldsMoveRowOnly_NotPhantomCubeRow()
    {
        const string name = "Opening 32 65 64 31 65.xgp";
        var file = XgFileReader.ReadFile(Fixture(name));
        var rows = XgDecisionIterator.Iterate(file, name).ToList();
        rows.Should().ContainSingle(
            "the cube has Level=-100 (queued, never analysed) — only the analysed " +
            "move row should be yielded");
        rows[0].IsCube.Should().BeFalse(
            "the only yielded row must be the analysed move, not the phantom cube");
    }

    [Fact]
    public void Opening_3265643165_DiagramRequests_YieldMoveRequestOnly()
    {
        const string name = "Opening 32 65 64 31 65.xgp";
        var file = XgFileReader.ReadFile(Fixture(name));
        var requests = XgDecisionIterator.IterateDiagramRequests(file, name).ToList();
        requests.Should().ContainSingle();
        requests[0].Decision.IsCube.Should().BeFalse(
            "the cube was requested but never analysed — the diagram request iterator " +
            "must skip it, not emit an empty cube panel");
    }
}
