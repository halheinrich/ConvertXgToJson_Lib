using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// One entry of XG's opening-book database (<c>OpeningBookV2.ob</c>): the
/// stored analysis of a single opening-line position under a single game
/// context (money rules or a specific match score). XG renders exactly this
/// data in the tooltip it shows for a book-analysed candidate.
///
/// <para>
/// <b>Perspectives.</b> <see cref="Position"/> is the position <i>resulting</i>
/// from a candidate play, expressed from the perspective of the player who is
/// on roll in it (positive checkers). The player who just made the play — the
/// on-roll player's opponent — is the perspective of <see cref="Evaluation"/>:
/// its win/lose probabilities and equity are theirs, matching how XG surfaces
/// a candidate's eval vector in a <c>.xg</c> analysis pane (for a book hit XG
/// copies this vector into the pane verbatim). <see cref="OnRollAway"/> /
/// <see cref="OpponentAway"/> follow the same frame: "on roll" is the stored
/// position's on-roll player, "opponent" the player who made the play.
/// </para>
///
/// <para>
/// <b>Two entry populations.</b> Rollout entries (<see cref="Level"/> 100)
/// carry the rollout parameters (<see cref="Trials"/>,
/// <see cref="RolloutMovesLevel"/> / <see cref="RolloutCubeLevel"/>,
/// <see cref="Seed"/>, <see cref="EquityStandardDeviation"/>,
/// <see cref="Duration"/>). Evaluation entries (<see cref="Level"/> 1002,
/// XG Roller++) have zeros there — <see cref="Trials"/> 0 marks them.
/// </para>
/// </summary>
internal sealed class OpeningBookEntry
{
    /// <summary>Name of the contributor who rolled out or evaluated the
    /// position (e.g. "Neil Kazaross"; "GameSite 2000, Ltd" is XG itself).</summary>
    public string Contributor { get; init; } = string.Empty;

    /// <summary>
    /// The keyed position: the position resulting from the candidate play,
    /// from the perspective of the player on roll in it (positive = on-roll
    /// player's checkers — the standard <see cref="PositionEngine"/> layout).
    /// </summary>
    public PositionEngine Position { get; init; } = new();

    /// <summary>Cube value of the entry's context. 1 (centred, never turned)
    /// for all but a handful of entries.</summary>
    public int CubeValue { get; init; }

    /// <summary>
    /// Cube-owner field as stored: 0 = centred, ±1 = owned. The sign's player
    /// mapping is unverified — only 22 of 53,210 entries in the shipped
    /// database carry a turned cube, none reachable from a tooltip oracle.
    /// </summary>
    public int CubeOwnerSign { get; init; }

    /// <summary>True when the entry's context is a money session
    /// (both away fields stored as −1).</summary>
    public bool IsMoneySession => OnRollAway < 0;

    /// <summary>Away score (points still needed) of the stored position's
    /// on-roll player; −1 for money entries.</summary>
    public int OnRollAway { get; init; }

    /// <summary>Away score of the on-roll player's opponent — the player who
    /// made the play the entry analyses; −1 for money entries.</summary>
    public int OpponentAway { get; init; }

    /// <summary>Jacoby rule flag. Set only on money entries.</summary>
    public bool Jacoby { get; init; }

    /// <summary>
    /// Beaver rule flag. Meaningful for money entries; a minority of match
    /// entries also carry it (a contributor's money-rollout setting riding
    /// along), which is why it is entry data but not part of the lookup key.
    /// </summary>
    public bool Beaver { get; init; }

    /// <summary>Crawford-game flag. Set only on match entries where one
    /// player is 1-away.</summary>
    public bool Crawford { get; init; }

    /// <summary>
    /// The stored seven-float evaluation vector, from the perspective of the
    /// player who made the play. Unlike a cube pane's cubeless
    /// <see cref="EvalResult.Equity"/>, the equity slot here is the
    /// <b>cubeful, context-adjusted</b> candidate equity — the number XG
    /// displays in the candidate list for a book hit (match entries carry the
    /// score-adjusted normalized equity, which is why the same position shows
    /// very different equities under different away scores). The cubeless
    /// equity XG's tooltip shows is derived from the probability slots:
    /// (win − lose) + (winG − loseG) + (winBG − loseBG).
    /// </summary>
    public EvalResult Evaluation { get; init; } = new();

    /// <summary>
    /// Analysis level of the entry itself, in the XG level-code domain shared
    /// with <see cref="BestMoveAnalysis.Level"/>: 100 = rollout,
    /// 1002 = XG Roller++ evaluation (rare ply codes also occur).
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// True for rollout entries (<see cref="Level"/> 100) — the population
    /// whose rollout parameters (<see cref="Trials"/>,
    /// <see cref="RolloutMovesLevel"/> / <see cref="RolloutCubeLevel"/>, …)
    /// are real. Evaluation entries store zeros there, so consumers reading
    /// those fields must gate on this first (the depth-enrichment path does).
    /// </summary>
    public bool IsRollout => Level == 100;

    /// <summary>Number of games rolled out; 0 for evaluation entries.</summary>
    public int Trials { get; init; }

    /// <summary>
    /// Per-game standard deviation of the rollout equity as stored;
    /// 0 for evaluation entries. XG's displayed "±" confidence is derived —
    /// see <see cref="ConfidenceInterval95"/>.
    /// </summary>
    public float EquityStandardDeviation { get; init; }

    /// <summary>
    /// The 95% confidence half-interval XG's tooltip displays as "±":
    /// 1.96 · <see cref="EquityStandardDeviation"/> / √<see cref="Trials"/>.
    /// Null for evaluation entries (no trials).
    /// </summary>
    public double? ConfidenceInterval95 =>
        Trials > 0 ? 1.96 * EquityStandardDeviation / Math.Sqrt(Trials) : null;

    /// <summary>Checker-play level used inside the rollout, in the XG
    /// level-code domain (e.g. 3 = 4-ply, 1000 = XG Roller);
    /// 0 for evaluation entries.</summary>
    public int RolloutMovesLevel { get; init; }

    /// <summary>Cube-decision level used inside the rollout, in the XG
    /// level-code domain; 0 for evaluation entries.</summary>
    public int RolloutCubeLevel { get; init; }

    /// <summary>Dice seed of the rollout; 0 for evaluation entries.</summary>
    public int Seed { get; init; }

    /// <summary>Major part of the XG engine version that produced the entry
    /// (tooltip "XG 2.00" ⇒ major 2, minor 0).</summary>
    public int EngineVersionMajor { get; init; }

    /// <summary>Minor part of the XG engine version that produced the entry.</summary>
    public int EngineVersionMinor { get; init; }

    /// <summary>Wall-clock duration of the rollout as stored (a seconds
    /// scalar); zero for evaluation entries.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>When the entry was added to the book (observed: shared batch
    /// timestamps across entries imported together).</summary>
    public DateTime AddedOn { get; init; }

    /// <summary>When the rollout or evaluation was performed — the date XG's
    /// tooltip displays.</summary>
    public DateTime AnalyzedOn { get; init; }
}
