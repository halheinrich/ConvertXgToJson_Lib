using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// The typed depth facts of one XG analysis — what a record stores for a
/// candidate's or a cube analysis's depth (<see cref="PlayCandidate"/>,
/// <see cref="CubeDecisionData"/>): the <see cref="AnalysisMode"/> ×
/// <see cref="AnalysisLevel"/> pair, the rollout trial count, the opening-book
/// edition, and the raw code of a level this producer does not recognize.
/// </summary>
/// <remarks>
/// <para>
/// <b>Facts, never their spellings</b> (the arc's rule, applied to depth:
/// halheinrich/backgammon#273). The label, the abbreviation and the rank
/// are determined by these facts, so BgDataTypes_Lib derives them
/// (<see cref="PlayCandidate.Depth"/>, <see cref="PlayCandidate.DepthAbbreviation"/>,
/// <see cref="PlayCandidate.DepthRank"/>) and this producer states only what
/// XG's analysis records. What stays here is the one thing only a reader of
/// XG's files knows: how XG's PLAYERLEVEL codes, rollout contexts and book
/// stamps map onto the facts.
/// </para>
/// <para>
/// <b>The code table</b> (<see cref="OfLevel"/>): an evaluation code is
/// <see cref="AnalysisMode.Evaluation"/> at its level — XG's code for an
/// N-ply evaluation is N − 1, codes 1 and 11 both 2-ply (code 11 identified by
/// XG's own display, halheinrich/backgammon#160), 12 is 3-ply Red, 1000–1002
/// the XG Roller family; 998 and 999 are book hits
/// (<see cref="AnalysisMode.BookRollout"/>), the V2 and V1 editions
/// respectively — the lower code is the newer book; 100 is a rollout with no
/// recorded parameters; any other code is unrecognized and stated as its raw
/// code.
/// </para>
/// <para>
/// <b>A rollout</b> (<see cref="Resolve"/>) is named by its inner evaluation
/// level — the first phase's (<c>Level1</c>) when a first phase exists, which
/// <c>LevelCut</c> above 0 says, otherwise the second phase's (<c>Level2</c>),
/// which then plays throughout (the user's ruling on
/// halheinrich/backgammon#251: the first phase is the strength the user set).
/// Phase existence is read from <c>LevelCut</c>, never from a level: level 0
/// is 1-ply, not "unset". <c>LevelTrunc</c> belongs to truncation, not to a
/// phase, and is not a depth input. An inner code that is not an evaluation
/// level is stated as its raw code.
/// </para>
/// <para>
/// <b>A book hit enriched by the opening book</b> (<see cref="Resolve"/>'s
/// <c>bookEntry</c>) states the entry's rollout moves level and trial count
/// beside its edition. Only a rollout entry enriches: the book's Roller++
/// evaluation baselines store zeroed rollout parameters.
/// </para>
/// <para>
/// A trial count XG records as 0 is none recorded: a record's trial count is
/// a number of games rolled, at least 1.
/// </para>
/// </remarks>
internal readonly record struct XgDepthFacts(
    AnalysisMode Mode,
    AnalysisLevel Level,
    int? RolloutTrials = null,
    BookEdition? BookEdition = null,
    int? UnrecognizedLevelCode = null)
{
    /// <summary>No depth recorded: the mode and the level unknown, no raw code.</summary>
    internal static XgDepthFacts NotRecorded { get; } = new(AnalysisMode.Unknown, AnalysisLevel.Unknown);

    /// <summary>XG's rollout sentinel level code: a rollout, its parameters in a rollout context.</summary>
    internal const int RolloutCode = 100;

    /// <summary>XG's level code for a V2-book hit — the edition whose database this library reads.</summary>
    internal const int BookV2Code = 998;

    /// <summary>XG's level code for a V1-book hit.</summary>
    internal const int BookV1Code = 999;

    /// <summary>
    /// The facts XG's level <paramref name="code"/> states on its own — the
    /// one decoding of XG's PLAYERLEVEL code space (see the remarks).
    /// </summary>
    internal static XgDepthFacts OfLevel(int code) => code switch
    {
        // Arms read in ascending rigor — XG's menu order, not XG's code order.
        0 => Evaluation(AnalysisLevel.Ply1),
        1 or 11 => Evaluation(AnalysisLevel.Ply2),
        12 => Evaluation(AnalysisLevel.Ply3Red),
        2 => Evaluation(AnalysisLevel.Ply3),
        1000 => Evaluation(AnalysisLevel.XgRoller),
        3 => Evaluation(AnalysisLevel.Ply4),
        1001 => Evaluation(AnalysisLevel.XgRollerPlus),
        4 => Evaluation(AnalysisLevel.Ply5),
        5 => Evaluation(AnalysisLevel.Ply6),
        6 => Evaluation(AnalysisLevel.Ply7),
        1002 => Evaluation(AnalysisLevel.XgRollerPlusPlus),
        BookV2Code => new(AnalysisMode.BookRollout, AnalysisLevel.Unknown, BookEdition: BgDataTypes_Lib.BookEdition.V2),
        BookV1Code => new(AnalysisMode.BookRollout, AnalysisLevel.Unknown, BookEdition: BgDataTypes_Lib.BookEdition.V1),
        RolloutCode => new(AnalysisMode.Rollout, AnalysisLevel.Unknown),
        _ => new(AnalysisMode.Unknown, AnalysisLevel.Unknown, UnrecognizedLevelCode: code),
    };

    /// <summary>
    /// The facts of one candidate's or cube analysis's depth: the rollout
    /// behind it when <paramref name="rolloutIndex"/> names one of
    /// <paramref name="rollouts"/>; else the book rollout behind a book hit
    /// when <paramref name="bookEntry"/> is a rollout entry; else what
    /// <paramref name="evalLevel"/> states (see the remarks).
    /// </summary>
    /// <param name="evalLevel">The analysis's XG level code.</param>
    /// <param name="rolloutIndex">The analysis's index into <paramref name="rollouts"/>, or −1 for none.</param>
    /// <param name="rollouts">The file's rollout contexts.</param>
    /// <param name="bookEntry">The book entry resolved for a book-stamped candidate, or <see langword="null"/>.</param>
    internal static XgDepthFacts Resolve(
        int evalLevel, int rolloutIndex, IReadOnlyList<RolloutContext> rollouts, OpeningBookEntry? bookEntry = null)
    {
        if (rolloutIndex >= 0 && rolloutIndex < rollouts.Count)
        {
            var rollout = rollouts[rolloutIndex];
            var inner = Inner(rollout.LevelCut > 0 ? rollout.Level1 : rollout.Level2);
            return new(AnalysisMode.Rollout, inner.Level, Trials(rollout.GamesRolled),
                UnrecognizedLevelCode: inner.UnrecognizedLevelCode);
        }

        var stated = OfLevel(evalLevel);
        if (stated.Mode == AnalysisMode.BookRollout && bookEntry is { IsRollout: true })
        {
            var inner = Inner(bookEntry.RolloutMovesLevel);
            return stated with
            {
                Level = inner.Level,
                RolloutTrials = Trials(bookEntry.Trials),
                UnrecognizedLevelCode = inner.UnrecognizedLevelCode,
            };
        }

        return stated;
    }

    private static XgDepthFacts Evaluation(AnalysisLevel level) => new(AnalysisMode.Evaluation, level);

    /// <summary>
    /// A rollout's inner level: an evaluation level, or — for any code that
    /// is not one — <see cref="AnalysisLevel.Unknown"/> with its raw code.
    /// </summary>
    private static (AnalysisLevel Level, int? UnrecognizedLevelCode) Inner(int code)
    {
        var facts = OfLevel(code);
        return facts.Mode == AnalysisMode.Evaluation
            ? (facts.Level, null)
            : (AnalysisLevel.Unknown, code);
    }

    /// <summary>XG's trial count as a record states it: none for 0 (or less).</summary>
    private static int? Trials(int games) => games >= 1 ? games : null;
}
