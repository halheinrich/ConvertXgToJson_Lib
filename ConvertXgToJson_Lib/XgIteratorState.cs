using System.Collections.ObjectModel;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Read-only observer of producer-internal iteration state. Populated by
/// <see cref="XgDecisionIterator"/> as iteration progresses; the caller may
/// inspect <see cref="MatchInfo"/> and <see cref="GameInfo"/> for per-row
/// context, and <see cref="Omissions"/> and <see cref="ForcedPlayCount"/>
/// for what the walk left out of the file and why
/// (halheinrich/backgammon#514).
///
/// <para>
/// Iteration control — skipping matches, games, or stopping early — is
/// supplied separately as predicates on <see cref="XgIteratorCallbacks"/>.
/// This type carries no caller-mutable surface.
/// </para>
///
/// <para>
/// <b>One instance serves any number of walks, and each walk starts it
/// afresh.</b> Every member is reset when an enumeration begins — the
/// iterator's at its first <c>MoveNext</c>,
/// <see cref="XgFileReader.ReadGameHeaders"/>'s likewise — so nothing
/// recorded in one file's walk, or one enumeration of the same file,
/// carries into the next: a consumer that holds one state across a
/// directory reads each file's omissions and forced count while, or just
/// after, it enumerates that file. The reset replaces
/// <see cref="Omissions"/> rather than clearing it, so a list a caller kept
/// from an earlier walk is left as that walk recorded it. Reuse is not
/// refused: that is the contract of XgFilter_Lib's <c>SourceReport</c>,
/// which records a whole walk over many sources; this observer records one
/// file at a time.
/// </para>
///
/// <para>
/// <b>Truthful about the walk.</b> The omissions and the count are what the
/// walk examined. A game the caller's <see cref="XgIteratorCallbacks.SkipGameAt"/>
/// skipped, a game the walk never reached because the caller stopped, and
/// the rest of a game after <see cref="XgIteratorCallbacks.StopGameAfter"/>
/// are not examined, so nothing here says they are clean.
/// </para>
/// </summary>
public sealed class XgIteratorState
{
    private List<XgOmission> _omissions = [];

    /// <summary>
    /// Populated by the iterator from <see cref="Models.MatchHeaderRecord"/> before
    /// any rows are yielded from the match. Reset (repopulated) at the start
    /// of each new .xg file.
    /// </summary>
    public XgMatchInfo? MatchInfo { get; internal set; }

    /// <summary>
    /// Populated by the iterator from <see cref="Models.GameHeaderRecord"/> before
    /// any rows are yielded from the game. Reset to null at the start of each
    /// new .xg file and repopulated at each new game. <see langword="null"/>
    /// while the walk is in a game it omitted
    /// (<see cref="XgOmissionKind.InvalidStanding"/>): a refused header
    /// leaves no usable standing, and the game before it is not left in
    /// place.
    /// </summary>
    public XgGameInfo? GameInfo { get; internal set; }

    /// <summary>
    /// What the walk has left out of the current file so far, in the order
    /// found — each omitted game and each omitted checker play, with its
    /// kind and the refusing rule's complete message as the reason
    /// (<see cref="XgOmission"/>; an omitted play names its roll where the
    /// record states one). Complete once the
    /// enumeration has ended; empty when the walk found nothing to omit.
    /// Each omission is also logged, at <c>Warning</c>, through the logger
    /// the iterator was given.
    /// </summary>
    public IReadOnlyList<XgOmission> Omissions { get; private set; } = ReadOnlyCollection<XgOmission>.Empty;

    /// <summary>
    /// How many analysed checker plays of the current file the walk has
    /// passed by as forced — positions with exactly one legal play, a dance
    /// included — so far. Information, not a problem: a forced play is no
    /// decision (Hal's ruling of 2026-11-01, halheinrich/backgammon#514), so
    /// the walk yields none and reports none as an omission. A caller that
    /// shows "N forced plays were set aside" reads it here.
    /// </summary>
    public int ForcedPlayCount { get; private set; }

    /// <summary>
    /// Starts a walk afresh: clears every member, so nothing of an earlier
    /// walk carries into this one. Called by each producer operation that
    /// takes a state, at the start of its enumeration.
    /// </summary>
    internal void Reset()
    {
        MatchInfo = null;
        GameInfo = null;
        _omissions = [];
        Omissions = _omissions.AsReadOnly();
        ForcedPlayCount = 0;
    }

    /// <summary>Records an omission the walk found, in walk order.</summary>
    internal void Omit(XgOmission omission) => _omissions.Add(omission);

    /// <summary>Counts a forced play the walk passed by.</summary>
    internal void CountForcedPlay() => ForcedPlayCount++;
}
