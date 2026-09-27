using BgDataTypes_Lib;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for <see cref="XgMoveTranslator.Translate"/>: XG's candidate-move
/// bytes decoded into a <see cref="Play"/>, each hit read off the starting
/// board into <see cref="Move.ToPt"/>'s sign. The translator's contract is
/// the play's encoding, so the tests pin encodings
/// (<see cref="Play.IsSameEncoding"/>); how a play is spelt is
/// <see cref="Play.ToNotation"/>'s, pinned in BgDataTypes_Lib, and how it
/// applies is BgDataTypes_Lib's play rule's.
/// </summary>
[Collection("FileIO")]
public class XgMoveTranslatorTests
{
    private static readonly BoardPosition Empty = BoardPosition.Empty;

    private static sbyte[] M(params int[] vals)
    {
        var arr = new sbyte[8];
        for (int i = 0; i < 8; i++) arr[i] = i < vals.Length ? (sbyte)vals[i] : (sbyte)-1;
        return arr;
    }

    private static BoardPosition Board(params (int Point, int Count)[] counts)
    {
        var slots = new int[26];
        foreach (var (point, count) in counts)
            slots[point] = count;
        return new BoardPosition(slots);
    }

    private static void ShouldEncode(Play actual, params Move[] expected) =>
        actual.IsSameEncoding(Play.Create(expected)).Should().BeTrue(
            $"expected [{string.Join(", ", expected)}], got [{string.Join(", ", Moves(actual))}]");

    private static IEnumerable<Move> Moves(Play play)
    {
        foreach (var move in play)
            yield return move;
    }

    // Decoding -------------------------------------------------------------

    /// <summary>Regular points are the raw value plus one, pair by pair in XG's order.</summary>
    [Fact]
    public void Translate_RegularPoints_AreValuePlusOne()
    {
        ShouldEncode(XgMoveTranslator.Translate(M(12, 7, 23, 21), BoardPosition.Standard),
            new Move(13, 8), new Move(24, 22));
    }

    /// <summary>XG's bar (24) is point 25.</summary>
    [Fact]
    public void Translate_BarEntry_FromPoint25()
    {
        ShouldEncode(XgMoveTranslator.Translate(M(24, 20), Empty), new Move(25, 21));
    }

    /// <summary>
    /// Any negative destination is a bear-off (XG writes overshoots as
    /// <c>from − die</c>), encoded as <see cref="Move.ToPt"/> 0.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(-5)]
    public void Translate_NegativeDestination_IsABearOff(int to)
    {
        ShouldEncode(XgMoveTranslator.Translate(M(1, to), Empty), new Move(2, 0));
    }

    /// <summary>
    /// XG's multi-die encodings are kept as XG stores them
    /// (halheinrich/backgammon#277): the translator neither joins nor splits
    /// pairs.
    /// </summary>
    [Fact]
    public void Translate_KeepsXgsPairsAsStored()
    {
        ShouldEncode(XgMoveTranslator.Translate(M(23, 20, 20, 14), Empty), new Move(24, 21), new Move(21, 15));
        ShouldEncode(XgMoveTranslator.Translate(M(23, 14), Empty), new Move(24, 15));
    }

    [Fact]
    public void Translate_FourMoves_NoTerminator()
    {
        ShouldEncode(XgMoveTranslator.Translate(M(23, 19, 12, 8, 12, 8, 7, 3), Empty),
            new Move(24, 20), new Move(13, 9), new Move(13, 9), new Move(8, 4));
    }

    [Fact]
    public void Translate_TerminatorFirst_IsTheEmptyPlay()
    {
        XgMoveTranslator.Translate(M(-1), Empty).Count.Should().Be(0);
        XgMoveTranslator.Translate([], Empty).Count.Should().Be(0);
    }

    // Hits -----------------------------------------------------------------

    /// <summary>XG's encoding has no hit mark: a landing on an opponent blot of the starting board is the hit.</summary>
    [Fact]
    public void Translate_LandingOnABlot_IsMarkedAsTheHit()
    {
        var board = Board((24, 2), (18, -1));
        ShouldEncode(XgMoveTranslator.Translate(M(23, 17), board), new Move(24, -18));
    }

    /// <summary>A landing on an empty point, the mover's own or a made point is not a hit.</summary>
    [Fact]
    public void Translate_LandingOffABlot_IsNotMarked()
    {
        var board = Board((24, 2), (18, -2), (17, 1));
        ShouldEncode(XgMoveTranslator.Translate(M(23, 16, 23, 20), board), new Move(24, 17), new Move(24, 21));
    }

    /// <summary>
    /// A hit point stops counting as a blot for the rest of the play, so a
    /// second landing there is not marked again: a point is hit once.
    /// </summary>
    [Fact]
    public void Translate_SecondLandingOnAHitPoint_IsNotMarkedAgain()
    {
        var board = Board((24, 1), (20, 1), (18, -1));
        ShouldEncode(XgMoveTranslator.Translate(M(23, 17, 19, 17), board), new Move(24, -18), new Move(20, 18));
    }

    [Fact]
    public void Translate_TwoSeparateBlots_AreEachMarked()
    {
        var board = Board((24, 1), (13, 1), (18, -1), (9, -1));
        ShouldEncode(XgMoveTranslator.Translate(M(23, 17, 12, 8), board), new Move(24, -18), new Move(13, -9));
    }

    /// <summary>An intermediate hit keeps its own pair: the chain's later leg starts on the hit point unmarked.</summary>
    [Fact]
    public void Translate_HitThenContinue_MarksOnlyTheLandingOnTheBlot()
    {
        var board = Board((24, 2), (18, -1));
        ShouldEncode(XgMoveTranslator.Translate(M(23, 17, 17, 16), board), new Move(24, -18), new Move(18, 17));
    }

    /// <summary>A bear-off lands nowhere, so it never hits.</summary>
    [Fact]
    public void Translate_BearOff_NeverHits()
    {
        var board = Board((2, 1), (1, -1));
        ShouldEncode(XgMoveTranslator.Translate(M(1, -1), board), new Move(2, 0));
    }

    // Real corpus ----------------------------------------------------------

    /// <summary>
    /// Every candidate of every checker play the corpus yields spells a
    /// non-empty notation through <see cref="Play.ToNotation"/>: the
    /// sentinel analyses, which translate to nothing, never reach the
    /// translator. Reads the local corpus; gates nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_EveryCandidate_SpellsANotation()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            foreach (var play in XgDecisionIterator.IterateDiagramRequests(file, Path.GetFileName(path)).OfType<CheckerPlayDecision>())
            {
                foreach (var candidate in play.Decision.Plays)
                    candidate.Notation.Should().NotBeNullOrEmpty($"{Path.GetFileName(path)} {play.Id}");
            }
        }
    }
}
