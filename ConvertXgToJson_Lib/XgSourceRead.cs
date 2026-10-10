namespace ConvertXgToJson_Lib;

/// <summary>
/// The outcome of reading one source through <see cref="XgSourceReader"/>:
/// either the source decoded (<see cref="XgSourceDecoded"/>, holding its
/// <see cref="Models.XgFile"/>) or the source failed
/// (<see cref="XgSourceFailed"/>, holding the exception that failed it). A
/// source that could not be acquired and a source that could not be decoded
/// fail alike, under the read's one failure policy.
/// </summary>
/// <remarks>
/// <para>
/// <b>A closed pair.</b> The constructors are not reachable outside this
/// library, so every read is exactly one of the two;
/// <see cref="Match{TResult}"/> and <see cref="Switch"/> take one branch per
/// case, and a pattern match on the two types is just as complete.
/// </para>
/// <para>
/// <b>Attributable.</b> <see cref="SourceName"/> is the source as the caller
/// named it, so a report or a skip record built from an outcome names the
/// source that produced it.
/// </para>
/// <para>
/// <b>One read's result, not a value.</b> An outcome compares by reference:
/// it holds an <see cref="Models.XgFile"/> or an exception, neither of
/// which has value equality, and two reads of the same bytes are two
/// outcomes.
/// </para>
/// </remarks>
public abstract class XgSourceRead
{
    /// <summary>Fixes the source's name; reachable only from the two cases in this library.</summary>
    private protected XgSourceRead(string sourceName) => SourceName = sourceName;

    /// <summary>
    /// The source as the caller named it: the path exactly as passed to
    /// <see cref="XgSourceReader.ReadFile"/> or
    /// <see cref="XgSourceReader.ReadFileAsync"/>, or the name passed beside
    /// a stream or an acquisition. Never null or blank.
    /// </summary>
    public string SourceName { get; }

    /// <summary>The result of the branch for this outcome — one branch per case.</summary>
    /// <typeparam name="TResult">The type both branches return.</typeparam>
    /// <param name="decoded">The branch for a decoded source.</param>
    /// <param name="failed">The branch for a failed source.</param>
    /// <exception cref="ArgumentNullException">Either branch is <see langword="null"/>.</exception>
    public abstract TResult Match<TResult>(Func<XgSourceDecoded, TResult> decoded, Func<XgSourceFailed, TResult> failed);

    /// <summary>Runs the branch for this outcome — the statement form of <see cref="Match{TResult}"/>.</summary>
    /// <param name="decoded">The branch for a decoded source.</param>
    /// <param name="failed">The branch for a failed source.</param>
    /// <exception cref="ArgumentNullException">Either branch is <see langword="null"/>.</exception>
    public abstract void Switch(Action<XgSourceDecoded> decoded, Action<XgSourceFailed> failed);
}
