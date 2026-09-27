using BgDataTypes_Lib;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Optional producer configuration supplied by callers of
/// <see cref="XgDecisionIterator.Iterate"/> and
/// <see cref="XgDecisionIterator.IterateDiagramRequests"/> — the third leg of
/// the iterator's parameter pattern: <see cref="XgIteratorState"/> observes,
/// <see cref="XgIteratorCallbacks"/> controls iteration, and this record
/// configures how rows are built. A null options object, or a member left at
/// its default, means "default behaviour". Every member is configuration for
/// a whole walk — a resource the caller loads, or a choice the caller makes —
/// never a per-decision knob.
/// </summary>
/// <param name="OpeningBook">
/// A loaded opening-book database (<see cref="ConvertXgToJson_Lib.OpeningBook"/>)
/// used to enrich book-stamped candidates: XG stamps a book-analysed
/// candidate with a bare level code (998, Book V2) and no parameters, and the
/// book database holds the cached rollout behind it. When supplied, each
/// V2-book-stamped checker-play candidate is resolved by its resulting
/// position + decision context and stamped with the entry's rollout moves
/// level and trial count; when null (or on a lookup miss, a V1 stamp, or a
/// cube decision — the cube keying convention is unproven), a book hit states
/// <see cref="AnalysisMode.BookRollout"/> +
/// <see cref="AnalysisLevel.Unknown"/> and its edition alone. Locating the
/// database on disk is the application's concern; this library takes the
/// loaded instance.
/// </param>
/// <param name="Ranking">
/// The ranking of a checker play's candidates the walk judges decisions under
/// (SPEC-scoring §2a): which play is best, each play's error and whether it
/// is scored. <see cref="XgDecisionIterator.Iterate"/> builds every row for
/// it (<see cref="DecisionRow.From"/>), and the post-yield callbacks of both
/// surfaces see each decision through a view built for it
/// (<see cref="BgDecisionData.ViewFor"/>). A record itself does not depend on
/// the ranking. <see cref="PlayRanking.Equity"/>, the default: an application
/// without the setting uses it.
/// </param>
public sealed record XgIteratorOptions(
    OpeningBook? OpeningBook = null,
    PlayRanking Ranking = PlayRanking.Equity)
{
    /// <summary>The ranking the walk judges decisions under; see the record's parameter of the same name.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown on construction or init when the value is not a defined ranking.</exception>
    public PlayRanking Ranking { get; init => field = Defined(value); } = Defined(Ranking);

    /// <summary>The options of a walk given none: no opening book, the default ranking.</summary>
    internal static XgIteratorOptions Default { get; } = new();

    private static PlayRanking Defined(PlayRanking ranking) =>
        Enum.IsDefined(ranking)
            ? ranking
            : throw new ArgumentOutOfRangeException(nameof(Ranking), ranking, "The ranking is Equity or DepthFirst.");
}
