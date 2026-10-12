using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The post-yield stop callbacks (<see cref="XgIteratorCallbacks.StopGameAfter"/>,
/// <see cref="XgIteratorCallbacks.StopMatchAfter"/>) judge what the iterator
/// yields to its caller, after the <c>.xgp</c> single-decision selection, on
/// the caller's next <c>MoveNext</c> (halheinrich/backgammon#392). They exist
/// to save work, never to change the result: a stop vote on an <c>.xgp</c>'s
/// cube, run inside the walk, once ended the walk before the play was reached,
/// and the selection fell back to the cube.
///
/// <para>
/// Every case runs for both callbacks and both surfaces, through the public
/// entry points. Each callback here logs what it judges and votes to stop on
/// every decision, the vote that exposed the defect; the log interleaves
/// what the caller received with what the callbacks judged, so it pins the
/// timing as well as the content. The <c>.xg</c> cases pin that a game stop
/// still reaches back into the streaming walk. The files are synthesized
/// through the builders.
/// </para>
/// </summary>
public class XgDecisionIteratorStopCallbackTests
{
    private const string Xg = "synthetic.xg";
    private const string Xgp = "position.xgp";
    private static readonly DiceRoll ThreeOne = new(3, 1);
    private static readonly Play MakeFivePoint = Play.Create(new Move(8, 5), new Move(6, 5));
    private static readonly XgCubeEquities NoDouble = new(-0.2, -0.4, 1.0);

    /// <summary>The surface a case runs through.</summary>
    public enum Surface { Rows, Records }

    /// <summary>The stop callback a case supplies.</summary>
    public enum StopCallback { StopGameAfter, StopMatchAfter }

    /// <summary>The <c>.xgp</c> source a case reads.</summary>
    public enum XgpSource { CubeThenPlay, CubeOnly }

    // -----------------------------------------------------------------------
    //  Sources
    // -----------------------------------------------------------------------

    /// <summary>
    /// An <c>.xgp</c> holding an analysed cube pane and then the checker play:
    /// the selection's answer is the play.
    /// </summary>
    private static XgFile CubeThenPlay()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .CubeDecision(XgPlayer.Player1, NoDouble, doublerAction: CubeAction.NoDouble)
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        return builder.Build();
    }

    /// <summary>An <c>.xgp</c> holding only an analysed cube pane: the selection's answer is the cube.</summary>
    private static XgFile CubeOnly()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .CubeDecision(XgPlayer.Player1, NoDouble, doublerAction: CubeAction.NoDouble);
        return builder.Build();
    }

    private static XgFile Source(XgpSource source) => source switch
    {
        XgpSource.CubeThenPlay => CubeThenPlay(),
        XgpSource.CubeOnly => CubeOnly(),
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    private static DecisionKind Selected(XgpSource source) => source switch
    {
        XgpSource.CubeThenPlay => DecisionKind.CheckerPlay,
        XgpSource.CubeOnly => DecisionKind.Cube,
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    /// <summary>
    /// An <c>.xg</c> match of two games: game 1 holds two plays, game 2 one.
    /// </summary>
    private static XgFile TwoGames()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint)
            .Play(XgPlayer.Player2, ThreeOne, MakeFivePoint);
        builder.AddGame()
            .Play(XgPlayer.Player1, ThreeOne, MakeFivePoint);
        return builder.Build();
    }

    // -----------------------------------------------------------------------
    //  Harness
    // -----------------------------------------------------------------------

    /// <summary>Each decision the surface yields, as its kind, game and move number.</summary>
    private static IEnumerable<(DecisionKind Kind, int? Game, int? Move)> Decisions(
        Surface surface, XgFile file, string sourceFile, XgIteratorCallbacks? callbacks) => surface switch
    {
        Surface.Rows => XgDecisionIterator.Iterate(file, sourceFile, callbacks: callbacks)
            .Select(r => (r.Kind, r.Game, r.MoveNumber)),
        Surface.Records => XgDecisionIterator.IterateDiagramRequests(file, sourceFile, callbacks: callbacks)
            .Select(r => (r.Kind, r.Game, r.MoveNumber)),
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    /// <summary>
    /// Callbacks supplying only <paramref name="which"/>, which logs each
    /// decision it judges and answers <paramref name="vote"/>.
    /// </summary>
    private static XgIteratorCallbacks Judging(StopCallback which, List<string> log, Func<bool> vote)
    {
        bool Judge(IDecisionFilterData d)
        {
            log.Add($"judged {d.Kind}");
            return vote();
        }

        return which switch
        {
            StopCallback.StopGameAfter => new XgIteratorCallbacks(StopGameAfter: Judge),
            StopCallback.StopMatchAfter => new XgIteratorCallbacks(StopMatchAfter: Judge),
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };
    }

    /// <summary>Enumerates to the end, logging each decision as the caller receives it.</summary>
    private static List<(DecisionKind Kind, int? Game, int? Move)> Drain(
        IEnumerable<(DecisionKind Kind, int? Game, int? Move)> decisions, List<string> log)
    {
        var received = new List<(DecisionKind Kind, int? Game, int? Move)>();
        foreach (var decision in decisions)
        {
            log.Add($"yielded {decision.Kind}");
            received.Add(decision);
        }
        return received;
    }

    // -----------------------------------------------------------------------
    //  .xgp: the selection is made before any callback judges
    // -----------------------------------------------------------------------

    /// <summary>
    /// A cube before the play: a stop vote on every decision still yields the
    /// play, and the callback judges only the play, after the caller received
    /// it — never the cube the selection discards.
    /// </summary>
    [Theory]
    [InlineData(Surface.Rows, StopCallback.StopGameAfter)]
    [InlineData(Surface.Rows, StopCallback.StopMatchAfter)]
    [InlineData(Surface.Records, StopCallback.StopGameAfter)]
    [InlineData(Surface.Records, StopCallback.StopMatchAfter)]
    public void Xgp_CubeBeforePlay_YieldsThePlay_AndNoCallbackSeesTheDiscardedCube(Surface surface, StopCallback which)
    {
        var log = new List<string>();

        var received = Drain(Decisions(surface, CubeThenPlay(), Xgp, Judging(which, log, () => true)), log);

        received.Should().ContainSingle().Which.Kind.Should().Be(DecisionKind.CheckerPlay);
        log.Should().Equal("yielded CheckerPlay", "judged CheckerPlay");
    }

    /// <summary>
    /// Only a cube: it is yielded, and it reaches the callback after the
    /// caller received it. This is what keeps "no callback for an
    /// <c>.xgp</c>" from passing as the fix.
    /// </summary>
    [Theory]
    [InlineData(Surface.Rows, StopCallback.StopGameAfter)]
    [InlineData(Surface.Rows, StopCallback.StopMatchAfter)]
    [InlineData(Surface.Records, StopCallback.StopGameAfter)]
    [InlineData(Surface.Records, StopCallback.StopMatchAfter)]
    public void Xgp_CubeOnly_YieldsTheCube_ThenTheCallbackJudgesIt(Surface surface, StopCallback which)
    {
        var log = new List<string>();

        var received = Drain(Decisions(surface, CubeOnly(), Xgp, Judging(which, log, () => true)), log);

        received.Should().ContainSingle().Which.Kind.Should().Be(DecisionKind.Cube);
        log.Should().Equal("yielded Cube", "judged Cube");
    }

    /// <summary>
    /// A caller that stops after its first result triggers no callback: the
    /// callbacks run on the caller's next <c>MoveNext</c>, which never comes.
    /// For the cube before the play, the selection has walked past the cube
    /// to reach that first result, and the callback still judges nothing.
    /// </summary>
    [Theory]
    [InlineData(Surface.Rows, StopCallback.StopGameAfter, XgpSource.CubeThenPlay)]
    [InlineData(Surface.Rows, StopCallback.StopMatchAfter, XgpSource.CubeThenPlay)]
    [InlineData(Surface.Records, StopCallback.StopGameAfter, XgpSource.CubeThenPlay)]
    [InlineData(Surface.Records, StopCallback.StopMatchAfter, XgpSource.CubeThenPlay)]
    [InlineData(Surface.Rows, StopCallback.StopGameAfter, XgpSource.CubeOnly)]
    [InlineData(Surface.Rows, StopCallback.StopMatchAfter, XgpSource.CubeOnly)]
    [InlineData(Surface.Records, StopCallback.StopGameAfter, XgpSource.CubeOnly)]
    [InlineData(Surface.Records, StopCallback.StopMatchAfter, XgpSource.CubeOnly)]
    public void Xgp_ACallerThatStopsAfterItsFirstResult_TriggersNoCallback(
        Surface surface, StopCallback which, XgpSource source)
    {
        var log = new List<string>();

        var first = Decisions(surface, Source(source), Xgp, Judging(which, log, () => true)).First();

        first.Kind.Should().Be(Selected(source));
        log.Should().BeEmpty();
    }

    /// <summary>
    /// The control: with no callbacks the selection is the play over the
    /// cube, or the cube alone — and the callbacks voting stop on every
    /// decision leave that selection as it is.
    /// </summary>
    [Theory]
    [InlineData(Surface.Rows, StopCallback.StopGameAfter, XgpSource.CubeThenPlay)]
    [InlineData(Surface.Rows, StopCallback.StopMatchAfter, XgpSource.CubeThenPlay)]
    [InlineData(Surface.Records, StopCallback.StopGameAfter, XgpSource.CubeThenPlay)]
    [InlineData(Surface.Records, StopCallback.StopMatchAfter, XgpSource.CubeThenPlay)]
    [InlineData(Surface.Rows, StopCallback.StopGameAfter, XgpSource.CubeOnly)]
    [InlineData(Surface.Rows, StopCallback.StopMatchAfter, XgpSource.CubeOnly)]
    [InlineData(Surface.Records, StopCallback.StopGameAfter, XgpSource.CubeOnly)]
    [InlineData(Surface.Records, StopCallback.StopMatchAfter, XgpSource.CubeOnly)]
    public void Xgp_WithoutCallbacks_TheSameSelectionResults(Surface surface, StopCallback which, XgpSource source)
    {
        var file = Source(source);

        var withoutCallbacks = Decisions(surface, file, Xgp, callbacks: null).ToList();
        var withCallbacks = Decisions(surface, file, Xgp, Judging(which, [], () => true)).ToList();

        withoutCallbacks.Should().ContainSingle().Which.Kind.Should().Be(Selected(source));
        withCallbacks.Should().Equal(withoutCallbacks);
    }

    /// <summary>
    /// A game stop raised on an <c>.xgp</c>'s play is never taken — the
    /// selection ends without resuming the walk — and it must not carry into
    /// a second enumeration of the same sequence, where it would skip the
    /// play and leave the cube selected. Each enumeration has its own request.
    /// </summary>
    [Theory]
    [InlineData(Surface.Rows)]
    [InlineData(Surface.Records)]
    public void Xgp_TheSameSequenceEnumeratedTwice_SelectsThePlayBothTimes(Surface surface)
    {
        var decisions = Decisions(surface, CubeThenPlay(), Xgp, Judging(StopCallback.StopGameAfter, [], () => true));

        var first = decisions.ToList();
        var second = decisions.ToList();

        first.Should().ContainSingle().Which.Kind.Should().Be(DecisionKind.CheckerPlay);
        second.Should().Equal(first);
    }

    // -----------------------------------------------------------------------
    //  .xg: streaming, and a stop reaches back into the walk
    // -----------------------------------------------------------------------

    /// <summary>
    /// A game stop voted on the first decision skips the rest of game 1, and
    /// game 2 still emits; each decision is judged right after the caller
    /// received it, so the walk streams.
    /// </summary>
    [Theory]
    [InlineData(Surface.Rows)]
    [InlineData(Surface.Records)]
    public void Xg_StopGameAfter_SkipsTheRestOfThatGame_AndTheNextGameStillEmits(Surface surface)
    {
        var file = TwoGames();
        var log = new List<string>();
        int judged = 0;

        var received = Drain(Decisions(surface, file, Xg,
            Judging(StopCallback.StopGameAfter, log, () => ++judged == 1)), log);

        Decisions(surface, file, Xg, callbacks: null).Select(d => (d.Game, d.Move))
            .Should().Equal([(1, 1), (1, 2), (2, 1)], "the control: without the stop, game 1 holds two decisions");
        received.Select(d => (d.Game, d.Move)).Should().Equal([(1, 1), (2, 1)]);
        log.Should().Equal("yielded CheckerPlay", "judged CheckerPlay", "yielded CheckerPlay", "judged CheckerPlay");
    }

    /// <summary>A match stop voted on the first decision ends the match there.</summary>
    [Theory]
    [InlineData(Surface.Rows)]
    [InlineData(Surface.Records)]
    public void Xg_StopMatchAfter_EndsTheMatchAfterTheJudgedDecision(Surface surface)
    {
        var log = new List<string>();

        var received = Drain(Decisions(surface, TwoGames(), Xg,
            Judging(StopCallback.StopMatchAfter, log, () => true)), log);

        received.Select(d => (d.Game, d.Move)).Should().Equal([(1, 1)]);
        log.Should().Equal("yielded CheckerPlay", "judged CheckerPlay");
    }
}
