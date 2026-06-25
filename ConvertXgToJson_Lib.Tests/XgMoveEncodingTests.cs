using ConvertXgToJson_Lib.Parsing;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for the single-sourced XG sentinel vocabulary and point-index
/// resolution in <see cref="XgMoveEncoding"/>:
/// <see cref="XgMoveEncoding.ClassifyCandidate"/> (the recognition the iterator
/// and after-board builder key off) and the defensive fail-fast in
/// <see cref="XgMoveEncoding.DecodeMovePair"/>.
/// </summary>
public class XgMoveEncodingTests
{
    /// <summary>Builds a terminator-padded 8-element raw XG candidate.</summary>
    private static sbyte[] Candidate(params int[] vals)
    {
        var arr = new sbyte[8];
        for (int i = 0; i < 8; i++) arr[i] = i < vals.Length ? (sbyte)vals[i] : (sbyte)-1;
        return arr;
    }

    // -----------------------------------------------------------------------
    //  ClassifyCandidate
    // -----------------------------------------------------------------------

    [Fact]
    public void ClassifyCandidate_IllegalMarkerPair_IsIllegalPlay()
    {
        XgMoveEncoding.ClassifyCandidate(Candidate(-100, -100))
            .Should().Be(SentinelKind.IllegalPlay);
    }

    [Fact]
    public void ClassifyCandidate_IllegalMarkerWithStrayPartner_IsIllegalPlay()
    {
        // The illegal-play marker is keyed on `from == -100` alone, so a stray
        // partner value (here 10) is still recognised as an illegal play.
        XgMoveEncoding.ClassifyCandidate(Candidate(-100, 10, 10, 7, 6, 7, 6, 7))
            .Should().Be(SentinelKind.IllegalPlay);
    }

    [Fact]
    public void ClassifyCandidate_DancePair_IsDance()
    {
        XgMoveEncoding.ClassifyCandidate(Candidate(0, 0))
            .Should().Be(SentinelKind.Dance);
    }

    [Fact]
    public void ClassifyCandidate_NormalCandidate_IsNone()
    {
        // 24/23 — raw (23, 22): a benign single-checker move.
        XgMoveEncoding.ClassifyCandidate(Candidate(23, 22))
            .Should().Be(SentinelKind.None);
    }

    [Fact]
    public void ClassifyCandidate_ShorterThanOnePair_IsNone()
    {
        XgMoveEncoding.ClassifyCandidate(new sbyte[] { -100 })
            .Should().Be(SentinelKind.None);
    }

    // -----------------------------------------------------------------------
    //  DecodeMovePair — normal decoding
    // -----------------------------------------------------------------------

    [Fact]
    public void DecodeMovePair_RegularPoint_ResolvesValuePlusOne()
    {
        var (fromIdx, toIdx, isBearOff) = XgMoveEncoding.DecodeMovePair(23, 22);
        fromIdx.Should().Be(24);
        toIdx.Should().Be(23);
        isBearOff.Should().BeFalse();
    }

    [Fact]
    public void DecodeMovePair_BarEntry_ResolvesToBarIdx()
    {
        var (fromIdx, _, _) = XgMoveEncoding.DecodeMovePair(XgMoveEncoding.Bar, 20);
        fromIdx.Should().Be(XgMoveEncoding.BarIdx);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-3)]
    public void DecodeMovePair_NegativeTo_IsBearOff(int to)
    {
        var (_, toIdx, isBearOff) = XgMoveEncoding.DecodeMovePair(5, (sbyte)to);
        isBearOff.Should().BeTrue();
        toIdx.Should().Be(0, "bear-off destinations are unused (ToIdx 0)");
    }

    // -----------------------------------------------------------------------
    //  DecodeMovePair — defensive fail-fast
    // -----------------------------------------------------------------------

    [Fact]
    public void DecodeMovePair_IllegalMarkerFrom_ThrowsWithLegibleMessage()
    {
        var act = () => XgMoveEncoding.DecodeMovePair(XgMoveEncoding.IllegalPlayMarker, -100);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("from")
            .Which.Message.Should().Contain("illegal-play marker",
                "the -100 case names XG's illegal-play marker specifically");
    }

    [Fact]
    public void DecodeMovePair_FromBeyondBar_Throws()
    {
        var act = () => XgMoveEncoding.DecodeMovePair(25, 10);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("from");
    }

    [Fact]
    public void DecodeMovePair_ToAboveBar_Throws()
    {
        var act = () => XgMoveEncoding.DecodeMovePair(10, 25);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("to");
    }
}
