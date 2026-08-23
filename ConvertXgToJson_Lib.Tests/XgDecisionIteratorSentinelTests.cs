using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for the iterator-level sentinel-only-analysis skip in
/// <see cref="XgDecisionIterator.Iterate"/> and
/// <see cref="XgDecisionIterator.IterateDiagramRequests"/>.
///
/// <para>
/// Two known XG sentinel patterns reach the iterator in real files:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>Moves[best] = [-100, -100, …]</c> — XG's <b>illegal-play
///     workaround</b>. Source play was illegal, XG forces the next
///     position rather than refusing to load and emits this as the
///     lone "candidate." Without the iterator skip, this used to
///     reach <see cref="Parsing.AfterBoardBuilder.ComputeAfterBoard"/>
///     and throw <see cref="IndexOutOfRangeException"/> on
///     <c>board[from + 1] = board[-99]</c>.
///   </description></item>
///   <item><description>
///     <c>Moves[best] = [0, 0, …]</c> — XG's <b>no-legal-move</b>
///     (dance) encoding. Without the iterator skip, this used to
///     reach <see cref="XgMoveTranslator.Translate"/> and render a
///     "1/1" notation glitch.
///   </description></item>
/// </list>
///
/// <para>
/// Both are filtered at the iterator boundary because neither is of
/// interest downstream — there is no real candidate to evaluate.
/// </para>
/// </summary>
[Collection("FileIO")]
public class XgDecisionIteratorSentinelTests
{
    // -----------------------------------------------------------------------
    //  Synthetic — deterministic coverage of both sentinel intents
    // -----------------------------------------------------------------------

    /// <summary>
    /// The two non-play shapes XG records as a checker-play's only
    /// candidate, as the builder expresses them: the illegal-play marker
    /// and the dance. The raw encodings ((-100, -100) / (0, 0)) are the
    /// builder's and <see cref="XgMoveEncodingTests"/>' concern.
    /// </summary>
    public static TheoryData<string> SentinelKinds => new() { "illegal play", "dance" };

    [Theory]
    [MemberData(nameof(SentinelKinds))]
    public void Iterate_SentinelOnlyAnalysis_RowSkipped(string kind)
    {
        var file = BuildFileWithSentinelOnlyMove(kind);

        var rows = XgDecisionIterator.Iterate(file, sourceFile: "synthetic.xg").ToList();

        rows.Should().BeEmpty(
            $"the only move in the file is a {kind}, a sentinel-only analysis; iterator must filter it");
    }

    [Theory]
    [MemberData(nameof(SentinelKinds))]
    public void IterateDiagramRequests_SentinelOnlyAnalysis_RequestSkipped(string kind)
    {
        var file = BuildFileWithSentinelOnlyMove(kind);

        var requests = XgDecisionIterator.IterateDiagramRequests(file, sourceFile: "synthetic.xg").ToList();

        requests.Should().BeEmpty(
            $"the only move in the file is a {kind}, a sentinel-only analysis; diagram-request iterator must filter it");
    }

    /// <summary>
    /// Sanity check: the same harness with a normal (non-sentinel)
    /// candidate produces a row. Guards against the synthetic file being
    /// degenerate for some unrelated reason.
    /// </summary>
    [Fact]
    public void Iterate_RealCandidate_RowEmitted()
    {
        // 24/23 — a benign single-checker move with no hits and no bear-offs.
        var play = new Play();
        play.Add(new Move(24, 23));
        var builder = XgFileBuilder.ForMatch(7, "P1", "P2");
        builder.AddGame(initialPosition: OneCheckerOn24()).Play(XgPlayer.Player1, new DiceRoll(3, 1), play);

        var rows = XgDecisionIterator.Iterate(builder.Build(), sourceFile: "synthetic.xg").ToList();

        rows.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    //  Fixture-driven — confirmed regression file
    // -----------------------------------------------------------------------

    /// <summary>
    /// Achim Mueller — Mario Sequeira (Monte Carlo WC 2008 SF) is a real
    /// tournament file that contains at least one illegal-play workaround
    /// emission. Before the iterator-level skip, running
    /// <see cref="XgDecisionIterator.Iterate"/> on this file threw
    /// <see cref="IndexOutOfRangeException"/> from
    /// <see cref="Parsing.AfterBoardBuilder.ComputeAfterBoard"/>.
    /// </summary>
    [Fact]
    public void Iterate_AchimMuellerSF_DoesNotThrow()
    {
        var path = TestPaths.AchimMuellerSeqXg;
        var xg = XgFileReader.ReadFile(path);

        var rows = XgDecisionIterator.Iterate(xg, Path.GetFileName(path)).ToList();

        rows.Should().NotBeEmpty(
            "the file contains analysed decisions besides the workaround emissions");
    }

    [Fact]
    public void IterateDiagramRequests_AchimMuellerSF_DoesNotThrow()
    {
        var path = TestPaths.AchimMuellerSeqXg;
        var xg = XgFileReader.ReadFile(path);

        var requests = XgDecisionIterator.IterateDiagramRequests(xg, Path.GetFileName(path)).ToList();

        requests.Should().NotBeEmpty(
            "the file contains analysed decisions besides the workaround emissions");
    }

    /// <summary>
    /// At least one MoveRecord in the corpus must be sentinel-only — otherwise
    /// the regression coverage is illusory (the file would pass even without
    /// the iterator skip). Pins the fixture's continued usefulness.
    /// </summary>
    [Fact]
    public void AchimMuellerSF_ContainsAtLeastOneSentinelOnlyMoveRecord()
    {
        var xg = XgFileReader.ReadFile(TestPaths.AchimMuellerSeqXg);

        bool hasSentinel = xg.Records.OfType<MoveRecord>().Any(IsSentinelOnly);

        hasSentinel.Should().BeTrue(
            "fixture is intentionally chosen because it carries the workaround " +
            "emission; if this fails, the file no longer exercises the bug path");
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal in-memory <see cref="XgFile"/> whose single move
    /// record is the named sentinel — through <see cref="XgFileBuilder"/>,
    /// the fixture SSOT.
    /// </summary>
    private static XgFile BuildFileWithSentinelOnlyMove(string kind)
    {
        var builder = XgFileBuilder.ForMatch(7, "P1", "P2");
        var game = builder.AddGame(initialPosition: OneCheckerOn24());
        var dice = new DiceRoll(3, 1);
        switch (kind)
        {
            case "illegal play": game.IllegalPlay(XgPlayer.Player1, dice); break;
            case "dance": game.Dance(XgPlayer.Player1, dice); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sentinel kind.");
        }
        return builder.Build();
    }

    /// <summary>
    /// A board with one player-1 checker on point 24 — enough that a
    /// synthetic non-sentinel test move (24/23) is plausible without the
    /// test having to model a full game position.
    /// </summary>
    private static int[] OneCheckerOn24()
    {
        var pts = new int[26];
        pts[24] = 1;
        return pts;
    }

    private static bool IsSentinelOnly(MoveRecord move)
    {
        if (move.Analysis.Moves.Length == 0) return false;
        sbyte[] m = move.Analysis.Moves[0];
        if (m.Length < 2) return false;
        return (m[0] == -100 && m[1] == -100) || (m[0] == 0 && m[1] == 0);
    }
}
