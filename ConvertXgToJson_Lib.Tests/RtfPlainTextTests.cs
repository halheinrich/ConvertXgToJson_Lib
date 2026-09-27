using System.Text.RegularExpressions;
using AwesomeAssertions.Execution;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using Xunit.Abstractions;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The contract of <see cref="RtfPlainText"/> — XG stores a decision's
/// comment as an RTF document, and this library is the one member that knows
/// that, so it stamps the text a reader would see
/// (halheinrich/backgammon#233). BgQuiz shows <c>DescriptiveData.Comment</c>
/// verbatim and must never inspect its format, which is why the whole RTF
/// source reached the screen before this existed.
/// </summary>
/// <remarks>
/// Everything here but the corpus test at the bottom is synthesized: the
/// gating coverage owes nothing to a file. The oracle document is the one
/// real comment the user's rejection came with, checked against real fixture
/// bytes before being trusted — <c>Opening 32 55 5184 4PATW.xgp</c> and
/// <c>Opening 41 21 at diff scores.xgp</c> carry the same shape verbatim
/// (<c>\viewkind4\uc1\pard\b\fs24 …\par</c>, a raw CRLF after every
/// <c>\par</c>, a trailing <c>\b0\fs20\par\par</c>).
/// </remarks>
public class RtfPlainTextTests(ITestOutputHelper output)
{
    /// <summary>
    /// The user's oracle: one real XG comment, and the three lines XG's own
    /// comment pane shows for it. The document's raw line endings are
    /// whatever this source file carries — immaterial, because a raw CR or LF
    /// in an RTF source is not text.
    /// </summary>
    private const string OracleSource = """
        {\rtf1\ansi\ansicpg1252\deff0\deflang1033{\fonttbl{\f0\fnil\fcharset0 Arial;}}
        \viewkind4\uc1\pard\b\fs24 ++  24/18 13/9 better than 2pt by .09\par
        \par
        Below we see its better by .11\par
        \b0\fs20\par
        \par
        }
        """;

    private const string OracleText =
        "++  24/18 13/9 better than 2pt by .09\r\n"
        + "\r\n"
        + "Below we see its better by .11";

    // ------------------------------------------------------------------ //
    //  The oracle
    // ------------------------------------------------------------------ //

    /// <summary>
    /// The whole contract at once over one real document: the font table
    /// contributes nothing, each control word's delimiting space is consumed,
    /// formatting is dropped rather than rendered, <c>\par</c> breaks the
    /// line, the interior blank line survives, and the trailing run of breaks
    /// is trimmed. Three lines, exactly what XG shows.
    /// </summary>
    [Fact]
    public void Extract_TheUsersOracleDocument_YieldsTheThreeLinesXgShows()
    {
        string plain = RtfPlainText.Extract(OracleSource);

        plain.Should().Be(OracleText);
        plain.Split("\r\n").Should().Equal(
            "++  24/18 13/9 better than 2pt by .09",
            "",
            "Below we see its better by .11");
    }

    // ------------------------------------------------------------------ //
    //  Plain text is not touched
    // ------------------------------------------------------------------ //

    /// <summary>
    /// A comment that does not open with the RTF header is plain text and
    /// passes through byte for byte — including one that merely holds a
    /// backslash or a brace, which a format sniffer less strict than "does it
    /// start with <c>{\rtf</c>" would mangle. The fixture builder writes such
    /// comments, and every decision comment a caller synthesizes is one.
    /// </summary>
    [Theory]
    [InlineData("8/5 6/5 makes the best point.")]
    [InlineData("Close.\r\nWith one more checker on the 13 it is a clear double.")]
    [InlineData(@"Not a document: 50\50 at best, and {sic} in the original.")]
    [InlineData(@"\par is a word here, not a break.")]
    [InlineData("")]
    [InlineData("   leading and trailing spaces are the caller's   ")]
    public void Extract_TextThatIsNotAnRtfDocument_PassesThroughUnchanged(string comment) =>
        RtfPlainText.Extract(comment).Should().BeSameAs(comment);

    // ------------------------------------------------------------------ //
    //  The rules, one at a time
    // ------------------------------------------------------------------ //

    /// <summary>
    /// The rules of the contract, each over the smallest document that shows
    /// it. <c>\line</c> breaks exactly as <c>\par</c> does; a control word's
    /// one delimiting space is consumed; formatting words vanish without
    /// leaving a gap; the <c>\'hh</c> escape decodes in the document's ANSI
    /// code page, Latin-1 below <c>0x80</c> and Windows-1252 above it;
    /// <c>\uN</c> emits the code unit once and swallows its fallback; a
    /// textless destination and an ignorable one both contribute nothing.
    /// </summary>
    [Theory]
    // \line breaks like \par, and both trim off the end.
    [InlineData(@"{\rtf1 one\line two\par}", "one\r\ntwo")]
    [InlineData(@"{\rtf1 one\par two\line}", "one\r\ntwo")]
    // The delimiting space belongs to the control word; a second space is text.
    [InlineData(@"{\rtf1\fs24 tight}", "tight")]
    [InlineData(@"{\rtf1\fs24  wide}", " wide")]
    // Formatting is dropped, never rendered, and leaves no gap.
    [InlineData(@"{\rtf1 bold \b now\b0 off}", "bold nowoff")]
    [InlineData(@"{\rtf1 bold \b now \b0 off}", "bold now off")]
    [InlineData(@"{\rtf1 a\tab b}", "a\tb")]
    // Control symbols.
    [InlineData(@"{\rtf1 \\ \{ \} done}", @"\ { } done")]
    [InlineData(@"{\rtf1 a\~b}", "a b")]
    [InlineData(@"{\rtf1 a\_b}", "a-b")]
    [InlineData(@"{\rtf1 a\-b}", "ab")]
    // \'hh in the document's ANSI code page: Latin-1, Windows-1252 at 0x80-0x9F.
    [InlineData(@"{\rtf1 caf\'e9}", "caf\u00E9")]
    [InlineData(@"{\rtf1 \'80 20}", "\u20AC 20")]
    [InlineData(@"{\rtf1 \'93quoted\'94}", "\u201Cquoted\u201D")]
    // \uN, with its fallback skipped exactly once.
    [InlineData(@"{\rtf1\uc1 \u8364 ?}", "\u20AC")]
    [InlineData(@"{\rtf1\uc1 \u8364 ? and more}", "\u20AC and more")]
    [InlineData(@"{\rtf1\uc2 \u8364 ?? tail}", "\u20AC tail")]
    [InlineData(@"{\rtf1\uc0 \u8364 tail}", "\u20ACtail")]
    [InlineData(@"{\rtf1\uc1 \u-4064 ?}", "\uF020")]
    // Textless and ignorable destinations.
    [InlineData(@"{\rtf1{\fonttbl{\f0\fnil\fcharset0 Arial;}}body}", "body")]
    [InlineData(@"{\rtf1{\colortbl;\red0\green0\blue0;}body}", "body")]
    [InlineData(@"{\rtf1{\*\generator Riched20 10.0.19041}body}", "body")]
    [InlineData(@"{\rtf1{\info{\author Someone}}body}", "body")]
    // A raw CR or LF in the source is not text.
    [InlineData("{\\rtf1 one\r\ntwo}", "onetwo")]
    // Interior blank lines and runs of spaces are kept exactly.
    [InlineData(@"{\rtf1 a\par\par\par b}", "a\r\n\r\n\r\nb")]
    [InlineData(@"{\rtf1\fs24      indented\par}", "     indented")]
    public void Extract_OneRuleAtATime(string source, string expected) =>
        RtfPlainText.Extract(source).Should().Be(expected);

    // ------------------------------------------------------------------ //
    //  Degrade, never block
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Malformed RTF yields the text extracted so far and never throws: one
    /// bad comment in a corpus of hundreds must not end the walk. An
    /// unbalanced brace in either direction, a truncated <c>\'hh</c>, a
    /// trailing lone backslash, a <c>\'</c> whose digits are not hex, and a
    /// parameter far past <see cref="int"/> all degrade quietly.
    /// </summary>
    [Theory]
    [InlineData(@"{\rtf1 unclosed", "unclosed")]
    [InlineData(@"{\rtf1 extra}}} tail", "extra tail")]
    [InlineData(@"{\rtf1 caf\'", "caf")]
    [InlineData(@"{\rtf1 caf\'e", "cafe")]
    [InlineData(@"{\rtf1 caf\'zz done", "cafzz done")]
    [InlineData(@"{\rtf1 trailing\", "trailing")]
    [InlineData(@"{\rtf1 a\fs999999999999999999 b}", "ab")]
    [InlineData(@"{\rtf1\uc999999999 \u8364 ?}", "\u20AC")]
    [InlineData(@"{\rtf1{\fonttbl{\f0 Arial;", "")]
    public void Extract_MalformedDocument_DegradesAndNeverThrows(string source, string expected)
    {
        string plain = RtfPlainText.Extract(source);
        plain.Should().Be(expected);
    }

    /// <summary>
    /// A <c>\uN</c> fallback never swallows a line break: a document whose
    /// fallback is missing must lose a character, not a paragraph.
    /// </summary>
    [Fact]
    public void Extract_UnicodeFallbackMissing_DoesNotSwallowTheFollowingBreak() =>
        RtfPlainText.Extract(@"{\rtf1\uc1 \u8364\par next}")
                    .Should().Be("\u20AC\r\nnext");

    // ------------------------------------------------------------------ //
    //  Through the real wire
    // ------------------------------------------------------------------ //

    /// <summary>
    /// The conversion where it actually matters: a comment holding an RTF
    /// document is written to a real file, read back, and iterated, and the
    /// stamped <c>DescriptiveData.Comment</c> is the plain text — while the
    /// file's own comment table still holds XG's bytes verbatim. That split
    /// is the design: only the stamp path converts, so a round trip through
    /// the writer reproduces the source document.
    /// </summary>
    [Fact]
    public void Iterate_ACommentHoldingAnRtfDocument_StampsPlainText_WhileTheTableStaysRaw()
    {
        // Two different documents, so the test also shows the table staying
        // aligned: the cube's comment must not reach the play.
        const string playSource = @"{\rtf1\ansi\ansicpg1252\fs20 Forced.\line Nothing else is legal.\par}";
        const string playText = "Forced.\r\nNothing else is legal.";

        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .CubeDecision(XgPlayer.Player1, new XgCubeEquities(0.2, 0.3, 1.0),
                doublerAction: CubeAction.NoDouble, comment: OracleSource)
            .Play(XgPlayer.Player1, new DiceRoll(3, 1),
                Play.Create(new Move(8, 5), new Move(6, 5)), comment: playSource);

        XgFile onTheWire = XgFileReader.ReadStream(
            new MemoryStream(XgFileWriter.ToBytes(builder.Build())));

        onTheWire.Comments.Should().Equal([OracleSource, playSource],
            "the comment table carries XG's bytes verbatim — the reader, the writer "
            + "and the slice export all copy it, and only the stamp path converts");

        XgDecisionIterator.IterateDiagramRequests(onTheWire, "synthetic.xg")
            .Select(r => r.Descriptive.Comment)
            .Should().Equal([OracleText, playText],
                "both stamp sites — the cube decision and the play — go through "
                + "MatchContext.CommentAt, the one conversion site");
    }

    // ------------------------------------------------------------------ //
    //  The real corpus — local only
    // ------------------------------------------------------------------ //

    /// <summary>Any backslash followed by an ASCII letter — an RTF control
    /// word if one survived extraction. A note legitimately holding such a
    /// pair (a Windows path, say) would read as a false positive; none in the
    /// corpus does, and this test is local-only, so the trade is worth the
    /// breadth.</summary>
    private static readonly Regex ControlWord = new(@"\\[A-Za-z]", RegexOptions.None);

    /// <summary>
    /// Every commented file the local fixture directory holds, end to end: no
    /// stamped comment opens with the RTF header, and none carries a surviving
    /// control word. The extracted text of each is printed, so a report can
    /// show what a reader now sees.
    ///
    /// <para>
    /// Local-only by the TestData rule: <c>TestData/FixtureFiles/</c> is
    /// gitignored, so this carries the CI-excluded
    /// <c>RequiresFixtureFiles</c> trait and nothing gating depends on it —
    /// the gating coverage is the synthesized contract above. The commented
    /// files present cover the properties the user copied them for: the
    /// longest comment (<c>Opening 32S 66 66 All.xgp</c>, eleven breaks), the
    /// commented cube decisions (<c>DoubleAnalysis.xgp</c>,
    /// <c>TakeAnalysis.xgp</c>), a line opening with a run of spaces
    /// (<c>Opening 32 65 63 DMP &amp; CASH.xgp</c>), and both break kinds
    /// (<c>\line</c> in <c>PlayAnalysis.xgp</c> and
    /// <c>Opening 41 21 at diff scores.xgp</c>).
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixtureFiles")]
    public void IterateDiagramRequests_EveryCommentedFixture_StampsPlainText()
    {
        if (!Directory.Exists(TestPaths.FixtureFilesDir))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture directory not present: {TestPaths.FixtureFilesDir}.");

        int stamped = 0;
        using var scope = new AssertionScope();

        foreach (string path in Directory.EnumerateFiles(TestPaths.FixtureFilesDir)
                                         .Where(IsXgFile)
                                         .OrderBy(p => p, StringComparer.Ordinal))
        {
            XgFile file;
            try
            {
                file = XgFileReader.ReadFile(path);
            }
            catch (Exception ex)
            {
                output.WriteLine($"{Path.GetFileName(path)}: unreadable ({ex.GetType().Name}), skipped");
                continue;
            }

            if (file.Comments.All(string.IsNullOrEmpty))
                continue;

            string name = Path.GetFileName(path);
            foreach (var request in XgDecisionIterator.IterateDiagramRequests(file, name))
            {
                if (request.Descriptive.Comment is not { } comment)
                    continue;

                stamped++;
                output.WriteLine($"=== {name}  game {request.Game} "
                    + $"move {request.MoveNumber} "
                    + $"({(request.Kind == DecisionKind.Cube ? "cube" : "play")})");
                output.WriteLine(comment);
                output.WriteLine("");

                comment.Should().NotStartWith(@"{\rtf",
                    $"{name} must reach a consumer as text, not as an RTF document");
                ControlWord.IsMatch(comment).Should().BeFalse(
                    $"{name} must carry no surviving RTF control word; it reads:\n{comment}");
            }
        }

        stamped.Should().BePositive(
            "the local fixture directory holds commented files and this test would "
            + "otherwise pass without having looked at one");
    }

    private static bool IsXgFile(string path) =>
        path.EndsWith(".xgp", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".xg", StringComparison.OrdinalIgnoreCase);
}
