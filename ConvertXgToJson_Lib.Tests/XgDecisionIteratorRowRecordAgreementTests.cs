using System.Collections;
using System.Reflection;
using BgDataTypes_Lib;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The agreement pin for <see cref="XgDecisionIterator"/>'s two parallel
/// constructions. <c>BuildMoveRow</c> / <c>BuildCubeRows</c> assemble a
/// <see cref="DecisionRow"/> and <c>BuildMoveDiagramRequest</c> /
/// <c>BuildCubeDiagramRequests</c> assemble a <see cref="BgDecisionData"/>
/// from the same match context, field by field — a second enumeration of the
/// same facts that nothing checked (halheinrich/backgammon#122's class). It
/// goes stale silently: a fact stamped on one construction and not the other
/// is invisible until a consumer notices the hole. That is exactly how the row
/// path came to leave <see cref="DecisionRow.IsJacoby"/> unstamped while the
/// record path carried it (halheinrich/backgammon#144).
///
/// <para>
/// <b>What enumerates the shared facts.</b> Not a hand-kept list of names:
/// <see cref="IDecisionFilterData"/>, the interface both types implement and
/// which its own summary calls the "common filtering contract shared by
/// DecisionRow and BgDecisionData". Every member on it is, by construction, a
/// fact both types carry, and the compiler keeps that enumeration current —
/// adding a member obliges both types to answer it, and this pin then obliges
/// both <em>constructions</em> to stamp it the same. Four further facts sit on
/// both types by the same name but off the interface (<c>Id</c>, <c>Xgid</c>,
/// <c>SourceFile</c>, <c>Game</c>); they are pinned explicitly in
/// <see cref="SharedFacts"/>.
/// </para>
///
/// <para>
/// <b>The completeness check</b> (<see cref="EveryNameBothTypesCarry_IsPinned"/>)
/// is what makes that explicit half safe: it intersects the two types' member
/// names — the record's across its four category records as well — and
/// requires the intersection to be exactly what <see cref="SharedFacts"/>
/// pins, less an explicitly reasoned <see cref="NotAgreedByDesign"/> entry. A
/// fact added to both types therefore fails here until it is pinned, and
/// pinning it puts it under the agreement assertion.
/// </para>
///
/// <para>
/// Modelled on BackgammonDiagram_Lib's <c>BuilderFieldCarriageTests</c>
/// (<c>1a928b3</c>), adapted from "one type copied field-by-field into itself"
/// to "two differently shaped types assembled side by side": the comparison is
/// between the two surfaces' real output over a real file, joined by
/// <see cref="DecisionId"/>, rather than between a hand-built fixture and its
/// copy.
/// </para>
/// </summary>
[Collection("FileIO")]
public class XgDecisionIteratorRowRecordAgreementTests
{
    /// <summary>
    /// The corpus, chosen so that between them the fixtures set every shared
    /// fact away from its default —
    /// <see cref="EveryFactIsExercised_SoADroppedStampCanFail"/> is what holds
    /// this list honest. Money on and off supply the two
    /// <see cref="DecisionRow.IsJacoby"/> values, the 5-point match supplies a
    /// non-money score, and the 3-point match is the only one here that reaches
    /// a Crawford game.
    /// </summary>
    private static readonly string[] FixtureNames =
    [
        "Make20Pt.xg",                                             // money, Jacoby on
        "MoneyTest.xg",                                            // money, Jacoby off
        "MatchTest.xg",                                            // 5-point match
        "2026-03-12_ABC_halheinrich-Khasha_3pt_HALHEINRICH9121.xg", // 3-point match, reaches Crawford
    ];

    public static TheoryData<string> Fixtures => [.. FixtureNames];

    // -----------------------------------------------------------------------
    //  The agreement
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void RowAndRecord_AgreeOnEveryFactBothCarry(string fixtureName)
    {
        foreach (var (row, record) in ReadPairs(fixtureName))
        {
            foreach (var fact in SharedFacts())
            {
                object? fromRow = fact.FromRow(row);
                object? fromRecord = fact.FromRecord(record);

                SameValue(fromRow, fromRecord).Should().BeTrue(
                    "the row and record constructions in XgDecisionIterator enumerate the "
                    + "same facts and must stamp {0} identically, but the row says {1} and "
                    + "the record says {2} ({3}, {4})",
                    fact.Name, Describe(fromRow), Describe(fromRecord), fixtureName, row.Id);
            }
        }
    }

    /// <summary>
    /// Guards the agreement above against passing vacuously: a fact left at its
    /// type default by every decision in every fixture would agree no matter
    /// which construction dropped it. Each shared fact must therefore be seen
    /// away from its default at least once across the corpus.
    /// </summary>
    [Fact]
    public void EveryFactIsExercised_SoADroppedStampCanFail()
    {
        var pairs = FixtureNames.SelectMany(ReadPairs).ToList();
        pairs.Should().NotBeEmpty();

        foreach (var fact in SharedFacts())
        {
            // Asserted as a bare bool rather than a collection predicate: the
            // pairs are whole decisions, and a failing Contain would render
            // every one of them into the message.
            bool exercised = pairs.Any(p => !SameValue(fact.FromRecord(p.Record), fact.Default));

            exercised.Should().BeTrue(
                "no decision in any fixture sets {0} away from its default ({1}), so the "
                + "agreement assertion cannot fail when one construction drops it — give "
                + "this pin a fixture that exercises it",
                fact.Name, Describe(fact.Default));
        }
    }

    // -----------------------------------------------------------------------
    //  The completeness check
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every member name the two types both carry must be pinned as a shared
    /// fact or declared not-agreed with a reason. Fails on a fact added to both
    /// types and on a pinned fact removed from either.
    /// </summary>
    [Fact]
    public void EveryNameBothTypesCarry_IsPinned()
    {
        var carriedByBoth = MemberNames(typeof(DecisionRow))
            .Intersect(MemberNames(typeof(BgDecisionData))
                .Union(MemberNames(typeof(PositionData)))
                .Union(MemberNames(typeof(DecisionData)))
                .Union(MemberNames(typeof(DescriptiveData)))
                .Union(MemberNames(typeof(PlayOutcomeData))))
            .ToHashSet(StringComparer.Ordinal);

        var pinned = SharedFacts().Select(f => f.Name)
            .Union(NotAgreedByDesign.Keys)
            .ToHashSet(StringComparer.Ordinal);

        carriedByBoth.Except(pinned).Should().BeEmpty(
            "a fact both DecisionRow and BgDecisionData carry must be pinned in "
            + "SharedFacts (or declared in NotAgreedByDesign with a reason), so the "
            + "iterator's two constructions cannot drift on it");
        pinned.Except(carriedByBoth).Should().BeEmpty(
            "this pin names a fact the two types no longer both carry — retire it here "
            + "rather than leaving a dead assertion");
    }

    /// <summary>
    /// Names carried by both types that the two constructions deliberately do
    /// <em>not</em> stamp alike, each with its reason. Kept as a map so the
    /// reason travels with the exemption rather than living in prose.
    /// </summary>
    private static readonly Dictionary<string, string> NotAgreedByDesign = new(StringComparer.Ordinal)
    {
        ["FilterError"] =
            "the two types' error contracts differ by design: DecisionRow.Error is "
            + "documented never-null and reads 0.0 for 'not recorded', while the record's "
            + "UserPlayError / UserDoubleError are null there. Pinned below against that "
            + "stated relationship instead of by equality.",
    };

    /// <summary>
    /// The exempted fact, pinned against the relationship that exempts it: the
    /// row's non-null error is the record's error with "not recorded" spelled
    /// 0.0 rather than <see langword="null"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void RowError_IsTheRecordError_WithNotRecordedSpelledZero(string fixtureName)
    {
        foreach (var (row, record) in ReadPairs(fixtureName))
        {
            IDecisionFilterData asRecord = record;
            row.FilterError.Should().Be(asRecord.FilterError ?? 0.0,
                "DecisionRow.Error carries the same magnitude the record does, spelling "
                + "'not recorded' as 0.0 ({0}, {1})", fixtureName, row.Id);
        }
    }

    // -----------------------------------------------------------------------
    //  The shared-fact table
    // -----------------------------------------------------------------------

    private sealed record SharedFact(
        string Name,
        Func<DecisionRow, object?> FromRow,
        Func<BgDecisionData, object?> FromRecord)
    {
        /// <summary>
        /// What this fact reads as on a row nothing stamped — the exact shape a
        /// dropped stamp leaves behind, and so the right baseline for
        /// <see cref="EveryFactIsExercised_SoADroppedStampCanFail"/>. Read off
        /// the row rather than the record deliberately: the record declines to
        /// answer <c>Dice</c> at all on a pristine instance (a checker play
        /// with unstamped dice fails loud by design), whereas the row's
        /// defaults are all readable — and it is the row's default that a drop
        /// would produce.
        /// </summary>
        public object? Default => FromRow(new DecisionRow { Id = PristineId });
    }

    /// <summary>
    /// Every member of <see cref="IDecisionFilterData"/> — the shared contract,
    /// reflected rather than restated — plus the facts both types carry by the
    /// same name off the interface. <see cref="NotAgreedByDesign"/> names are
    /// dropped here and pinned separately.
    /// </summary>
    private static IEnumerable<SharedFact> SharedFacts()
    {
        foreach (var property in typeof(IDecisionFilterData)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (NotAgreedByDesign.ContainsKey(property.Name))
                continue;
            var captured = property;
            yield return new SharedFact(
                captured.Name,
                row => captured.GetValue(row),
                record => captured.GetValue(record));
        }

        yield return new SharedFact("Id", r => r.Id, d => d.Id);
        yield return new SharedFact("Xgid", r => r.Xgid, d => d.Xgid);
        yield return new SharedFact("SourceFile", r => r.SourceFile, d => d.Descriptive.SourceFile);
        yield return new SharedFact("Game", r => r.Game, d => d.Descriptive.Game);
    }

    /// <summary>
    /// A stand-in Id for the unstamped row the table reads member defaults off
    /// — <c>Id</c> is <c>required</c>, so even a pristine row needs one.
    /// Deliberately not any real decision's.
    /// </summary>
    private static DecisionId PristineId { get; } = new XgpDecisionId("pristine.xgp");

    // -----------------------------------------------------------------------
    //  Reading one iteration of one fixture through both surfaces
    // -----------------------------------------------------------------------

    private sealed record Pair(DecisionRow Row, BgDecisionData Record);

    /// <summary>
    /// Runs one fixture through both decision surfaces and joins the results by
    /// <see cref="DecisionId"/>. The record surface is the subset — it declines
    /// a checker play whose dice the file never recorded — so every record must
    /// find a row, and both decision kinds must appear or one of the two
    /// construction pairs would go unproven.
    /// </summary>
    private static List<Pair> ReadPairs(string fixtureName)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, fixtureName);

        var rows = XgDecisionIterator
            .Iterate(XgFileReader.ReadFile(path), fixtureName)
            .ToDictionary(r => r.Id);
        var records = XgDecisionIterator
            .IterateDiagramRequests(XgFileReader.ReadFile(path), fixtureName)
            .ToList();

        var pairs = records
            .Where(d => rows.ContainsKey(d.Id))
            .Select(d => new Pair(rows[d.Id], d))
            .ToList();

        // Counts and bare bools rather than collection predicates: the pairs are
        // whole decisions, and a failing collection assertion would render every
        // one of them into the message.
        pairs.Count.Should().Be(records.Count,
            "{0}: every decision the record surface yields must also reach the row "
            + "surface — the two walk the same record stream", fixtureName);
        pairs.Any(p => !p.Record.Decision.IsCube).Should().BeTrue(
            "{0} must exercise the checker-play construction pair", fixtureName);
        pairs.Any(p => p.Record.Decision.IsCube).Should().BeTrue(
            "{0} must exercise the cube-decision construction pair", fixtureName);
        return pairs;
    }

    // -----------------------------------------------------------------------
    //  Reflection helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// The fact names a type carries: its own public instance properties, plus
    /// the shared contract's when it implements it —
    /// <c>IDecisionFilterData.IsMoneyGame</c> is a default interface member, so
    /// it is carried without appearing among either class's declared
    /// properties.
    /// </summary>
    private static IEnumerable<string> MemberNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Select(p => p.Name)
            .Union(typeof(IDecisionFilterData).IsAssignableFrom(type)
                ? typeof(IDecisionFilterData)
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => p.Name)
                : []);

    /// <summary>
    /// Structural equality that also handles the boards, which the two
    /// constructions build into separate arrays and so are never
    /// reference-equal.
    /// </summary>
    private static bool SameValue(object? a, object? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        if (a is not string && a is IEnumerable left && b is IEnumerable right)
            return left.Cast<object?>().SequenceEqual(right.Cast<object?>());
        return a.Equals(b);
    }

    private static string Describe(object? value) => value switch
    {
        null => "<null>",
        string s => $"\"{s}\"",
        IEnumerable e => $"[{string.Join(", ", e.Cast<object?>())}]",
        _ => value.ToString() ?? "<null>",
    };
}
