using System.Numerics;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Exports a single decision as an XG <c>.xgp</c> position file, layered
/// over <see cref="XgFileWriter"/>. ("Exporter" per the ecosystem
/// convention: an Exporter translates semantics; a Writer/Reader mirrors
/// byte layout.)
///
/// <para>Three surfaces, chosen by what the caller holds:</para>
///
/// <para>
/// <b>Clean-position export</b> — <c>Write(BgDecisionData, …)</c>. For
/// callers that hold only the consumer-level decision record (e.g.
/// JSON-sourced). Emits a clean <b>unanalyzed</b> position: XG re-analyzes
/// on import; the analysis blocks hold XG's own never-analysed sentinels
/// (<c>Level = -100</c>, error fields <c>-1000</c>), field values mirror an
/// XG position-editor save, and the on-roll player is normalized to
/// player 1. <b>XG-import-only, by design:</b> rule 1 of the <c>.xgp</c>
/// emission policy ("skip unanalysed") makes clean exports invisible to
/// this library's own <see cref="XgDecisionIterator"/>; the ecosystem's
/// re-ingestible format for analysis-less decisions remains
/// <see cref="BgDecisionData"/> JSON.
/// </para>
///
/// <para>
/// <b>Slice export</b> — <c>Write(XgFile, game, moveNumber, isCube, …)</c>.
/// For callers that hold the parsed source <see cref="XgFile"/> (they just
/// iterated it) plus the decision's coordinates — the same user-level
/// selectors a <see cref="BgDataTypes_Lib.XgDecisionId"/> carries. Emits
/// XG's own save-from-match shape: match header and game header copied
/// with their comment indices cleared but otherwise verbatim (source
/// perspective and metadata preserved), the decision
/// records copied <b>with their analysis panes</b>, and any referenced
/// rollout contexts carried over (indices remapped). The sliced decision
/// records' comments travel the same way — remapped into a fresh dense
/// comment table, RTF payloads verbatim, matching XG's own SaveAs of a
/// commented move. Match- and game-level comments are <b>not</b> carried;
/// the match/game header + footer comment indices are cleared so they
/// cannot dangle into the slice's table. A sliced analyzed
/// decision is therefore visible to our own iterator (exactly one decision)
/// and shows its analysis in XG without re-analyzing.
/// An optional <see cref="XgpSliceOptions"/> overrides the player names
/// (anonymized export) — by header slot or by decision role, resolved
/// internally from the sliced decision's <c>ActivePlayer</c>; every other
/// header field still passes through verbatim.
/// </para>
///
/// <para>
/// <b>Anonymize-copy</b> — <c>Write(XgFile, XgpSliceOptions, …)</c>. For
/// callers that already hold the finished file shape — typically a parsed
/// single-position <c>.xgp</c> being passed along — and only want the
/// names replaced. A whole-file re-emit: <b>every</b> record, rollout
/// context, and comment travels verbatim (no record selection, no comment
/// or rollout remapping — none of the slice path's carving); the only
/// rewrite is the match header's player-name fields per the options.
/// </para>
///
/// <para>
/// Both paths write which panes XG itself writes: a play decision exports a
/// cube record (the real same-turn cube record when the source has one,
/// else XG's incidental unanalysed cube pane) plus the move record; a cube
/// decision exports the cube record only.
/// </para>
/// </summary>
public static class XgpExporter
{
    /// <summary>
    /// The GameId GUID real XG stamps into every <c>.xgp</c> RichGameHeader.
    /// Constant across files and XG versions in the fixture corpus (2010
    /// through current), so it is an XG format constant, not a per-file id.
    /// </summary>
    private static readonly Guid XgpGameId = new("2f5af5e1-e021-4832-a423-ef480ec58a0b");

    /// <summary>
    /// Producer fingerprint written into the match header's Location fields.
    /// The ecosystem treats Location as tool provenance — Backgammon Galaxy
    /// writes "BackgammonGalaxy" there (and XG imports those files happily),
    /// and <see cref="Parsing.SaveRecordParser.IsGalaxyMoneyGame"/> keys on
    /// it — so exports self-identify rather than mimicking XG's own
    /// "eXtreme Gammon". Keep the string stable: it is the hook for ever
    /// special-casing our own exports the way Galaxy's are special-cased.
    /// </summary>
    private const string ExporterLocation = "ConvertXgToJson_Lib";

    /// <summary>Sentinel: analysis level "queued / never ran".</summary>
    private const int UnanalysedLevel = -100;

    /// <summary>Sentinel: error field "unanalyzed".</summary>
    private const double UnanalysedError = -1000.0;

    // ------------------------------------------------------------------ //
    //  Public API
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Writes <paramref name="decision"/> to <paramref name="output"/> as a
    /// complete <c>.xgp</c> file.
    /// </summary>
    /// <param name="decision">The decision to export. See <see cref="Validate"/> remarks for requirements.</param>
    /// <param name="output">Destination stream; written sequentially, left open.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the decision's position is not a 26-element board, its
    /// cube size is not a positive power of two, a play decision carries
    /// dice outside 1–6, or match-play needs are outside 1..MatchLength.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown for a centred cube above 1 (an auto-doubled money position):
    /// the XG record encoding carries cube ownership in the sign of a
    /// log2 field, which cannot express "centred, above 1" without XG's
    /// auto-double bookkeeping. Not supported in v1.
    /// </exception>
    public static void Write(BgDecisionData decision, Stream output)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(output);
        XgFileWriter.Write(ToXgFile(decision), output);
    }

    /// <summary>
    /// Serializes <paramref name="decision"/> to <c>.xgp</c> bytes. Preferred
    /// entry point for browser-hosted (WASM) consumers with no filesystem.
    /// </summary>
    public static byte[] ToBytes(BgDecisionData decision)
    {
        using var ms = new MemoryStream();
        Write(decision, ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Convenience overload: writes <paramref name="decision"/> to
    /// <paramref name="path"/>, overwriting any existing file.
    /// </summary>
    public static void WriteFile(BgDecisionData decision, string path)
    {
        using var fs = File.Create(path);
        Write(decision, fs);
    }

    // ------------------------------------------------------------------ //
    //  Public API — slice export (analysis carried through)
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Writes the decision at (<paramref name="game"/>,
    /// <paramref name="moveNumber"/>, <paramref name="isCube"/>) of
    /// <paramref name="source"/> to <paramref name="output"/> as a complete
    /// <c>.xgp</c> file, <b>carrying the source analysis through</b>. The
    /// coordinates are the same user-level selectors a
    /// <see cref="BgDataTypes_Lib.XgDecisionId"/> carries (and the same
    /// numbering the iterator stamps on
    /// <see cref="BgDataTypes_Lib.DescriptiveData"/>).
    /// </summary>
    /// <param name="source">The parsed source file (typically a full <c>.xg</c> match).</param>
    /// <param name="game">1-based game number within the source.</param>
    /// <param name="moveNumber">1-based move number within the game.</param>
    /// <param name="isCube"><see langword="true"/> for the cube decision at
    /// that move number; <see langword="false"/> for the checker play.</param>
    /// <param name="output">Destination stream; written sequentially, left open.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="source"/> does not begin with a
    /// <see cref="MatchHeaderRecord"/>, or no decision exists at the given
    /// coordinates.
    /// </exception>
    public static void Write(XgFile source, int game, int moveNumber, bool isCube, Stream output)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        XgFileWriter.Write(ToXgFileSlice(source, game, moveNumber, isCube), output);
    }

    /// <summary>
    /// Serializes the decision at the given coordinates of
    /// <paramref name="source"/> to <c>.xgp</c> bytes, analysis carried
    /// through. Preferred entry point for browser-hosted (WASM) consumers.
    /// </summary>
    /// <inheritdoc cref="Write(XgFile, int, int, bool, Stream)"/>
    public static byte[] ToBytes(XgFile source, int game, int moveNumber, bool isCube)
    {
        using var ms = new MemoryStream();
        Write(source, game, moveNumber, isCube, ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Convenience overload: writes the sliced decision to
    /// <paramref name="path"/>, overwriting any existing file.
    /// </summary>
    /// <inheritdoc cref="Write(XgFile, int, int, bool, Stream)"/>
    public static void WriteFile(XgFile source, int game, int moveNumber, bool isCube, string path)
    {
        using var fs = File.Create(path);
        Write(source, game, moveNumber, isCube, fs);
    }

    /// <summary>
    /// Slice export with <paramref name="options"/> applied: the player-name
    /// header fields can be overridden, by header slot or by decision role
    /// (see <see cref="XgpSliceOptions"/> — a slice always defines roles,
    /// the located decision's <c>ActivePlayer</c> naming the on-roll slot);
    /// every other header field is still copied verbatim.
    /// </summary>
    /// <param name="options">Slice options; the default instance changes nothing.</param>
    /// <inheritdoc cref="Write(XgFile, int, int, bool, Stream)"/>
    public static void Write(
        XgFile source, int game, int moveNumber, bool isCube, XgpSliceOptions options, Stream output)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        XgFileWriter.Write(ToXgFileSlice(source, game, moveNumber, isCube, options), output);
    }

    /// <summary>
    /// Serializes the decision at the given coordinates to <c>.xgp</c> bytes
    /// with <paramref name="options"/> applied.
    /// </summary>
    /// <inheritdoc cref="Write(XgFile, int, int, bool, XgpSliceOptions, Stream)"/>
    public static byte[] ToBytes(
        XgFile source, int game, int moveNumber, bool isCube, XgpSliceOptions options)
    {
        using var ms = new MemoryStream();
        Write(source, game, moveNumber, isCube, options, ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Convenience overload: writes the sliced decision to
    /// <paramref name="path"/> with <paramref name="options"/> applied,
    /// overwriting any existing file.
    /// </summary>
    /// <inheritdoc cref="Write(XgFile, int, int, bool, XgpSliceOptions, Stream)"/>
    public static void WriteFile(
        XgFile source, int game, int moveNumber, bool isCube, XgpSliceOptions options, string path)
    {
        using var fs = File.Create(path);
        Write(source, game, moveNumber, isCube, options, fs);
    }

    // ------------------------------------------------------------------ //
    //  Public API — slice export by XgDecisionId
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Slice export addressed by an <see cref="XgDecisionId"/> — the Id the
    /// iterator stamps on every <c>.xg</c>-sourced row. Destructures the
    /// Id's (Game, MoveNumber, IsCube) coordinates and delegates to the
    /// coordinate surface. <paramref name="id"/>.<c>Filename</c> is
    /// <b>not consulted</b>: the caller has already resolved
    /// <paramref name="source"/> from it, and a parsed <see cref="XgFile"/>
    /// carries no filename to verify against. The parameter is
    /// <see cref="XgDecisionId"/>-typed deliberately — an
    /// <see cref="BgDataTypes_Lib.XgpDecisionId"/> carries no coordinates,
    /// so routing it here is a compile-time error rather than a runtime
    /// throw; the Xgp-vs-Xg routing stays with the caller.
    /// </summary>
    /// <param name="source">The parsed source file the Id's coordinates address.</param>
    /// <param name="id">The decision's iterator-stamped Id.</param>
    /// <param name="output">Destination stream; written sequentially, left open.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="source"/> does not begin with a
    /// <see cref="MatchHeaderRecord"/>, or no decision exists at the Id's
    /// coordinates.
    /// </exception>
    public static void Write(XgFile source, XgDecisionId id, Stream output)
    {
        ArgumentNullException.ThrowIfNull(id);
        Write(source, id.Game, id.MoveNumber, id.IsCube, output);
    }

    /// <summary>
    /// Slice export addressed by an <see cref="XgDecisionId"/> with
    /// <paramref name="options"/> applied.
    /// </summary>
    /// <param name="options">Slice options; the default instance changes nothing.</param>
    /// <inheritdoc cref="Write(XgFile, XgDecisionId, Stream)"/>
    public static void Write(XgFile source, XgDecisionId id, XgpSliceOptions options, Stream output)
    {
        ArgumentNullException.ThrowIfNull(id);
        Write(source, id.Game, id.MoveNumber, id.IsCube, options, output);
    }

    /// <summary>
    /// Serializes the decision at <paramref name="id"/>'s coordinates to
    /// <c>.xgp</c> bytes.
    /// </summary>
    /// <inheritdoc cref="Write(XgFile, XgDecisionId, Stream)"/>
    public static byte[] ToBytes(XgFile source, XgDecisionId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return ToBytes(source, id.Game, id.MoveNumber, id.IsCube);
    }

    /// <summary>
    /// Serializes the decision at <paramref name="id"/>'s coordinates to
    /// <c>.xgp</c> bytes with <paramref name="options"/> applied.
    /// </summary>
    /// <inheritdoc cref="Write(XgFile, XgDecisionId, XgpSliceOptions, Stream)"/>
    public static byte[] ToBytes(XgFile source, XgDecisionId id, XgpSliceOptions options)
    {
        ArgumentNullException.ThrowIfNull(id);
        return ToBytes(source, id.Game, id.MoveNumber, id.IsCube, options);
    }

    /// <summary>
    /// Convenience overload: writes the decision at <paramref name="id"/>'s
    /// coordinates to <paramref name="path"/>, overwriting any existing file.
    /// </summary>
    /// <inheritdoc cref="Write(XgFile, XgDecisionId, Stream)"/>
    public static void WriteFile(XgFile source, XgDecisionId id, string path)
    {
        ArgumentNullException.ThrowIfNull(id);
        WriteFile(source, id.Game, id.MoveNumber, id.IsCube, path);
    }

    /// <summary>
    /// Convenience overload: writes the decision at <paramref name="id"/>'s
    /// coordinates to <paramref name="path"/> with
    /// <paramref name="options"/> applied, overwriting any existing file.
    /// </summary>
    /// <inheritdoc cref="Write(XgFile, XgDecisionId, XgpSliceOptions, Stream)"/>
    public static void WriteFile(XgFile source, XgDecisionId id, XgpSliceOptions options, string path)
    {
        ArgumentNullException.ThrowIfNull(id);
        WriteFile(source, id.Game, id.MoveNumber, id.IsCube, options, path);
    }

    // ------------------------------------------------------------------ //
    //  Public API — anonymize-copy (whole-file re-emit, name overrides)
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Re-emits <paramref name="source"/> in full to
    /// <paramref name="output"/> with <paramref name="options"/>'s
    /// player-name overrides applied. A <b>whole-file copy</b>, not a
    /// slice: every record, rollout context, and comment travels verbatim
    /// (no record selection, no comment clearing, no rollout remapping);
    /// the only rewrite is the match header's player-name fields — an
    /// options instance with no overrides re-emits the source unchanged.
    /// Role-based overrides apply when the source's decision records
    /// define roles — every move/cube record sharing one
    /// <c>ActivePlayer</c> sign, true for every single-decision
    /// <c>.xgp</c>; a multi-decision source (a whole <c>.xg</c> match) has
    /// no file-wide roles and uses the slot-based names (see
    /// <see cref="XgpSliceOptions"/>).
    /// For callers that already hold the finished file shape, typically a
    /// parsed single-position <c>.xgp</c> being passed along anonymized;
    /// callers extracting one decision from a match use the slice surface
    /// instead.
    /// </summary>
    /// <param name="source">The parsed file to re-emit.</param>
    /// <param name="options">Player-name overrides; the default instance changes nothing.</param>
    /// <param name="output">Destination stream; written sequentially, left open.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="source"/> does not begin with a
    /// <see cref="MatchHeaderRecord"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when <paramref name="options"/> sets a role-based name with
    /// no slot-based fallback and the source's roles are not determinable.
    /// </exception>
    public static void Write(XgFile source, XgpSliceOptions options, Stream output)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        XgFileWriter.Write(ToXgFileCopy(source, options), output);
    }

    /// <summary>
    /// Serializes the whole-file copy of <paramref name="source"/> to
    /// <c>.xgp</c> bytes with <paramref name="options"/> applied.
    /// </summary>
    /// <inheritdoc cref="Write(XgFile, XgpSliceOptions, Stream)"/>
    public static byte[] ToBytes(XgFile source, XgpSliceOptions options)
    {
        using var ms = new MemoryStream();
        Write(source, options, ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Convenience overload: writes the whole-file copy of
    /// <paramref name="source"/> to <paramref name="path"/> with
    /// <paramref name="options"/> applied, overwriting any existing file.
    /// </summary>
    /// <inheritdoc cref="Write(XgFile, XgpSliceOptions, Stream)"/>
    public static void WriteFile(XgFile source, XgpSliceOptions options, string path)
    {
        using var fs = File.Create(path);
        Write(source, options, fs);
    }

    // ------------------------------------------------------------------ //
    //  Source slice → record set
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Builds the <see cref="XgFile"/> record set for a slice export —
    /// XG's own save-from-match shape, learned from the XG-authored
    /// agreement fixtures: match header and game header copied with their
    /// comment indices cleared but otherwise verbatim (source perspective
    /// and metadata preserved), decision records copied with analysis
    /// intact, referenced rollout contexts carried over with indices
    /// remapped, and the decision records' comments carried into a fresh
    /// dense comment table (indices remapped the same way).
    /// <paramref name="options"/> may override the player names (both
    /// twins per player), by slot or by role — role names resolve against
    /// the located decision's <c>ActivePlayer</c> via
    /// <see cref="ResolveNameOverrides"/>; null or the default instance
    /// changes nothing. Internal so tests can assert on records directly.
    /// </summary>
    internal static XgFile ToXgFileSlice(
        XgFile source, int game, int moveNumber, bool isCube, XgpSliceOptions? options = null)
    {
        if (source.Records.Count == 0 || source.Records[0] is not MatchHeaderRecord sourceMh)
            throw new ArgumentException(
                "Slice source must begin with a MatchHeaderRecord (a parsed .xg/.xgp file always does).",
                nameof(source));

        var (gh, cube, move) = LocateDecision(source, game, moveNumber, isCube);

        // Resolve role-based name overrides against the located decision —
        // the single decision defines the roles, so its record(s) carry the
        // deciding ActivePlayer sign (a play's same-turn cube pane shares
        // the move's) — before the header copy, which stays slot-dumb.
        string? player1Override = null;
        string? player2Override = null;
        if (options is not null)
        {
            var decisionRecords = new List<SaveRecord>(capacity: 2);
            if (cube is not null) decisionRecords.Add(cube);
            if (move is not null) decisionRecords.Add(move);
            (player1Override, player2Override) = ResolveNameOverrides(options, decisionRecords);
        }
        var mh = CopyMatchHeader(sourceMh, player1Override, player2Override, clearCommentIndices: true);

        // Carry only the rollout contexts the sliced records reference,
        // in first-appearance order, and remap indices into the new table.
        var rollouts = new List<RolloutContext>();
        var remap = new Dictionary<int, int>();
        int Remap(int index)
        {
            if (index < 0 || index >= source.Rollouts.Count)
                return -1;
            if (!remap.TryGetValue(index, out int mapped))
            {
                mapped = rollouts.Count;
                rollouts.Add(source.Rollouts[index]);
                remap[index] = mapped;
            }
            return mapped;
        }

        // A cube rollout occupies an adjacent context *pair* with the record
        // pointing at the second leg — XG's own save of a rolled-out cube
        // (match35253054_2_37 ground truth) carries both legs and keeps the
        // pointer on the second. Carry the companion leg before the
        // referenced one so the pair stays adjacent and in order.
        int RemapCubeRollout(int index)
        {
            if (index >= 1 && index < source.Rollouts.Count)
                Remap(index - 1);
            return Remap(index);
        }

        // Carry the comments the sliced decision records reference, into a
        // fresh dense table in record order — the same carve XG's own SaveAs
        // of a commented move performs (CommentExported fixture ground
        // truth). Match- and game-level comments are deliberately not
        // carried: XG mirrors the match header comment into the outer
        // header's plain-text Comments field (CommentsAddedToXgp fixture),
        // so carrying them would drag RTF→plain-text extraction in.
        var comments = new List<string>();
        var commentRemap = new Dictionary<int, int>();
        int RemapComment(int index)
        {
            if (index < 0 || index >= source.Comments.Count)
                return -1;
            if (!commentRemap.TryGetValue(index, out int mapped))
            {
                mapped = comments.Count;
                comments.Add(source.Comments[index]);
                commentRemap[index] = mapped;
            }
            return mapped;
        }

        var records = new List<SaveRecord> { mh, WithClearedCommentIndices(gh) };
        if (isCube)
        {
            records.Add(SliceCubeRecord(cube!, RemapCubeRollout, RemapComment));
        }
        else
        {
            // XG always writes a cube pane beside a move pane: the real
            // same-turn cube record when the source has one (Joe Russell
            // shape), else the incidental unanalysed pane (PlayAnalysis
            // shape) carrying the roll.
            records.Add(cube != null
                ? SliceCubeRecord(cube, RemapCubeRollout, RemapComment)
                : UnanalysedCubeRecord(
                    activePlayer: move!.ActivePlayer,
                    position: move.InitialPosition,
                    cubeValueRaw: move.CubeValue,
                    doubled: -2,
                    diceRolled: $"{move.Dice[0]}{move.Dice[1]}"));
            records.Add(SliceMoveRecord(move!, Remap, RemapComment));
        }

        return new XgFile
        {
            Header = new RichGameHeader
            {
                HeaderVersion = 1,
                GameId = XgpGameId,
                SaveName = BuildSaveName(
                    isMoney: mh.MatchLength >= 99999,
                    matchLength: mh.MatchLength,
                    score1: gh.Score1,
                    score2: gh.Score2,
                    jacoby: mh.Jacoby),
            },
            Records = records,
            Rollouts = rollouts,
            Comments = comments,
        };
    }

    /// <summary>
    /// Finds the decision records at the iterator's coordinates: game =
    /// ordinal of the containing <see cref="GameHeaderRecord"/>; a play's
    /// move number = ordinal of its <see cref="MoveRecord"/> within the
    /// game; a cube decision's move number = moves seen so far + 1 (the
    /// same <c>ctx.MoveNumber + 1</c> stamp the iterator emits). For a play,
    /// also returns the immediately preceding same-turn
    /// <see cref="CubeRecord"/> when one exists.
    /// </summary>
    private static (GameHeaderRecord Game, CubeRecord? Cube, MoveRecord? Move) LocateDecision(
        XgFile source, int game, int moveNumber, bool isCube)
    {
        int currentGame = 0;
        int movesSeen = 0;
        GameHeaderRecord? gh = null;
        SaveRecord? previousInGame = null;

        foreach (var record in source.Records)
        {
            if (record is GameHeaderRecord g)
            {
                currentGame++;
                movesSeen = 0;
                previousInGame = null;
                if (currentGame == game)
                    gh = g;
                continue;
            }
            if (currentGame != game)
                continue;

            if (isCube && record is CubeRecord c && movesSeen == moveNumber - 1)
                return (gh!, c, null);

            if (record is MoveRecord m)
            {
                movesSeen++;
                if (!isCube && movesSeen == moveNumber)
                    return (gh!, previousInGame as CubeRecord, m);
            }
            previousInGame = record;
        }

        throw new ArgumentException(
            $"No {(isCube ? "cube" : "play")} decision at game {game}, move {moveNumber} in the source file.",
            nameof(moveNumber));
    }

    /// <summary>
    /// Copy of a source <see cref="CubeRecord"/> for the slice: analysis
    /// intact, rollout index remapped into the slice's rollout table,
    /// comment index remapped into the slice's comment table (a dangling
    /// source index clears to −1, like an out-of-range rollout index).
    /// </summary>
    private static CubeRecord SliceCubeRecord(
        CubeRecord c, Func<int, int> remapRollout, Func<int, int> remapComment) => new()
    {
        EntryType = RecordType.Cube,
        ActivePlayer = c.ActivePlayer,
        Doubled = c.Doubled,
        Taken = c.Taken,
        BeaverAccepted = c.BeaverAccepted,
        RaccoonAccepted = c.RaccoonAccepted,
        CubeValue = c.CubeValue,
        Position = c.Position,
        Analysis = c.Analysis,
        ErrorCube = c.ErrorCube,
        DiceRolled = c.DiceRolled,
        ErrorTake = c.ErrorTake,
        RolloutIndex = remapRollout(c.RolloutIndex),
        ComputerChoice = c.ComputerChoice,
        AnalyzeLevel = c.AnalyzeLevel,
        ErrorBeaver = c.ErrorBeaver,
        ErrorRaccoon = c.ErrorRaccoon,
        AnalyzeLevelRequested = c.AnalyzeLevelRequested,
        InvalidDecision = c.InvalidDecision,
        TutorCube = c.TutorCube,
        TutorTake = c.TutorTake,
        ErrorTutorCube = c.ErrorTutorCube,
        ErrorTutorTake = c.ErrorTutorTake,
        Flagged = c.Flagged,
        CommentIndex = remapComment(c.CommentIndex),
        Edited = c.Edited,
        TimeDelayed = c.TimeDelayed,
        TimeDelayDone = c.TimeDelayDone,
        NumberOfAutoDoubles = c.NumberOfAutoDoubles,
        TimeBotLeft = c.TimeBotLeft,
        TimeTopLeft = c.TimeTopLeft,
    };

    /// <summary>
    /// Copy of a source <see cref="MoveRecord"/> for the slice: analysis
    /// intact, rollout indices remapped, comment index remapped.
    /// </summary>
    private static MoveRecord SliceMoveRecord(
        MoveRecord m, Func<int, int> remapRollout, Func<int, int> remapComment) => new()
    {
        EntryType = RecordType.Move,
        InitialPosition = m.InitialPosition,
        FinalPosition = m.FinalPosition,
        ActivePlayer = m.ActivePlayer,
        MoveList = m.MoveList,
        Dice = m.Dice,
        CubeValue = m.CubeValue,
        ErrorMove = m.ErrorMove,
        CandidateCount = m.CandidateCount,
        Analysis = m.Analysis,
        Played = m.Played,
        MoveError = m.MoveError,
        LuckError = m.LuckError,
        ComputerChoice = m.ComputerChoice,
        InitialEquity = m.InitialEquity,
        RolloutIndices = [.. m.RolloutIndices.Select(remapRollout)],
        AnalyzeLevel = m.AnalyzeLevel,
        AnalyzeLevelLuck = m.AnalyzeLevelLuck,
        InvalidDecision = m.InvalidDecision,
        TutorPosition = m.TutorPosition,
        TutorMoveIndex = m.TutorMoveIndex,
        ErrorTutorMove = m.ErrorTutorMove,
        Flagged = m.Flagged,
        CommentIndex = remapComment(m.CommentIndex),
        Edited = m.Edited,
        TimeDelayBits = m.TimeDelayBits,
        TimeDelayDoneBits = m.TimeDelayDoneBits,
        NumberOfAutoDoubles = m.NumberOfAutoDoubles,
    };

    /// <summary>
    /// Resolves an options instance's two naming axes into the slot-based
    /// (player 1, player 2) override pair <see cref="CopyMatchHeader"/>
    /// takes — the header copy stays a dumb slot mechanism. Role names
    /// (<see cref="XgpSliceOptions.OnRollName"/> /
    /// <see cref="XgpSliceOptions.OpponentName"/>) name the decision-maker's
    /// slot. Roles are determinable iff <paramref name="records"/> holds at
    /// least one move/cube record and all of them share one
    /// <c>ActivePlayer</c> sign (<c>&gt;= 0</c> is player 1 — the
    /// <see cref="MatchContext.PlayerName"/> convention; a cube record's
    /// <c>ActivePlayer</c> is the doubler, so a take decision is anchored
    /// to the doubler with no special-casing). When determinable, a role
    /// name outranks the same slot's slot name; when not (a multi-decision
    /// whole-<c>.xg</c> copy has no file-wide roles), slot names apply and
    /// role names are deliberately unused — unless no slot name exists to
    /// fall back on, which throws rather than silently guessing a slot.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Thrown when a role-based name is set, no slot-based fallback is, and
    /// roles are not determinable from <paramref name="records"/>.
    /// </exception>
    internal static (string? Player1, string? Player2) ResolveNameOverrides(
        XgpSliceOptions options, IEnumerable<SaveRecord> records)
    {
        if (options.OnRollName is null && options.OpponentName is null)
            return (options.Player1Name, options.Player2Name);

        bool sawPlayer1 = false;
        bool sawPlayer2 = false;
        foreach (var record in records)
        {
            int? activePlayer = record switch
            {
                CubeRecord c => c.ActivePlayer,
                MoveRecord m => m.ActivePlayer,
                _ => null,
            };
            if (activePlayer is null)
                continue;
            if (activePlayer >= 0) sawPlayer1 = true;
            else sawPlayer2 = true;
        }

        if (sawPlayer1 == sawPlayer2) // no decision records, or both signs
        {
            if (options.Player1Name is null && options.Player2Name is null)
                throw new NotSupportedException(
                    "Role-based name overrides (OnRollName/OpponentName) need a source whose " +
                    "records define a single on-roll player — every move/cube record sharing " +
                    "one ActivePlayer sign. This source does not (a multi-decision match has " +
                    "no file-wide roles); set Player1Name/Player2Name as the slot fallback, " +
                    "as the Anonymized preset does.");
            return (options.Player1Name, options.Player2Name);
        }

        return sawPlayer1
            ? (options.OnRollName ?? options.Player1Name, options.OpponentName ?? options.Player2Name)
            : (options.OpponentName ?? options.Player1Name, options.OnRollName ?? options.Player2Name);
    }

    /// <summary>
    /// The single field-for-field copy of <see cref="MatchHeaderRecord"/>,
    /// with two rewrite knobs. A non-null player name replaces that
    /// player's Unicode field <b>and</b> its ANSI twin; null keeps both of
    /// that player's source fields. <paramref name="clearCommentIndices"/>
    /// clears the match header/footer comment indices (the slice path,
    /// whose fresh comment table carries no match-level comments); false
    /// passes them through (the anonymize-copy path, whose comment table
    /// travels verbatim). Every other field, the Location provenance
    /// fingerprint included, passes through verbatim; the options
    /// byte-identity test pins this copy field-for-field.
    /// </summary>
    private static MatchHeaderRecord CopyMatchHeader(
        MatchHeaderRecord mh, string? player1, string? player2, bool clearCommentIndices) => new()
    {
        EntryType = mh.EntryType,
        Player1Ansi = player1 ?? mh.Player1Ansi,
        Player2Ansi = player2 ?? mh.Player2Ansi,
        MatchLength = mh.MatchLength,
        Variation = mh.Variation,
        Crawford = mh.Crawford,
        Jacoby = mh.Jacoby,
        Beaver = mh.Beaver,
        AutoDouble = mh.AutoDouble,
        Elo1 = mh.Elo1,
        Elo2 = mh.Elo2,
        Experience1 = mh.Experience1,
        Experience2 = mh.Experience2,
        Date = mh.Date,
        EventAnsi = mh.EventAnsi,
        GameId = mh.GameId,
        CompLevel1 = mh.CompLevel1,
        CompLevel2 = mh.CompLevel2,
        CountForElo = mh.CountForElo,
        AddToProfile1 = mh.AddToProfile1,
        AddToProfile2 = mh.AddToProfile2,
        LocationAnsi = mh.LocationAnsi,
        GameMode = mh.GameMode,
        Imported = mh.Imported,
        RoundAnsi = mh.RoundAnsi,
        Invert = mh.Invert,
        Version = mh.Version,
        Magic = mh.Magic,
        MoneyInitGames = mh.MoneyInitGames,
        MoneyInitScore = mh.MoneyInitScore,
        Entered = mh.Entered,
        Counted = mh.Counted,
        UnratedImport = mh.UnratedImport,
        CommentHeaderMatchIndex = clearCommentIndices ? -1 : mh.CommentHeaderMatchIndex,
        CommentFooterMatchIndex = clearCommentIndices ? -1 : mh.CommentFooterMatchIndex,
        IsMoneyMatch = mh.IsMoneyMatch,
        WinMoney = mh.WinMoney,
        LoseMoney = mh.LoseMoney,
        Currency = mh.Currency,
        FeeMoney = mh.FeeMoney,
        TableStake = mh.TableStake,
        SiteId = mh.SiteId,
        CubeLimit = mh.CubeLimit,
        AutoDoubleMax = mh.AutoDoubleMax,
        Transcribed = mh.Transcribed,
        Event = mh.Event,
        Player1 = player1 ?? mh.Player1,
        Player2 = player2 ?? mh.Player2,
        Location = mh.Location,
        Round = mh.Round,
        TimeSetting = mh.TimeSetting,
        TotalTimeDelayMoves = mh.TotalTimeDelayMoves,
        TotalTimeDelayCubes = mh.TotalTimeDelayCubes,
        TotalTimeDelayMovesDone = mh.TotalTimeDelayMovesDone,
        TotalTimeDelayCubesDone = mh.TotalTimeDelayCubesDone,
        Transcriber = mh.Transcriber,
    };

    /// <summary>
    /// Copy of the source game header for the slice: every field verbatim
    /// except the game header/footer comment indices, cleared because the
    /// slice's fresh comment table carries no game-level comments.
    /// </summary>
    private static GameHeaderRecord WithClearedCommentIndices(GameHeaderRecord gh) => new()
    {
        EntryType = gh.EntryType,
        Score1 = gh.Score1,
        Score2 = gh.Score2,
        CrawfordApplies = gh.CrawfordApplies,
        InitialPosition = gh.InitialPosition,
        GameNumber = gh.GameNumber,
        InProgress = gh.InProgress,
        CommentHeaderGameIndex = -1,
        CommentFooterGameIndex = -1,
        NumberOfAutoDoubles = gh.NumberOfAutoDoubles,
    };

    // ------------------------------------------------------------------ //
    //  Source copy → record set
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Builds the <see cref="XgFile"/> for an anonymize-copy: the source
    /// model shared wholesale — header, rollout table, and comment table
    /// by reference, records in order — with only the match header
    /// replaced through <see cref="CopyMatchHeader"/> when an override is
    /// present. Role-based names resolve against the whole record stream
    /// via <see cref="ResolveNameOverrides"/> first (determinable for a
    /// single-decision <c>.xgp</c>; a whole <c>.xg</c> match falls back to
    /// slot names). When nothing resolves to an override the source itself
    /// is returned (the model is init-only throughout, so sharing is
    /// safe). Internal so tests can assert on records directly.
    /// </summary>
    internal static XgFile ToXgFileCopy(XgFile source, XgpSliceOptions options)
    {
        if (source.Records.Count == 0 || source.Records[0] is not MatchHeaderRecord mh)
            throw new ArgumentException(
                "Copy source must begin with a MatchHeaderRecord (a parsed .xg/.xgp file always does).",
                nameof(source));

        var (player1Override, player2Override) = ResolveNameOverrides(options, source.Records);
        if (player1Override is null && player2Override is null)
            return source;

        var records = new List<SaveRecord>(source.Records)
        {
            [0] = CopyMatchHeader(
                mh, player1Override, player2Override, clearCommentIndices: false),
        };
        return new XgFile
        {
            Header = source.Header,
            Records = records,
            Rollouts = source.Rollouts,
            Comments = source.Comments,
        };
    }

    // ------------------------------------------------------------------ //
    //  Decision → record set
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Builds the <see cref="XgFile"/> record set for <paramref name="decision"/>.
    /// Internal so tests can assert on records directly; consumers go through
    /// the byte-level entry points and never touch XG record internals.
    /// </summary>
    internal static XgFile ToXgFile(BgDecisionData decision)
    {
        Validate(decision);

        bool isMoney = decision.Descriptive.MatchLength <= 0;
        int matchLength = decision.Descriptive.MatchLength;
        int score1 = isMoney ? 0 : matchLength - decision.Position.OnRollNeeds;
        int score2 = isMoney ? 0 : matchLength - decision.Position.OpponentNeeds;
        var (jacoby, beaver) = isMoney ? MoneyFlags(decision.Xgid) : (false, false);
        bool crawfordApplies = !isMoney && decision.Position.IsCrawford;

        string player1 = NameOrDefault(decision.Descriptive.OnRollName, "Player 1");
        string player2 = NameOrDefault(decision.Descriptive.OpponentName, "Player 2");

        sbyte[] position = ToPositionEngine(decision.Position.Mop);
        int cubeRaw = EncodeCube(decision.Position.CubeSize, decision.Position.CubeOwner);

        var records = new List<SaveRecord>
        {
            BuildMatchHeader(decision, isMoney, matchLength, jacoby, beaver, player1, player2),
            BuildGameHeader(position, score1, score2, crawfordApplies),
            BuildCubeRecord(decision, position, cubeRaw),
        };
        if (!decision.Decision.IsCube)
            records.Add(BuildMoveRecord(decision, position, cubeRaw));

        return new XgFile
        {
            Header = new RichGameHeader
            {
                HeaderVersion = 1,
                GameId = XgpGameId,
                SaveName = BuildSaveName(isMoney, matchLength, score1, score2, jacoby),
            },
            Records = records,
        };
    }

    private static void Validate(BgDecisionData decision)
    {
        if (decision.Position.Mop.Count != 26)
            throw new ArgumentException(
                $"Position.Mop must have 26 elements (got {decision.Position.Mop.Count}).", nameof(decision));

        int cubeSize = decision.Position.CubeSize;
        if (cubeSize < 1 || !BitOperations.IsPow2((uint)cubeSize))
            throw new ArgumentException(
                $"Position.CubeSize must be a positive power of two (got {cubeSize}).", nameof(decision));
        if (cubeSize > 1 && decision.Position.CubeOwner == CubeOwner.Centered)
            throw new NotSupportedException(
                "A centred cube above 1 (auto-doubled money position) is not representable " +
                "in the XG record encoding without auto-double bookkeeping; not supported.");

        if (!decision.Decision.IsCube)
        {
            var dice = decision.Decision.Dice;
            if (dice.Count < 2 || dice[0] is < 1 or > 6 || dice[1] is < 1 or > 6)
                throw new ArgumentException(
                    "A play decision requires two dice in the range 1–6.", nameof(decision));
        }

        int matchLength = decision.Descriptive.MatchLength;
        if (matchLength > 0)
        {
            if (decision.Position.OnRollNeeds < 1 || decision.Position.OnRollNeeds > matchLength
                || decision.Position.OpponentNeeds < 1 || decision.Position.OpponentNeeds > matchLength)
                throw new ArgumentException(
                    $"Match-play needs must be within 1..{matchLength} " +
                    $"(got {decision.Position.OnRollNeeds}/{decision.Position.OpponentNeeds}).", nameof(decision));
        }
    }

    // ------------------------------------------------------------------ //
    //  Record builders — values mirror what real XG writes for a position
    //  saved from its editor (NoAnalysis/PlayAnalysis/DoubleAnalysis fixtures)
    // ------------------------------------------------------------------ //

    private static MatchHeaderRecord BuildMatchHeader(
        BgDecisionData decision, bool isMoney, int matchLength,
        bool jacoby, bool beaver, string player1, string player2)
    {
        string eventName = decision.Descriptive.Event ?? "";
        DateTime date = decision.Descriptive.Date is { } d
            ? d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : default;

        return new MatchHeaderRecord
        {
            EntryType = RecordType.HeaderMatch,
            Player1Ansi = player1,
            Player2Ansi = player2,
            Player1 = player1,
            Player2 = player2,
            MatchLength = isMoney ? 99999 : matchLength,
            Variation = 0,
            Crawford = true,           // XG writes the rule flag on even for money sessions
            Jacoby = isMoney && jacoby,
            Beaver = isMoney && beaver,
            AutoDouble = false,
            Elo1 = 1600,
            Elo2 = 1600,
            Date = date,
            EventAnsi = eventName,
            Event = eventName,
            GameId = DeterministicGameId(decision),
            CompLevel1 = -1,
            CompLevel2 = -1,
            LocationAnsi = ExporterLocation,
            Location = ExporterLocation,
            GameMode = GameMode.Competition,
            Invert = 1,
            Version = 30,
            Magic = 1229737284,        // constant XG stamps into every save
            CommentHeaderMatchIndex = -1,
            CommentFooterMatchIndex = -1,
            IsMoneyMatch = false,      // XG leaves this false; 99999 is the money signal
            SiteId = (SiteId)(-1),
            CubeLimit = 10,            // XG default: max cube 2^10
        };
    }

    private static GameHeaderRecord BuildGameHeader(
        sbyte[] position, int score1, int score2, bool crawfordApplies) => new()
    {
        EntryType = RecordType.HeaderGame,
        Score1 = score1,
        Score2 = score2,
        CrawfordApplies = crawfordApplies,
        // XG's position-editor pattern: the game "starts" at the saved position.
        InitialPosition = new PositionEngine { Points = position },
        GameNumber = 1,
        InProgress = true,
        CommentHeaderGameIndex = -1,
        CommentFooterGameIndex = -1,
    };

    private static CubeRecord BuildCubeRecord(
        BgDecisionData decision, sbyte[] position, int cubeRaw)
    {
        bool isCube = decision.Decision.IsCube;
        return UnanalysedCubeRecord(
            activePlayer: 1,
            position: new PositionEngine { Points = position },
            cubeValueRaw: cubeRaw,
            // Mirrors XG's own pane-state values: -1 for a saved cube
            // problem (DoubleAnalysis fixture), -2 when the position is a
            // play decision and the cube pane is incidental (PlayAnalysis).
            doubled: isCube ? -1 : -2,
            // XG writes "11" as the cube-pane placeholder of a pre-roll
            // position; a play decision carries its real roll.
            diceRolled: isCube ? "11" : $"{decision.Decision.Dice[0]}{decision.Decision.Dice[1]}");
    }

    /// <summary>
    /// XG's incidental / never-analysed cube pane, exactly as real XG
    /// writes it (NoAnalysis / PlayAnalysis fixtures). Shared by the
    /// clean-position path (normalized perspective) and the slice path
    /// (source perspective) — only the pane-state inputs differ.
    /// </summary>
    private static CubeRecord UnanalysedCubeRecord(
        int activePlayer, PositionEngine position, int cubeValueRaw,
        int doubled, string diceRolled) => new()
    {
        EntryType = RecordType.Cube,
        ActivePlayer = activePlayer,
        Doubled = doubled,
        Taken = -1,
        BeaverAccepted = -1,
        RaccoonAccepted = -1,
        CubeValue = cubeValueRaw,
        Position = position,
        Analysis = UnanalysedDoubleAction(),
        ErrorCube = UnanalysedError,
        DiceRolled = diceRolled,
        ErrorTake = UnanalysedError,
        RolloutIndex = -1,
        AnalyzeLevel = -1,
        ErrorBeaver = UnanalysedError,
        ErrorRaccoon = UnanalysedError,
        AnalyzeLevelRequested = -1,
        TutorCube = -1,
        TutorTake = -1,
        ErrorTutorCube = UnanalysedError,
        ErrorTutorTake = UnanalysedError,
        CommentIndex = -1,
    };

    private static MoveRecord BuildMoveRecord(
        BgDecisionData decision, sbyte[] position, int cubeRaw) => new()
    {
        EntryType = RecordType.Move,
        InitialPosition = new PositionEngine { Points = position },
        FinalPosition = new PositionEngine(),   // no play made — XG leaves this zeroed
        ActivePlayer = 1,
        Dice = [decision.Decision.Dice[0], decision.Decision.Dice[1]],
        CubeValue = cubeRaw,
        Analysis = new BestMoveAnalysis { Level = UnanalysedLevel },
        MoveError = UnanalysedError,
        RolloutIndices = [.. Enumerable.Repeat(-1, 32)],
        AnalyzeLevel = -1,
        AnalyzeLevelLuck = -1,
        TutorMoveIndex = -1,
        ErrorTutorMove = UnanalysedError,
        CommentIndex = -1,
    };

    /// <summary>
    /// The "never analysed" cube-analysis block exactly as XG writes it
    /// (NoAnalysis fixture): level −100, IsBeaver −100, all else zero.
    /// </summary>
    private static DoubleActionAnalysis UnanalysedDoubleAction() => new()
    {
        Level = UnanalysedLevel,
        IsBeaver = UnanalysedLevel,
    };

    // ------------------------------------------------------------------ //
    //  Derivation helpers
    // ------------------------------------------------------------------ //

    private static string NameOrDefault(string name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name;

    private static sbyte[] ToPositionEngine(IReadOnlyList<int> mop)
    {
        // On-roll player is written as player 1, so the on-roll-relative
        // board maps onto the record position verbatim.
        var points = new sbyte[26];
        for (int i = 0; i < 26; i++)
        {
            if (mop[i] is < -15 or > 15)
                throw new ArgumentException($"Position.Mop[{i}] = {mop[i]} is not a valid checker count.");
            points[i] = (sbyte)mop[i];
        }
        return points;
    }

    /// <summary>
    /// Encodes cube size + owner into XG's signed-log2 record field:
    /// 0 = centred 1-cube; +n = player 1 (the on-roll player here) owns
    /// 2^n; −n = player 2 owns 2^n.
    /// </summary>
    private static int EncodeCube(int cubeSize, CubeOwner owner)
    {
        int log2 = BitOperations.Log2((uint)cubeSize);
        return owner switch
        {
            CubeOwner.OnRoll => log2,
            CubeOwner.Opponent => -log2,
            _ => 0,
        };
    }

    /// <summary>
    /// Recovers the money-game Jacoby/Beaver flags from XGID field 8
    /// (<c>Jacoby + 2×Beaver</c>) — the only place the pipeline carries
    /// them. Falls back to XG's defaults (Jacoby on, Beaver off) when the
    /// decision carries no parseable XGID.
    /// </summary>
    private static (bool Jacoby, bool Beaver) MoneyFlags(string xgid)
    {
        const string prefix = "XGID=";
        string body = xgid.StartsWith(prefix, StringComparison.Ordinal) ? xgid[prefix.Length..] : xgid;
        string[] fields = body.Split(':');
        if (fields.Length >= 8 && int.TryParse(fields[7], out int cj))
            return ((cj & 1) != 0, (cj & 2) != 0);
        return (true, false);
    }

    /// <summary>
    /// Deterministic stand-in for the random per-save id XG writes:
    /// a CRC32 over the position, dice, and cube encoding. Determinism
    /// keeps exports byte-stable for testing; XG treats the value as opaque.
    /// </summary>
    private static int DeterministicGameId(BgDecisionData decision)
    {
        var bytes = new byte[29];
        for (int i = 0; i < 26; i++)
            bytes[i] = unchecked((byte)decision.Position.Mop[i]);
        bytes[26] = decision.Decision.IsCube ? (byte)0 : (byte)decision.Decision.Dice[0];
        bytes[27] = decision.Decision.IsCube ? (byte)0 : (byte)decision.Decision.Dice[1];
        bytes[28] = unchecked((byte)EncodeCube(decision.Position.CubeSize, decision.Position.CubeOwner));
        return unchecked((int)System.IO.Hashing.Crc32.HashToUInt32(bytes));
    }

    private static string BuildSaveName(
        bool isMoney, int matchLength, int score1, int score2, bool jacoby) =>
        isMoney
            ? jacoby ? "Position:  Unlimited Game, Jacoby" : "Position:  Unlimited Game"
            : $"Position: {matchLength} point match {score1}-{score2}";
}
