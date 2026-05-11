using BgDataTypes_Lib;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Optional skip predicates supplied by callers of
/// <see cref="XgDecisionIterator.Iterate"/> and
/// <see cref="XgDecisionIterator.IterateDiagramRequests"/>. The producer
/// evaluates each predicate at the indicated boundary and short-circuits
/// internal iteration when the predicate returns <c>true</c>.
///
/// <para>
/// Each predicate is null by default; a null predicate is treated as
/// "never skip / never stop." All four are mutually independent.
/// </para>
///
/// <para>
/// This type replaces the imperative <c>AdvanceNextMatch</c> /
/// <c>AdvanceNextGame</c> field setters that previously lived on
/// <see cref="XgIteratorState"/>. Skip semantics belong to the caller's
/// filter / consumer concern, not the producer's iteration state; moving
/// them into a single pre-registered predicate record keeps
/// <see cref="XgIteratorState"/> a pure read-only observer.
/// </para>
/// </summary>
/// <param name="SkipMatchAt">Evaluated once per match at the match header,
///   before any rows are yielded. Return <c>true</c> to skip the entire
///   match. Use case: filter-driven prune (e.g., player-name mismatch,
///   money/tournament mode mismatch, match length insufficient for the
///   filter's sought scores).</param>
/// <param name="SkipGameAt">Evaluated once per game at the game header,
///   before any rows are yielded from the game. Return <c>true</c> to
///   skip the rest of the current game. Use case: game's entry-state
///   (away scores, Crawford, non-standard start) does not match the
///   filter's sought target.</param>
/// <param name="StopGameAfter">Evaluated after each yielded decision.
///   Return <c>true</c> to advance to the next game without yielding any
///   more decisions from the current game. Use case: per-decision
///   early-exit (e.g., move-number cutoff reached).
///   <para>Timing: the predicate fires on the producer's next
///   <c>MoveNext</c> call, so the consumer sees the just-yielded row
///   before the skip takes effect. To suppress the row itself, return
///   <c>true</c> from <see cref="SkipGameAt"/> at the game boundary
///   instead.</para></param>
/// <param name="StopMatchAfter">Evaluated after each yielded decision.
///   Return <c>true</c> to advance to the next match without yielding any
///   more decisions from the current match. Use case: per-decision
///   early-exit at match scope (e.g., "one decision per match" filter
///   mode).
///   <para>Timing: same as <see cref="StopGameAfter"/> — the predicate
///   fires on the producer's next <c>MoveNext</c>; the consumer sees the
///   just-yielded row before the skip takes effect. To suppress the
///   match's rows entirely, return <c>true</c> from
///   <see cref="SkipMatchAt"/> at the match boundary instead.</para></param>
public sealed record XgIteratorCallbacks(
    Func<XgMatchInfo, bool>? SkipMatchAt = null,
    Func<XgGameInfo, bool>? SkipGameAt = null,
    Func<IDecisionFilterData, bool>? StopGameAfter = null,
    Func<IDecisionFilterData, bool>? StopMatchAfter = null);
