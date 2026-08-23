namespace ConvertXgToJson_Lib;

/// <summary>
/// The three cubeful equities of an analysed cube decision, from the
/// doubler's perspective — what <see cref="XgGameBuilder.CubeDecision"/>
/// takes to describe an analysed cube action. The proper actions and the
/// played actions' errors are derived from these by the builder, so a
/// fixture states equities, never errors.
/// </summary>
/// <param name="NoDouble">Cubeful equity of not doubling (rolling on).</param>
/// <param name="DoubleTake">Cubeful equity of doubling when the opponent takes.</param>
/// <param name="DoubleDrop">
/// Cubeful equity of doubling when the opponent passes — <c>+1</c> for a
/// centred cube in match play or money, the stake-normalized value XG
/// stores.
/// </param>
public readonly record struct XgCubeEquities(double NoDouble, double DoubleTake, double DoubleDrop)
{
    /// <summary>
    /// The doubler's equity of doubling: the taker chooses the reply that
    /// costs the doubler most.
    /// </summary>
    internal double DoubleEquity => Math.Min(DoubleTake, DoubleDrop);

    /// <summary>True when doubling is the proper action.</summary>
    internal bool ShouldDouble => DoubleEquity > NoDouble;

    /// <summary>True when taking is the proper reply to a double.</summary>
    internal bool ShouldTake => DoubleTake <= DoubleDrop;
}
