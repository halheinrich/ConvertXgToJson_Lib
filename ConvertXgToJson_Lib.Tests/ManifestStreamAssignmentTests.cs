namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Parse-level pins for manifest-directed stream assignment against
/// XG-authored files: commentless files get an empty comment table (before
/// manifest-based assignment, the manifest itself parsed as one garbage
/// comment), rollout streams stay assigned, files with a real temp.xgc keep
/// their comments, and the two files that showed stored entries read them raw
/// (halheinrich/backgammon#343). The synthetic container-level coverage lives
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
}
