using BgDataTypes_Lib;

namespace ConvertXgToJson_Lib;

/// <summary>
/// One analysed candidate of a checker-play decision, as supplied to
/// <see cref="XgGameBuilder.Play(XgPlayer, DiceRoll, Play, IReadOnlyList{XgPlayCandidate})"/>:
/// the play, its equity from the mover's perspective, and the evaluation
/// depth it was analysed at. Immutable; an invalid depth is unrepresentable.
/// </summary>
public sealed record XgPlayCandidate
{
    /// <summary>The deepest N-ply evaluation XG's level taxonomy names.</summary>
    internal const int MaxPly = 7;

    private readonly int _ply;

    /// <summary>Creates a candidate analysed at <paramref name="ply"/> plies (default 1).</summary>
    /// <param name="play">See <see cref="Play"/>.</param>
    /// <param name="equity">See <see cref="Equity"/>.</param>
    /// <param name="ply">See <see cref="Ply"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ply"/> is outside 1–7.</exception>
    public XgPlayCandidate(Play play, double equity, int ply = 1)
    {
        Play = play;
        Equity = equity;
        Ply = ply;
    }

    /// <summary>
    /// The candidate play in the ecosystem's <see cref="BgDataTypes_Lib.Play"/>
    /// primitive: on-roll point numbering (the mover bears off past point 1),
    /// <c>FrPt</c> 25 for bar entry, <c>ToPt</c> 0 for a bear-off, negative
    /// <c>ToPt</c> for a hit. The builder validates the play against the
    /// position it is applied to (a checker must be on the from-point, the
    /// to-point must not be blocked, and the hit flag must agree with a blot
    /// being there); dice legality is not checked.
    /// </summary>
    public Play Play { get; init; }

    /// <summary>
    /// The candidate's equity from the mover's perspective. The iterator
    /// reports the highest-equity candidate as the best play, so the order
    /// candidates are given to the builder in does not matter.
    /// </summary>
    public double Equity { get; init; }

    /// <summary>Evaluation depth, 1–7 plies (XG's N-ply taxonomy).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set outside 1–7.</exception>
    public int Ply
    {
        get => _ply;
        init
        {
            if (value is < 1 or > MaxPly)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"An evaluation depth must be 1–{MaxPly} plies.");
            _ply = value;
        }
    }
}
