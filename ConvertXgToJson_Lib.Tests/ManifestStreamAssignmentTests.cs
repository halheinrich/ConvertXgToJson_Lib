using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Parse-level pins for manifest-directed stream assignment against
/// XG-authored files: commentless files get an empty comment table (before
/// manifest-based assignment, the manifest itself parsed as one garbage
/// comment), rollout streams stay assigned, files with a real temp.xgc keep
/// their comments, the two files that showed stored entries read them raw
/// (halheinrich/backgammon#343), and a file whose manifest names temp.xgr2
/// reads its match with that entry set aside (halheinrich/backgammon#419). The synthetic container-level coverage lives
/// in <see cref="DecompressionTests"/> and
/// <see cref="XgContainerDirectoryTests"/>; these fixtures pin the same
/// behaviour against bytes real XG wrote.
/// </summary>
[Collection("FileIO")]
public class ManifestStreamAssignmentTests
{
    private static string Fixture(string name) => Path.Combine(TestPaths.FixtureFilesDir, name);

    [Fact]
    public void CommentlessXgFixture_ParsesWithEmptyCommentTable()
    {
        // MTCH4064.xg carries only temp.xg + temp.xgi; before manifest-based
        // assignment its container manifest parsed as one garbage comment.
        var file = XgFileReader.ReadFile(Fixture("MTCH4064.xg"));
        file.Comments.Should().BeEmpty("the fixture has no temp.xgc stream");
    }

    [Fact]
    public void RolloutBearingCommentlessXgp_KeepsRolloutStreamAssigned()
    {
        // match35253054_2_37.xgp carries temp.xg + temp.xgr + temp.xgi: the
        // rollout stream must still land in the xgr slot, and the manifest
        // must not masquerade as its comment table.
        var file = XgFileReader.ReadFile(Fixture("match35253054_2_37.xgp"));
        file.Rollouts.Should().NotBeEmpty("the fixture is pinned as rollout-bearing");
        file.Comments.Should().BeEmpty("the fixture has no temp.xgc stream");
    }

    [Theory]
    [InlineData("CommentExported.xgp", 1)]
    [InlineData("CommentsAddedToXgp.xgp", 2)]
    public void CommentBearingXgpFixtures_KeepTheirRealComments(string name, int commentCount)
    {
        // Regression guard on the comment carriage: files with a real
        // temp.xgc must still parse their RTF entries under manifest-based
        // assignment.
        var file = XgFileReader.ReadFile(Fixture(name));
        file.Comments.Should().HaveCount(commentCount);
        file.Comments.Should().OnlyContain(c => c.StartsWith(@"{\rtf"));
    }

    [Fact]
    public void XgCorpus_NoParsedCommentIsManifestShaped()
    {
        // Fixture-agnostic sweep, tolerating an empty corpus: a manifest
        // misread as a comment surfaces as one garbage entry full of NULs
        // that leaks the inner-file names. Real comments are RTF/plain text
        // and can contain neither.
        foreach (string path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            file.Comments.Should().NotContain(
                c => c.Contains('\0') || c.Contains("temp.xg"),
                $"'{Path.GetFileName(path)}' must not parse container metadata as a comment");
        }
    }

    // -----------------------------------------------------------------------
    //  Stored entries (halheinrich/backgammon#343) — local-only by the
    //  TestData rule; XgContainerDirectoryTests gates the same shapes
    // -----------------------------------------------------------------------

    /// <summary>
    /// The tester's report: this <c>.xgp</c>'s temp.xgc is a stored entry of
    /// two bytes, CR LF. Its flag byte reads 1, so the entry did not match
    /// the four-byte constant the manifest was once read against, and the
    /// manifest itself landed in the comment slot, where BgQuiz's Notes showed
    /// the inner-file names. Read raw, it is a comment table of exactly one
    /// empty comment, and no decision carries a note.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixtureFiles")]
    public void StoredCommentTableXgp_ReadsOneEmptyComment_AndNoDecisionCarriesANote()
    {
        const string name = "21 and 18 anchors 44 BR.xgp";
        var file = XgFileReader.ReadFile(Fixture(name));

        file.Comments.Should().Equal([""], "temp.xgc is stored as CR LF: one empty comment");
        var decisions = XgDecisionIterator.IterateDiagramRequests(file, name).ToList();
        decisions.Should().NotBeEmpty("the position is analysed");
        decisions.Should().OnlyContain(d => d.Descriptive.Comment == null,
            "an empty comment is no note, and no record references anything else");
    }

    /// <summary>
    /// The start of the real <c>.xg</c>'s name whose temp.xgr and temp.xgc
    /// are stored and zero-length, temp.xgc lying exactly at the manifest's
    /// offset. Specific enough to name one match export, and short of the
    /// export's own numbering suffix, which the umbrella's reference check
    /// would read as a bare issue reference; the file is found by it.
    /// </summary>
    private const string ZeroLengthEntriesPrefix = "Shimodaira-Suzuki 2010 Japan Open Final";

    [Fact]
    [Trait("Category", "RequiresFixtureFiles")]
    public void StoredZeroLengthEntriesXg_ReadsAnEmptyCommentTable()
    {
        var matches = Directory.EnumerateFiles(TestPaths.FixtureFilesDir, ZeroLengthEntriesPrefix + "*")
            .Where(path => string.Equals(Path.GetExtension(path), ".xg", StringComparison.OrdinalIgnoreCase))
            .ToList();
        matches.Should().ContainSingle(
            $"exactly one .xg in FixtureFiles starts '{ZeroLengthEntriesPrefix}': none means the fixture is " +
            "missing, more than one that the prefix no longer names it");

        var file = XgFileReader.ReadFile(matches[0]);

        file.Comments.Should().BeEmpty("its temp.xgc is stored with no bytes");
        file.Rollouts.Should().BeEmpty("its temp.xgr is stored with no bytes");
    }

    // -----------------------------------------------------------------------
    //  A fifth inner file (halheinrich/backgammon#419) — local-only by the
    //  TestData rule; XgContainerDirectoryTests gates the rule
    // -----------------------------------------------------------------------

    /// <summary>
    /// A real XG file of save-file version 32 whose manifest names a fifth
    /// inner file, temp.xgr2 — all zero bytes here — beside temp.xg, temp.xgr
    /// and temp.xgi. Until halheinrich/backgammon#419 it was refused whole.
    /// </summary>
    private const string Xgr2Fixture =
        "Mikayel Vardanyan (2.40) - Chris Trencher (2.94) 9pt Monte Carlo Open 8-7-2018 Cons R2 013.xg";

    /// <summary>
    /// With temp.xgr2 set aside the file reads, and what it reads means
    /// something, not only that it decompressed: the players and match
    /// length its name states, its nine games on every surface, and its 17
    /// rollout contexts, each one a rollout that ran, every one referenced
    /// by a cube or play record and no record referring past them — so
    /// temp.xgr was read in step with temp.xg.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixtureFiles")]
    public void Xgr2BearingXg_ReadsItsMatchAndRollouts()
    {
        string path = Fixture(Xgr2Fixture);
        var file = XgFileReader.ReadFile(path);

        var header = file.Records[0].Should().BeOfType<MatchHeaderRecord>().Subject;
        header.Version.Should().Be(32, "the save-file version that carries temp.xgr2");

        var info = XgFileReader.ReadMatchInfo(path)!;
        info.Player1.Should().Be("Mikayel Vardanyan");
        info.Player2.Should().Be("Chris Trencher");
        info.Terms.Should().Be(new MatchTerms { Length = 9 });

        file.Records.OfType<GameHeaderRecord>().Should().HaveCount(9);
        XgFileReader.ReadGameHeaders(path, new XgIteratorState()).Should().HaveCount(9);
        file.Records.OfType<MatchFooterRecord>().Should().ContainSingle("the match was played out");

        file.Rollouts.Should().HaveCount(17);
        file.Rollouts.Should().OnlyContain(r => r.GamesRolled > 0, "each context is a rollout that ran");
        int[] cubeRefs = [.. file.Records.OfType<CubeRecord>().Select(c => c.RolloutIndex).Where(i => i >= 0)];
        int[] moveRefs = [.. file.Records.OfType<MoveRecord>().SelectMany(m => m.RolloutIndices.Where(i => i >= 0))];
        cubeRefs.Should().NotBeEmpty("a cube decision was rolled out");
        moveRefs.Should().NotBeEmpty("a play was rolled out");
        cubeRefs.Concat(moveRefs).Distinct().Should().BeEquivalentTo(Enumerable.Range(0, file.Rollouts.Count),
            "every reference names one of the contexts, and every context is referenced");

        XgDecisionIterator.IterateDiagramRequests(file, Xgr2Fixture).Should().NotBeEmpty();
    }
}
