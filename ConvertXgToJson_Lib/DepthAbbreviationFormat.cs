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
/// The two forms share a shape — a level token, a separator, then the trial
/// count — but deliberately not a separator. The rollout form joins with
/// <see cref="RolloutSeparator"/> (<c>3p1296</c>); the book form is marked by
/// <see cref="BookPrefix"/> and joins with <see cref="BookSeparator"/>
/// (<c>B4_12960</c>). That the two differ is the user's ruling of 2026-09-16
/// on the scope of halheinrich/backgammon#232 (halheinrich/backgammon#240):
/// the underscore was asked for on the <i>book</i> labels, and the rollout
/// form keeps the ply-marked spelling it always had. The rollout form's token
/// is the rollout's inner ply; the book form's is the moves-level token
/// <c>XgDecisionIterator.BookInnerToken</c> derives. The division of
/// ownership is deliberate: the iterator decides <i>what</i> the token is (it
/// owns the level taxonomy the token projects), this type decides <i>how</i> a
/// token and a trial count are written. Both separators and the prefix are
/// spelled nowhere else.
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
    /// <summary>Joins the rollout form's inner ply to the trial count.</summary>
    private const string RolloutSeparator = "p";

    /// <summary>Joins the book form's moves-level token to the trial count.</summary>
    private const string BookSeparator = "_";

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
        Compose(innerPly.ToString(), RolloutSeparator, trials);

    /// <summary>
    /// The book form: <see cref="BookPrefix"/>, then the entry's moves-level
    /// token, then the trial count.
    /// </summary>
    /// <param name="levelToken">The moves-level token, as
    /// <c>XgDecisionIterator.BookInnerToken</c> derives it.</param>
    /// <param name="trials">The book entry's stored trial count.</param>
    internal static string Book(string levelToken, int trials) =>
        BookPrefix + Compose(levelToken, BookSeparator, trials);

    private static string Compose(string levelToken, string separator, int trials) =>
        $"{levelToken}{separator}{trials}";
}
