using System.Diagnostics.CodeAnalysis;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;

namespace ConvertXgToJson_Lib;

/// <summary>
/// A loaded XG opening-book database (<c>OpeningBookV2.ob</c>, installed
/// with eXtreme Gammon 2) — the rollout data behind the bare 998/999 book
/// level codes that <c>.xg</c> files stamp on book-analysed candidates.
/// Load once with <see cref="Load"/> / <see cref="TryLoad"/> and hand the
/// instance to the decision iterator through
/// <see cref="XgIteratorOptions.OpeningBook"/>; the iterator resolves each
/// book-stamped candidate to its entry and enriches the emitted depth
/// labels and levels.
///
/// <para>
/// Position-keyed lookup (<see cref="TryGetEntry"/> / <see cref="GetEntries"/>
/// over an <see cref="OpeningBookKey"/>) is internal to that enrichment:
/// keying needs the XG record position convention, which is not part of
/// the public surface, so a consumer's intent is "enrich with this book",
/// never "look this position up".
/// </para>
///
/// <para>
/// The book may hold several entries for one <see cref="OpeningBookKey"/>
/// (independent rollouts by different contributors, plus XG's own
/// Roller++ evaluation baseline). <see cref="TryGetEntry"/> returns the
/// most rigorous of them, per the selection policy below;
/// <see cref="GetEntries"/> returns all of them, best first.
/// </para>
///
/// <para>
/// <b>Selection policy — this library's, not XG's: the most rigorous
/// entry wins.</b> Rollout entries outrank evaluation entries; deeper
/// rollout levels outrank shallower ones (checker level first, then cube
/// level, compared by the <see cref="XgDecisionIterator.ResolveDepthInfo"/>
/// rank taxonomy); then more trials; then the later analysis date; then
/// the later file position (the book grows by import-append, so later
/// wins). The relative order of the checker-level and cube-level
/// comparisons is the one untested choice: the shipped database offers no
/// key where they disagree across competing entries.
/// </para>
///
/// <para>
/// <b>XG's own display follows some other rule, and the whole observed
/// sample says so.</b> XG's tooltip is the only oracle for which entry XG
/// shows, and it has been read on two keys where rollouts of different
/// depth compete; they pull opposite ways. On <c>ajhhBG0407.xg</c> game 9
/// move 1 (13/9 6/5) XG shows the 12,960-game 4-ply/4-ply Kazaross
/// rollout over a 20,736-game 3-ply/3-ply one — the deeper entry, which
/// this policy also selects. On <c>match26212229.xg</c> game 3 move 2
/// (13/11 13/7, 2-away/1-away Crawford) XG shows Rockwell's 20,736-game
/// 3-ply rollout while the same key also holds Obukhov's 2,592-game 4-ply
/// one; this policy selects the latter, and that is the ruling: both sit
/// under one byte-identical key, so the deeper rollout is the more rigorous
/// answer to the same question, and the selection stands
/// (halheinrich/backgammon#203, ruled 2026-09-12). One case each way is
/// not a rule for XG's display, so this library makes no parity claim: the
/// policy is stated as its own, and a consumer comparing an enriched depth
/// label against XG's tooltip should expect the two to differ on keys of
/// the second shape. (A third pinned tooltip reading, a Steven Carey
/// 9-away/9-away rollout, agrees with the policy's choice but its key's
/// competitors were not surveyed, so it separates nothing.) Format
/// decoding is a different matter, where the tooltip is the oracle —
/// see <see cref="OpeningBookParser"/>.
/// </para>
///
/// <para>
/// Instances are immutable after load and safe for concurrent reads.
/// </para>
/// </summary>
public sealed class OpeningBook
{
    private static readonly List<RolloutContext> NoRollouts = [];

    private readonly OpeningBookEntry[] _entries;                  // file order
    private readonly Dictionary<OpeningBookKey, List<int>> _index; // key → file indices, ascending

    private OpeningBook(OpeningBookDocument document)
    {
        _entries = [.. document.Entries];
        Title = document.Title;
        Description = document.Description;
        VersionText = document.VersionText;
        FormatVersion = document.FormatVersion;
        CreatedOn = document.CreatedOn;

        _index = [];
        for (int i = 0; i < _entries.Length; i++)
        {
            var key = OpeningBookKey.ForStoredEntry(_entries[i]);
            if (!_index.TryGetValue(key, out var indices))
                _index[key] = indices = [];
            indices.Add(i);
        }
    }

    /// <summary>
    /// Loads an opening-book database from disk. Throws
    /// <see cref="FileNotFoundException"/> (or the usual I/O exceptions)
    /// when the file cannot be read and <see cref="InvalidDataException"/>
    /// when its content is not a valid opening book.
    /// </summary>
    public static OpeningBook Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return FromImage(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Builds a book from an in-memory file image — the byte-level seam the
    /// synthetic-image tests parse through; <see cref="Load"/> routes here.
    /// </summary>
    internal static OpeningBook FromImage(ReadOnlySpan<byte> image) =>
        new(OpeningBookParser.Parse(image));

    /// <summary>
    /// Attempts to load an opening-book database. Returns false — with a
    /// null <paramref name="book"/> — when the file is missing, unreadable,
    /// or not a valid opening book.
    /// </summary>
    public static bool TryLoad(string path, [NotNullWhen(true)] out OpeningBook? book)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            book = Load(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            book = null;
            return false;
        }
    }

    /// <summary>Number of entries in the book.</summary>
    public int EntryCount => _entries.Length;

    /// <summary>Header title (the shipped database:
    /// "Based on rollout performed by the Bgonline.org community").</summary>
    public string Title { get; }

    /// <summary>Long description from the header's continuation blocks
    /// (contributor credits in the shipped database).</summary>
    public string Description { get; }

    /// <summary>Header version text (observed: "3.70").</summary>
    public string VersionText { get; }

    /// <summary>Header format-version int (observed: 1).</summary>
    public int FormatVersion { get; }

    /// <summary>Header creation date.</summary>
    public DateTime CreatedOn { get; }

    /// <summary>All entries in file order — test/diagnostic surface.</summary>
    internal IReadOnlyList<OpeningBookEntry> Entries => _entries;

    /// <summary>
    /// Looks up the most rigorous book entry for the keyed candidate play:
    /// the best matching entry under the selection policy documented on the
    /// class — this library's policy, not a prediction of the entry XG
    /// displays. Returns false when the book holds no entry for the key.
    /// </summary>
    internal bool TryGetEntry(in OpeningBookKey key, [NotNullWhen(true)] out OpeningBookEntry? entry)
    {
        if (!_index.TryGetValue(key, out var indices))
        {
            entry = null;
            return false;
        }

        int best = indices[0];
        for (int i = 1; i < indices.Count; i++)
            if (CompareSelection(indices[i], best) < 0)
                best = indices[i];

        entry = _entries[best];
        return true;
    }

    /// <summary>
    /// Returns every book entry for the keyed candidate play, ordered best
    /// first under the selection policy documented on the class; empty when
    /// the book holds none.
    /// </summary>
    internal IReadOnlyList<OpeningBookEntry> GetEntries(in OpeningBookKey key)
    {
        if (!_index.TryGetValue(key, out var indices))
            return [];

        var sorted = indices.ToArray();
        Array.Sort(sorted, CompareSelection);
        return [.. sorted.Select(i => _entries[i])];
    }

    /// <summary>
    /// The selection policy as a comparison over file indices: negative when
    /// <paramref name="a"/> is the better entry. See the class docs for the
    /// policy and the observed cases it is measured against.
    /// </summary>
    private int CompareSelection(int a, int b)
    {
        var ea = _entries[a];
        var eb = _entries[b];

        int c = Rank(eb.Level).CompareTo(Rank(ea.Level));
        if (c != 0) return c;
        c = Rank(eb.RolloutMovesLevel).CompareTo(Rank(ea.RolloutMovesLevel));
        if (c != 0) return c;
        c = Rank(eb.RolloutCubeLevel).CompareTo(Rank(ea.RolloutCubeLevel));
        if (c != 0) return c;
        c = eb.Trials.CompareTo(ea.Trials);
        if (c != 0) return c;
        c = eb.AnalyzedOn.CompareTo(ea.AnalyzedOn);
        if (c != 0) return c;
        return b.CompareTo(a); // later file position wins
    }

    /// <summary>
    /// Depth rank of an XG level code, routed through the level taxonomy's
    /// single source (<see cref="XgDecisionIterator.ResolveDepthInfo"/>):
    /// rollout (100) ranks above the opening book (99), which ranks above
    /// every evaluation. Within the evaluations the ply and XG Roller
    /// families <i>interleave</i> per XG's own menu, so this comparison is a
    /// rigor ordering — not "Roller beats ply". Only the ordering is used;
    /// the absolute values are the taxonomy's business.
    /// </summary>
    private static int Rank(int level) =>
        XgDecisionIterator.ResolveDepthInfo((short)level, rolloutIndex: -1, NoRollouts).Rank;
}
