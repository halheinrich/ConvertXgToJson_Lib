namespace ConvertXgToJson_Lib;

/// <summary>
/// Single source of truth for the depth-abbreviation grammar: the compact
/// <c>Abbreviation</c> that <see cref="XgDecisionIterator.ResolveDepthInfo"/>
/// produces for the two depth forms that carry a trial count — an explicit
/// rollout and an enriched opening-book rollout — and that reaches consumers
/// as the candidate's <c>DepthAbbreviation</c> and the cube decision's
/// <c>CubeDepthAbbreviation</c>. Every other depth form (a plain evaluation
/// level, a bare book stamp, the fallback) abbreviates to the fixed
/// <c>LevelInfo</c> table entry and never passes through here.
///
/// <para>
/// The two forms are one grammar: a level token, then
/// <see cref="Separator"/>, then the trial count. The rollout form's token is
/// the rollout's inner ply; the book form's is the moves-level token
/// <c>XgDecisionIterator.BookInnerToken</c> derives, and the form is marked by
/// <see cref="BookPrefix"/>. The division of ownership is deliberate: the
/// iterator decides <i>what</i> the token is (it owns the level taxonomy the
/// token projects), this type decides <i>how</i> a token and a trial count
/// are written. The separator and the prefix are spelled nowhere else.
/// </para>
///
/// <para>
/// The written form is pinned in exactly one test,
/// <c>DepthResolutionTests.DepthAbbreviationFormat_SpellsBothTrialBearingForms</c>;
/// every other abbreviation assertion composes its expectation through this
/// type. A change to the grammar is an edit here and to that pin, and
/// nowhere else in this library.
/// </para>
/// </summary>
internal static class DepthAbbreviationFormat
{
    /// <summary>Joins the level token to the trial count, in both forms.</summary>
    private const string Separator = "p";

    /// <summary>Marks the book form, distinguishing a cached book rollout
    /// from an explicit rollout the file carries.</summary>
    private const string BookPrefix = "B";

    /// <summary>
    /// The rollout form: the inner evaluation ply, then the trial count.
    /// </summary>
    /// <param name="innerPly">The rollout's inner ply, written raw — an
    /// out-of-range value is still spelled (only the level axis degrades it).</param>
    /// <param name="trials">The number of games rolled.</param>
    internal static string Rollout(int innerPly, int trials) =>
        Compose(innerPly.ToString(), trials);

    /// <summary>
    /// The book form: <see cref="BookPrefix"/>, then the entry's moves-level
    /// token, then the trial count.
    /// </summary>
    /// <param name="levelToken">The moves-level token, as
    /// <c>XgDecisionIterator.BookInnerToken</c> derives it.</param>
    /// <param name="trials">The book entry's stored trial count.</param>
    internal static string Book(string levelToken, int trials) =>
        BookPrefix + Compose(levelToken, trials);

    private static string Compose(string levelToken, int trials) =>
        $"{levelToken}{Separator}{trials}";
}
