using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Unit tests for <see cref="MatchContext"/>'s Crawford / Jacoby / Beaver
/// separation. The old <c>CrawfordJacoby</c> int folded two orthogonal
/// concepts into one, yielding a latent false-positive IsCrawford on
/// Jacoby money-game decisions. These tests pin the separation.
/// </summary>
public class MatchContextTests
{
    // -----------------------------------------------------------------------
    //  Match play — Crawford and non-Crawford games
    // -----------------------------------------------------------------------

    [Fact]
    public void MatchPlay_NonCrawfordGame_IsCrawfordFalse_OthersFalse()
    {
        var ctx = BuildContext(matchLength: 7, jacoby: false, beaver: false, crawfordApplies: false);

        ctx.IsCrawford.Should().BeFalse();
        ctx.IsJacoby.Should().BeFalse();
        ctx.IsBeaver.Should().BeFalse();
        ctx.XgidCrawfordJacobyField.Should().Be(0);
    }

    [Fact]
    public void MatchPlay_CrawfordGame_IsCrawfordTrue_OthersFalse()
    {
        var ctx = BuildContext(matchLength: 7, jacoby: false, beaver: false, crawfordApplies: true);

        ctx.IsCrawford.Should().BeTrue();
        ctx.IsJacoby.Should().BeFalse();
        ctx.IsBeaver.Should().BeFalse();
        ctx.XgidCrawfordJacobyField.Should().Be(1);
    }

    /// <summary>
    /// The old CrawfordJacoby int encoded Jacoby in money games as 1 — the
    /// same value as Crawford in match play. A match-header Jacoby flag must
    /// NOT leak into match-play IsCrawford. This test guards that.
    /// </summary>
    [Fact]
    public void MatchPlay_WithMatchHeaderJacobyFlag_IsJacobyFalse()
    {
        // Jacoby is a money-game setting; match play ignores it.
        var ctx = BuildContext(matchLength: 7, jacoby: true, beaver: true, crawfordApplies: false);

        ctx.IsCrawford.Should().BeFalse();
        ctx.IsJacoby.Should().BeFalse();
        ctx.IsBeaver.Should().BeFalse();
        ctx.XgidCrawfordJacobyField.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    //  Money games — Jacoby / Beaver combinations
    // -----------------------------------------------------------------------

    [Fact]
    public void MoneyGame_Plain_AllFalse()
    {
        var ctx = BuildContext(matchLength: 0, jacoby: false, beaver: false, crawfordApplies: false);

        ctx.IsCrawford.Should().BeFalse();
        ctx.IsJacoby.Should().BeFalse();
        ctx.IsBeaver.Should().BeFalse();
        ctx.XgidCrawfordJacobyField.Should().Be(0);
    }

    /// <summary>
    /// The regression guard: money-game + Jacoby used to produce
    /// <c>CrawfordJacoby == 1</c>, and downstream consumers reading
    /// <c>CrawfordJacoby == 1</c> as IsCrawford tripped here. After the
    /// separation, IsCrawford is unconditionally false in money games.
    /// </summary>
    [Fact]
    public void MoneyGame_Jacoby_IsJacobyOnly_IsCrawfordFalse()
    {
        var ctx = BuildContext(matchLength: 0, jacoby: true, beaver: false, crawfordApplies: false);

        ctx.IsCrawford.Should().BeFalse();
        ctx.IsJacoby.Should().BeTrue();
        ctx.IsBeaver.Should().BeFalse();
        ctx.XgidCrawfordJacobyField.Should().Be(1);
    }

    [Fact]
    public void MoneyGame_Beaver_IsBeaverOnly_XgidFieldIs2()
    {
        var ctx = BuildContext(matchLength: 0, jacoby: false, beaver: true, crawfordApplies: false);

        ctx.IsCrawford.Should().BeFalse();
        ctx.IsJacoby.Should().BeFalse();
        ctx.IsBeaver.Should().BeTrue();
        ctx.XgidCrawfordJacobyField.Should().Be(2);
    }

    [Fact]
    public void MoneyGame_JacobyAndBeaver_XgidFieldIs3()
    {
        var ctx = BuildContext(matchLength: 0, jacoby: true, beaver: true, crawfordApplies: false);

        ctx.IsCrawford.Should().BeFalse();
        ctx.IsJacoby.Should().BeTrue();
        ctx.IsBeaver.Should().BeTrue();
        ctx.XgidCrawfordJacobyField.Should().Be(3);
    }

    /// <summary>
    /// Money games never have a Crawford game, even if a (malformed) game
    /// header claimed <c>CrawfordApplies</c>. Match-length 0 is the
    /// unconditional guard.
    /// </summary>
    [Fact]
    public void MoneyGame_WithSpuriousCrawfordApplies_IsCrawfordFalse()
    {
        var ctx = BuildContext(matchLength: 0, jacoby: true, beaver: false, crawfordApplies: true);

        ctx.IsCrawford.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    //  IsStandardStart — standard vs. non-standard opening positions
    // -----------------------------------------------------------------------

    /// <summary>
    /// A GameHeaderRecord whose InitialPosition is the canonical opening
    /// drives IsStandardStart to true. The flag is recomputed per-game from
    /// the header, so downstream consumers can gate move-number filtering
    /// on it.
    /// </summary>
    [Fact]
    public void StandardOpeningPosition_IsStandardStartTrue()
    {
        var hm = new MatchHeaderRecord { MatchLength = 7 };
        var gh = new GameHeaderRecord
        {
            InitialPosition = new PositionEngine
            {
                Points = (sbyte[])BackgammonConstants.StandardOpeningPosition.Clone(),
            },
        };

        var ctx = new MatchContext(new List<SaveRecord> { hm }, sourceFile: null, comments: []);
        ctx.Update(gh);

        ctx.IsStandardStart.Should().BeTrue();
    }

    /// <summary>
    /// A non-canonical InitialPosition (problem position, Bg960, mid-game
    /// snapshot, etc.) drives IsStandardStart to false. Uses the all-zeros
    /// default <see cref="PositionEngine"/> as a representative
    /// non-standard shape.
    /// </summary>
    [Fact]
    public void NonStandardOpeningPosition_IsStandardStartFalse()
    {
        var hm = new MatchHeaderRecord { MatchLength = 7 };
        var gh = new GameHeaderRecord(); // default InitialPosition is all-zero

        var ctx = new MatchContext(new List<SaveRecord> { hm }, sourceFile: null, comments: []);
        ctx.Update(gh);

        ctx.IsStandardStart.Should().BeFalse();
    }

    /// <summary>
    /// IsStandardStart is recomputed per-game: a standard-start game
    /// followed by a non-standard-start game flips the flag back to false.
    /// Pins the per-game reset semantic — without it, a stale true from
    /// game 1 would falsely admit move-number-filtered decisions in
    /// non-standard later games.
    /// </summary>
    [Fact]
    public void IsStandardStart_ResetPerGame()
    {
        var hm = new MatchHeaderRecord { MatchLength = 7 };
        var standardGh = new GameHeaderRecord
        {
            InitialPosition = new PositionEngine
            {
                Points = (sbyte[])BackgammonConstants.StandardOpeningPosition.Clone(),
            },
        };
        var nonStandardGh = new GameHeaderRecord();

        var ctx = new MatchContext(new List<SaveRecord> { hm }, sourceFile: null, comments: []);
        ctx.Update(standardGh);
        ctx.IsStandardStart.Should().BeTrue("standard-start game should set IsStandardStart=true");
        ctx.Update(nonStandardGh);
        ctx.IsStandardStart.Should().BeFalse("subsequent non-standard-start game must reset IsStandardStart=false");
    }

    // -----------------------------------------------------------------------
    //  Helper
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="MatchContext"/> from synthetic headers and drives
    /// it through a single <see cref="GameHeaderRecord"/> update so
    /// IsCrawford reflects the current game. Covers just the fields the
    /// Crawford/Jacoby/Beaver logic actually reads; other fields left default.
    /// </summary>
    private static MatchContext BuildContext(int matchLength, bool jacoby, bool beaver, bool crawfordApplies)
    {
        var hm = new MatchHeaderRecord
        {
            MatchLength = matchLength,
            Jacoby = jacoby,
            Beaver = beaver,
        };
        var gh = new GameHeaderRecord
        {
            CrawfordApplies = crawfordApplies,
        };

        var ctx = new MatchContext(new List<SaveRecord> { hm }, sourceFile: null, comments: []);
        ctx.Update(gh);
        return ctx;
    }
}
