namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Parse-level pins for manifest-directed stream assignment against
/// XG-authored files: commentless files get an empty comment table (the
/// phantom-comment regression), rollout streams stay assigned, and files
/// with a real temp.xgc keep their comments. The synthetic container-level
/// coverage lives in <see cref="DecompressionTests"/>; these fixtures pin
/// the same behaviour against bytes real XG wrote.
/// </summary>
[Collection("FileIO")]
public class ManifestStreamAssignmentTests
{
    private static string Fixture(string name) => Path.Combine(TestPaths.FixtureFilesDir, name);

    [Fact]
    public void CommentlessXgFixture_ParsesWithEmptyCommentTable()
    {
        // MTCH4064.xg carries only temp.xg + temp.xgi; before manifest-based
        // assignment its container manifest parsed as one phantom garbage
        // comment.
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
}
