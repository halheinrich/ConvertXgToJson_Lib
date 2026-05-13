using BgDataTypes_Lib;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for the producer-side <see cref="DecisionId"/> stamping on
/// <see cref="DecisionRow.Id"/> and <see cref="BgDecisionData.Id"/>. Covers
/// the four stamp sites (<c>BuildMoveRow</c>, <c>BuildMoveDiagramRequest</c>,
/// <c>BuildCubeRows</c>, <c>BuildCubeDiagramRequests</c>) via end-to-end
/// iteration and the format-dispatch helper directly via
/// <c>InternalsVisibleTo</c>.
///
/// <para>
/// Producer contract under test:
/// </para>
/// <list type="bullet">
///   <item><description>
///     A <c>.xg</c> source file yields <see cref="XgDecisionId"/> with the
///     within-file tuple <c>(Filename, Game, MoveNumber, IsCube)</c>;
///     cube records use <c>ctx.MoveNumber + 1</c> so the Id agrees with
///     the emitted <see cref="DecisionRow.MoveNumber"/>.
///   </description></item>
///   <item><description>
///     A <c>.xgp</c> source file yields <see cref="XgpDecisionId"/> keyed
///     on the bare filename only.
///   </description></item>
///   <item><description>
///     <see cref="XgDecisionIterator.Iterate"/> and
///     <see cref="XgDecisionIterator.IterateDiagramRequests"/> stamp the
///     same Id at corresponding indices — the two surfaces commit to a
///     single producer convention.
///   </description></item>
///   <item><description>
///     <c>sourceFile == null</c> is a producer-contract violation and
///     throws <see cref="InvalidOperationException"/> eagerly, before any
///     deferred enumeration begins.
///   </description></item>
///   <item><description>
///     An unsupported extension (anything other than <c>.xg</c> / <c>.xgp</c>)
///     is also a producer-contract violation; <c>BuildDecisionId</c> throws
///     <see cref="InvalidOperationException"/> on first stamp.
///   </description></item>
/// </list>
/// </summary>
[Collection("FileIO")]
public class XgDecisionIdStampingTests
{
    // -----------------------------------------------------------------------
    //  Corpus-wide invariants
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every <see cref="DecisionRow"/> emitted from a <c>.xg</c> file in
    /// <c>TestData/xg/</c> carries an <see cref="XgDecisionId"/> whose
    /// <see cref="XgDecisionId.Filename"/> equals the bare source filename
    /// and whose tuple coordinates match the emitted row's
    /// <c>(Game, MoveNumber, IsCube)</c>. Iterates the corpus and rebuilds
    /// the expected Id from each row's published fields — a regression
    /// that disagreed between Id and emitted fields would fail here
    /// regardless of whether the disagreement was on the Id side or the
    /// row-field side.
    /// </summary>
    [Fact]
    public void Iterate_XgCorpus_EmitsXgDecisionIdAgreeingWithRowFields()
    {
        int rowsChecked = 0;
        int cubeRowsChecked = 0;

        foreach (var path in TestPaths.XgFiles)
        {
            string sourceFile = Path.GetFileName(path);
            var file = XgFileReader.ReadFile(path);

            foreach (var row in XgDecisionIterator.Iterate(file, sourceFile))
            {
                row.Id.Should().BeOfType<XgDecisionId>(
                    $"{sourceFile}: .xg files must stamp XgDecisionId, got {row.Id.GetType().Name}");

                var xgId = (XgDecisionId)row.Id;
                xgId.Filename.Should().Be(sourceFile,
                    $"{sourceFile} row [{rowsChecked}]: Id.Filename must equal the bare source filename");
                xgId.Game.Should().Be(row.Game,
                    $"{sourceFile} row [{rowsChecked}]: Id.Game must match emitted DecisionRow.Game");
                xgId.MoveNumber.Should().Be(row.MoveNumber,
                    $"{sourceFile} row [{rowsChecked}]: Id.MoveNumber must match emitted DecisionRow.MoveNumber " +
                    "(cube path uses ctx.MoveNumber + 1; Id must reflect the emitted value)");
                xgId.IsCube.Should().Be(row.IsCube,
                    $"{sourceFile} row [{rowsChecked}]: Id.IsCube must match DecisionRow.IsCube");

                if (row.IsCube) cubeRowsChecked++;
                rowsChecked++;
            }
        }

        rowsChecked.Should().BeGreaterThan(0,
            "the .xg corpus must contain at least one analysed decision; otherwise this test passes vacuously");
        cubeRowsChecked.Should().BeGreaterThan(0,
            "the .xg corpus must contain at least one cube decision; otherwise the IsCube=true branch is untested");
    }

    /// <summary>
    /// Every <see cref="DecisionRow"/> emitted from a <c>.xgp</c> file in
    /// <c>TestData/xgp/</c> carries an <see cref="XgpDecisionId"/> whose
    /// <see cref="XgpDecisionId.Filename"/> equals the bare source
    /// filename. <c>.xgp</c> files are single-decision-per-file by XG's
    /// design — game / move / cube coordinates are not part of the Id.
    /// </summary>
    [Fact]
    public void Iterate_XgpCorpus_EmitsXgpDecisionIdWithBareFilename()
    {
        int rowsChecked = 0;

        foreach (var path in TestPaths.XgpFiles)
        {
            string sourceFile = Path.GetFileName(path);
            var file = XgFileReader.ReadFile(path);

            foreach (var row in XgDecisionIterator.Iterate(file, sourceFile))
            {
                row.Id.Should().BeOfType<XgpDecisionId>(
                    $"{sourceFile}: .xgp files must stamp XgpDecisionId, got {row.Id.GetType().Name}");

                var xgpId = (XgpDecisionId)row.Id;
                xgpId.Filename.Should().Be(sourceFile,
                    $"{sourceFile}: Id.Filename must equal the bare source filename");

                rowsChecked++;
            }
        }

        rowsChecked.Should().BeGreaterThan(0,
            "the .xgp corpus must contain at least one analysed decision; otherwise this test passes vacuously");
    }

    /// <summary>
    /// <see cref="XgDecisionIterator.Iterate"/> and
    /// <see cref="XgDecisionIterator.IterateDiagramRequests"/> commit to a
    /// single producer convention: paired 1:1 in order across both .xg and
    /// .xgp corpora, every <see cref="DecisionRow.Id"/> equals the
    /// corresponding <see cref="BgDecisionData.Id"/>. Catches drift between
    /// the four <c>Build*</c> sites (a regression where one stamp site uses
    /// a different coordinate would surface here even if each stamp's
    /// internal self-consistency tests passed).
    /// </summary>
    [Fact]
    public void IterateAndIterateDiagramRequests_StampMatchingIds()
    {
        int pairsChecked = 0;

        foreach (var path in TestPaths.XgFiles.Concat(TestPaths.XgpFiles))
        {
            string sourceFile = Path.GetFileName(path);
            var file = XgFileReader.ReadFile(path);

            var rows = XgDecisionIterator.Iterate(file, sourceFile).ToList();
            var requests = XgDecisionIterator.IterateDiagramRequests(file, sourceFile).ToList();

            requests.Count.Should().Be(rows.Count,
                $"{sourceFile}: Iterate and IterateDiagramRequests are 1:1 by contract");

            for (int i = 0; i < rows.Count; i++)
            {
                requests[i].Id.Should().Be(rows[i].Id,
                    $"{sourceFile} record [{i}]: BgDecisionData.Id must equal DecisionRow.Id");
                pairsChecked++;
            }
        }

        pairsChecked.Should().BeGreaterThan(0,
            "the combined corpus must contain at least one analysed decision; otherwise this test passes vacuously");
    }

    // -----------------------------------------------------------------------
    //  Multi-game .xg fixture — pins tuple coordinates across game boundary
    // -----------------------------------------------------------------------

    /// <summary>
    /// On <c>match35041658.xg</c> (multi-game, includes a Crawford game 4):
    /// the first emitted decision carries an Id with <c>Game == 1</c>, and
    /// at least one emitted decision carries <c>Game &gt; 1</c>. A regression
    /// that hard-coded <c>Game = 1</c> at the stamp sites would pass the
    /// first assertion but fail the second; a regression that skipped the
    /// Id stamp on cube rows would fail at the BeOfType check.
    /// </summary>
    [Fact]
    public void Iterate_Match35041658_StampsXgDecisionIdAcrossGameBoundary()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "match35041658.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on match35041658.xg being in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        var rows = XgDecisionIterator.Iterate(file, "match35041658.xg").ToList();

        rows.Should().NotBeEmpty("fixture must yield at least one decision");

        var firstId = rows[0].Id.Should().BeOfType<XgDecisionId>().Subject;
        firstId.Filename.Should().Be("match35041658.xg");
        firstId.Game.Should().Be(1, "the first emitted decision is in game 1");

        int maxGame = rows.Max(r => ((XgDecisionId)r.Id).Game);
        maxGame.Should().BeGreaterThan(1,
            "match35041658.xg spans multiple games; at least one Id must carry Game > 1");
    }

    // -----------------------------------------------------------------------
    //  Cube path: Id.MoveNumber tracks the emitted MoveNumber (+1 contract)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cube emissions write <c>MoveNumber = ctx.MoveNumber + 1</c> on the
    /// emitted row; the stamped Id must use the same value, not the raw
    /// <c>ctx.MoveNumber</c>. Asserts the equality directly on every cube
    /// row across the .xg corpus. Non-vacuousness: at least one cube row
    /// must be encountered.
    /// </summary>
    [Fact]
    public void Iterate_CubeRows_IdMoveNumberMatchesEmittedMoveNumber()
    {
        int cubeRowsChecked = 0;

        foreach (var path in TestPaths.XgFiles)
        {
            string sourceFile = Path.GetFileName(path);
            var file = XgFileReader.ReadFile(path);

            foreach (var row in XgDecisionIterator.Iterate(file, sourceFile).Where(r => r.IsCube))
            {
                var xgId = row.Id.Should().BeOfType<XgDecisionId>().Subject;
                xgId.MoveNumber.Should().Be(row.MoveNumber,
                    $"{sourceFile} cube row at game {row.Game} move {row.MoveNumber}: " +
                    "Id.MoveNumber must equal the emitted DecisionRow.MoveNumber " +
                    "(i.e. ctx.MoveNumber + 1 per the producer convention)");
                xgId.IsCube.Should().BeTrue(
                    $"{sourceFile} cube row at game {row.Game} move {row.MoveNumber}: Id.IsCube must be true");
                cubeRowsChecked++;
            }
        }

        cubeRowsChecked.Should().BeGreaterThan(0,
            "the .xg corpus must contain at least one cube decision; otherwise this test passes vacuously");
    }

    // -----------------------------------------------------------------------
    //  .xgp fixture — pins Filename equality precisely
    // -----------------------------------------------------------------------

    /// <summary>
    /// <c>Opening 32 65 64 31 65.xgp</c> is a single-decision position
    /// file. Its first emitted decision carries an <see cref="XgpDecisionId"/>
    /// whose <see cref="XgpDecisionId.Filename"/> exactly equals
    /// <c>"Opening 32 65 64 31 65.xgp"</c> — including the embedded spaces.
    /// Catches regressions that strip the extension, normalize case, or
    /// route a <c>.xgp</c> through the <c>.xg</c> tuple shape.
    /// </summary>
    [Fact]
    public void Iterate_OpeningFixture_StampsXgpDecisionIdWithExactFilename()
    {
        string fixtureName = "Opening 32 65 64 31 65.xgp";
        string path = Path.Combine(TestPaths.FixtureFilesDir, fixtureName);
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on the opening-roll fixture in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        var firstRow = XgDecisionIterator.Iterate(file, fixtureName).First();

        var xgpId = firstRow.Id.Should().BeOfType<XgpDecisionId>().Subject;
        xgpId.Filename.Should().Be(fixtureName,
            "XgpDecisionId.Filename must round-trip the bare filename verbatim, including spaces and extension");
    }

    // -----------------------------------------------------------------------
    //  Negative — null sourceFile (eager throw)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Producer contract: <c>sourceFile</c> must be non-null because the
    /// iterator stamps a <see cref="DecisionId"/> on every yielded row.
    /// The check fires <b>eagerly</b> — at the call site, before any
    /// deferred enumeration begins — so the throw surfaces synchronously
    /// without needing a <c>ToList()</c> / <c>foreach</c>. The match-header
    /// <see cref="InvalidDataException"/> remains deferred because that's a
    /// content-level parse error, a different category from this
    /// caller-contract violation.
    /// </summary>
    [Fact]
    public void Iterate_NullSourceFile_ThrowsEagerly()
    {
        var file = new Models.XgFile();
        var act = () => XgDecisionIterator.Iterate(file, sourceFile: null);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*sourceFile*",
               "the eager null check identifies the offending parameter by name in its message");
    }

    /// <summary>
    /// Same contract on the diagram-request surface.
    /// </summary>
    [Fact]
    public void IterateDiagramRequests_NullSourceFile_ThrowsEagerly()
    {
        var file = new Models.XgFile();
        var act = () => XgDecisionIterator.IterateDiagramRequests(file, sourceFile: null);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*sourceFile*",
               "the eager null check identifies the offending parameter by name in its message");
    }

    // -----------------------------------------------------------------------
    //  Negative — unsupported extension (helper unit test + format coverage)
    // -----------------------------------------------------------------------

    /// <summary>
    /// <c>BuildDecisionId</c> dispatches on the source file's extension;
    /// anything other than <c>.xg</c>, <c>.xgp</c>, or <c>.json</c> is a
    /// producer-side contract violation. Direct unit test against the
    /// internal helper — confirms the throw fires regardless of which
    /// <c>Build*</c> stamp site routes through it, without needing a
    /// synthetic <see cref="Models.XgFile"/> to exercise end-to-end.
    /// </summary>
    [Theory]
    [InlineData("foo.xgx")]
    [InlineData("foo.csv")]
    [InlineData("foo")]                 // no extension at all
    [InlineData("foo.xg.bak")]          // extension is .bak, not .xg
    public void BuildDecisionId_UnsupportedExtension_Throws(string sourceFile)
    {
        var act = () => XgDecisionIterator.BuildDecisionId(sourceFile, game: 1, moveNumber: 1, isCube: false);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Unsupported file extension*",
               $"'{sourceFile}' has none of the supported extensions; the helper must reject it");
    }

    /// <summary>
    /// <c>.json</c> is treated as an XG-format-equivalent serialization
    /// (multi-decision content from <see cref="XgFileReader.WriteJsonAsync"/>
    /// / <see cref="XgFileReader.ReadJson"/>): same record-level structure
    /// as <c>.xg</c>, same Id shape. The resulting
    /// <see cref="XgDecisionId.Filename"/> ends in <c>.json</c> — a
    /// <c>.xg</c> and <c>.json</c> of the same content are distinct
    /// on-disk artifacts and legitimately carry different Ids. Pins both
    /// the dispatch and the verbatim filename.
    /// </summary>
    [Fact]
    public void BuildDecisionId_JsonExtension_RoutesToXgDecisionId()
    {
        var id = XgDecisionIterator.BuildDecisionId("match.json", game: 2, moveNumber: 7, isCube: true);
        var xgId = id.Should().BeOfType<XgDecisionId>().Subject;
        xgId.Filename.Should().Be("match.json",
            ".json filenames are stored verbatim — by design, not a bug; cross-format Id identity is explicitly not a goal");
        xgId.Game.Should().Be(2);
        xgId.MoveNumber.Should().Be(7);
        xgId.IsCube.Should().BeTrue();
    }

    /// <summary>
    /// Extension matching is case-insensitive invariant — <c>.XG</c>,
    /// <c>.XgP</c>, <c>.JSON</c> on Windows clones must route to the same
    /// shape as their lowercase forms. Round-trips a mix of cases through
    /// all three branches of the dispatch.
    /// </summary>
    [Theory]
    [InlineData("match.XG", typeof(XgDecisionId))]
    [InlineData("match.Xg", typeof(XgDecisionId))]
    [InlineData("position.XGP", typeof(XgpDecisionId))]
    [InlineData("position.XgP", typeof(XgpDecisionId))]
    [InlineData("match.JSON", typeof(XgDecisionId))]
    [InlineData("match.Json", typeof(XgDecisionId))]
    public void BuildDecisionId_CaseInsensitiveExtension_DispatchesCorrectly(string sourceFile, Type expected)
    {
        var id = XgDecisionIterator.BuildDecisionId(sourceFile, game: 1, moveNumber: 1, isCube: false);
        id.Should().BeOfType(expected);
        id.Filename.Should().Be(sourceFile,
            "the filename is stored verbatim — case-preserving — even when the extension match was case-insensitive");
    }
}
