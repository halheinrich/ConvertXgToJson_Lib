namespace ConvertXgToJson_Lib;

/// <summary>
/// Why the decision walk left something out of a decoded file: the kinds of
/// <see cref="XgOmission"/>, a refused game or a corrupt candidate. Each is a
/// problem in XG's data the walk recognizes, reported to the user (Hal's
/// ruling of 2026-11-01, halheinrich/backgammon#514: "any problems are to be
/// brought to the attention of the user"). A forced play is not among them:
/// it is left out of the walk too, but it is ordinary data, counted
/// (<see cref="XgIteratorState.ForcedPlayCount"/>) and never reported as a
/// problem. Nor is an illegal play, which the walk passes by in silence
/// (<see cref="XgDecisionIterator.CheckerPlayKind.IllegalPlay"/>).
/// </summary>
public enum XgOmissionKind
{
    /// <summary>
    /// A game whose header states a standing its session refuses, so no
    /// decision of the game has a trustworthy context: a match game in which
    /// a player needs 0 points or less, or more than the match's length, or
    /// a game flagged Crawford at a standing where not exactly one player is
    /// 1-away. The whole game is omitted; the omission names no move.
    /// </summary>
    InvalidStanding,

    /// <summary>
    /// A checker play holding a candidate invalid from its own position —
    /// a play the record would refuse (Hal's ruling of 2026-09-25 on invalid
    /// plays, applied to stored candidates), or a dance recorded where a
    /// legal play exists. The decision is omitted; the game goes on from the
    /// next record.
    /// </summary>
    CorruptCandidate,
}
