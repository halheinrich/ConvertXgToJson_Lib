namespace ConvertXgToJson_Lib;

/// <summary>
/// Optional producer configuration supplied by callers of
/// <see cref="XgDecisionIterator.Iterate"/> and
/// <see cref="XgDecisionIterator.IterateDiagramRequests"/> — the third leg of
/// the iterator's parameter pattern: <see cref="XgIteratorState"/> observes,
/// <see cref="XgIteratorCallbacks"/> controls iteration, and this record
/// configures how rows are built. Null (or a member left null) means "default
/// behaviour"; members are resources the caller loads and owns, not
/// per-decision knobs.
/// </summary>
/// <param name="OpeningBook">
/// A loaded opening-book database (<see cref="ConvertXgToJson_Lib.OpeningBook"/>)
/// used to enrich book-stamped candidates: XG stamps a book-analysed
/// candidate with a bare level code (998, Book V2) and no parameters, and the
/// book database holds the cached rollout behind it. When supplied, each
/// V2-book-stamped checker-play candidate is resolved by its resulting
/// position + decision context and stamped with the entry's rollout moves
/// level and trial count; when null (or on a lookup miss, a V1 stamp, or a
/// cube row — the cube keying convention is unproven), book hits degrade to
/// <see cref="BgDataTypes_Lib.AnalysisMode.BookRollout"/> +
/// <see cref="BgDataTypes_Lib.AnalysisLevel.Unknown"/> with the plain
/// "Book V1" / "Book V2" labels. Locating the database on disk is the
/// application's concern; this library takes the loaded instance.
/// </param>
public sealed record XgIteratorOptions(
    OpeningBook? OpeningBook = null);
