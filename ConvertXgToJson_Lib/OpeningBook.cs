using System.Diagnostics.CodeAnalysis;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Position-keyed lookup over XG's opening-book database
/// (<c>OpeningBookV2.ob</c>, installed with eXtreme Gammon 2). Load once
/// with <see cref="Load"/> / <see cref="TryLoad"/>, then resolve candidate
/// plays to book entries with <see cref="TryGetEntry"/> — the data behind
/// the bare 998/999 book level codes that <c>.xg</c> files stamp on
/// book-analysed candidates.
///
/// <para>
/// The book may hold several entries for one <see cref="OpeningBookKey"/>
/// (independent rollouts by different contributors, plus XG's own
/// Roller++ evaluation baseline). <see cref="TryGetEntry"/> returns the
/// entry XG itself displays, per the selection policy below;
/// <see cref="GetEntries"/> returns all of them, best first.
/// </para>
///
/// <para>
/// <b>Selection policy</b> (empirical — verified against XG's tooltip
/// choice on positions where the tiers compete): rollout entries outrank
/// evaluation entries; deeper rollout levels outrank shallower ones
/// (checker level first, then cube level, compared by the
/// <see cref="XgDecisionIterator.ResolveDepthInfo"/> rank taxonomy — XG
/// demonstrably prefers a deeper-level rollout over one with more games);
/// then more trials; then the later analysis date; then the later file
/// position (the book grows by import-append, so later wins). The relative
/// order of the checker-level and cube-level comparisons is the one
/// unverified choice: the shipped database offers no key where they
/// disagree across competing entries.
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
    /// Looks up the book entry XG would display for the keyed candidate
    /// play: the best matching entry under the selection policy documented
    /// on the class. Returns false when the book holds no entry for the key.
    /// </summary>
    public bool TryGetEntry(in OpeningBookKey key, [NotNullWhen(true)] out OpeningBookEntry? entry)
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
    public IReadOnlyList<OpeningBookEntry> GetEntries(in OpeningBookKey key)
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
    /// policy and its empirical grounding.
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
    /// rollout 100 ranks above the Roller family, which ranks above plies.
    /// </summary>
    private static int Rank(int level) =>
        XgDecisionIterator.ResolveDepthInfo((short)level, rolloutIndex: -1, NoRollouts).Rank;
}
