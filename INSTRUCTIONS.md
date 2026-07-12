# ConvertXgToJson_Lib

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## Stack

C# / .NET 10 / Class Library / xUnit.

Parses eXtreme Gammon `.xg` and `.xgp` binary files into records defined by
`BgDataTypes_Lib`, and writes XG binary files back out — including `.xgp`
position export from a `BgDecisionData` (the ecosystem's XG-format writer).

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\ConvertXgToJson_Lib\ConvertXgToJson_Lib.slnx`

## Repo

https://github.com/halheinrich/ConvertXgToJson_Lib — branch `main`.

## Depends on

* **BgDataTypes_Lib** — record types produced by this library: `DecisionRow`, `BgDecisionData`, `PositionData`, `DecisionData`, `DescriptiveData`, `PlayCandidate`, `CubeOwner`. The `Move` / `Play` value types also live here, so non-move-gen consumers can use them without dragging in the generator.
* **BgMoveGen** — `MoveNotationFormatter` for rendering candidate plays as standard backgammon notation. `XgMoveTranslator` is the producer-side bridge between XG's raw `sbyte[]` move encoding and the shared `Play` primitive.

## Directory tree

```
ConvertXgToJson_Lib.slnx
ConvertXgToJson_Lib/
  ConvertXgToJson_Lib.csproj
  BackgammonConstants.cs
  MatchContext.cs
  XgDecisionIterator.cs
  XgFileReader.cs
  XgFileWriter.cs
  XgGameInfo.cs
  XgidEncoder.cs
  XgIteratorCallbacks.cs
  XgIteratorState.cs
  XgMatchInfo.cs
  XgMoveTranslator.cs
  XgpExporter.cs
  XgpSliceOptions.cs
  Json/
    XgJsonOptions.cs
  Models/
    Models.cs
  Parsing/
    AfterBoardBuilder.cs
    CommentParser.cs
    PascalBinaryReader.cs
    RichGameHeaderParser.cs
    RolloutContextParser.cs
    SaveRecordParser.cs
    XgDecompressor.cs
    XgMoveEncoding.cs
  Writing/
    CommentWriter.cs
    PascalBinaryWriter.cs
    RichGameHeaderWriter.cs
    RolloutContextWriter.cs
    SaveRecordWriter.cs
    XgContainerWriter.cs
ConvertXgToJson_Lib.Tests/
  ConvertXgToJson_Lib.Tests.csproj
  BoardTests.cs
  DecisionCsvTests.cs
  DiagramRequestIteratorTests.cs
  FileIOCollection.cs
  GlobalUsings.cs
  ReadMatchInfoBenchmarkTests.cs
  RealFileTests.cs
  SaveRecordWriterTests.cs
  TestPaths.cs
  XgDecisionIteratorTests.cs
  XgFileWriterTests.cs
  XgpExporterTests.cs
  XgpExportXgAgreementTests.cs
  XgpSliceExportTests.cs
```

The test directory lists principal classes only; additional `*Tests.cs`
files exist per parser, builder, and analysis surface.

## Architecture

### Pipeline

`XgFileReader` handles binary I/O and zlib decompression; `XgDecisionIterator`
walks the resulting record stream and yields typed rows. Parsing of individual
record payloads lives under `Parsing/` (`SaveRecordParser`,
`RichGameHeaderParser`, `RolloutContextParser`, `CommentParser`,
`PascalBinaryReader`, `XgDecompressor`).

### XgFileReader

File discovery (which paths are XG-format input):

* `XgFormatExtensions` — the canonical extension set `[".xg", ".xgp"]`, in
  that order. Single source of truth for the two members below.
* `IsXgFormatFile(path)` — case-insensitive extension check against
  `XgFormatExtensions`. Pure path inspection; the file need not exist.
* `EnumerateXgFormatFiles(directory)` — yields all `.xg` then all `.xgp`
  files (filesystem order within each), one `Directory.EnumerateFiles` pass
  per extension rather than a `*.xg*` glob. This is the single producer-side
  copy of the discovery rule; `IterateXgDirectory` routes through it.
* `EnumerateXgFormatFiles(directory, searchOption)` — recursive-capable
  overload; filters through `IsXgFormatFile`, so `XgFormatExtensions`
  stays the discovery SSOT. **Enumeration order is deterministic and
  contractual:** ascending full path, `OrdinalIgnoreCase` with an
  `Ordinal` tiebreak — independent of filesystem walk order and culture,
  so consumers may pin user-visible sequencing (e.g. export numbering) to
  it. Extensions interleave by path: deliberately different from the
  single-arg overload's historical extension-major order (the divergence
  is documented on both overloads). Sorting materializes the matching
  paths on first enumeration; directory-access errors stay deferred.

Entry points:

* `ReadFile` — full parse of a `.xg` or `.xgp` file, yielding all records.
  Format is detected from file content, so `IterateXgDirectory` routes both
  extensions through it uniformly.
* `ReadMatchInfo` — fast path. Reads only the first zlib stream and stops at
  the `MatchHeaderRecord`. Used when a caller only needs match metadata.
* `ReadGameHeaders` — fast path. Reads the first zlib stream and yields
  `XgGameInfo` entries; populates `XgIteratorState.MatchInfo` before the
  first yield. To stop early, the consumer breaks out of the foreach;
  disposing the enumerator stops further yields. There is no imperative
  skip flag.

### Writing (XgFileWriter / XgpExporter)

The reader's mirror. Layered exactly like the read path:

* `Writing/` internals mirror `Parsing/` file-for-file:
  `PascalBinaryWriter` (alignment-mirroring primitive writes; padding is
  explicit zeros), `SaveRecordWriter` (all six TSaveRec variants → complete
  zero-padded 2560-byte records), `RolloutContextWriter` (2184-byte records),
  `CommentWriter` (CRLF lines, `#1#2` escape), `RichGameHeaderWriter`
  (8232-byte packed outer header, thumbnail always omitted — the model does
  not carry its bytes), `XgContainerWriter` (concatenated zlib streams plus
  the trailing manifest).
* **`XgFileWriter`** (public) — record-level serializer: `XgFile` →
  stream / bytes / file. Format-generic by construction (it writes whatever
  record list the model holds, so full-`.xg` output works), but only the
  `.xgp` shape is validated against real XG imports.
* **`XgpExporter`** (public) — decision-level export, two surfaces chosen
  by what the caller holds (see below). "Exporter" per the ecosystem
  convention (MatExporter precedent): an Exporter translates semantics, a
  Writer/Reader mirrors byte layout. Consumers never touch record
  internals.

Container facts the reader never needed (writer-only knowledge, decoded from
the fixture corpus and pinned by `XgFileWriterTests`):

* Physical stream order is `temp.xg`, `temp.xgr` (only when rollouts exist),
  `temp.xgi`, `temp.xgc` (only when comments exist), then a manifest stream.
* The manifest is one 532-byte entry per inner file, in stream order, the
  manifest itself unlisted: Pascal ANSI filename padded to 512 bytes, then
  uncompressed size, compressed size, offset relative to content start,
  CRC32 (IEEE) of the uncompressed bytes, and constant `0x200`.
* `temp.xgi` holds exactly two records: byte-copies of the first and last
  records of the emitted stream. (Real XG writes its session's first/last —
  the "last" often isn't in the `.xgp` at all — so XG demonstrably does not
  validate the pair; self-consistent first/last is the clean choice.)
* Real XG stamps one constant GUID into every `.xgp` RichGameHeader
  (`2f5af5e1-e021-4832-a423-ef480ec58a0b`, stable 2010→current); the
  exporter reproduces it.

**Clean-position export** (`Write(BgDecisionData, …)`) — for callers that
hold only the consumer-level decision record (JSON-sourced). Emits a
**clean unanalyzed position**: match header + game header (game "starts"
at the saved position, XG's position-editor pattern) + cube record, plus a
move record carrying the dice when the decision is a play. Analysis blocks
hold XG's own never-analysed sentinels (`Level = -100`, errors `-1000`).
XG re-analyzes on import. Money games recover Jacoby/Beaver from XGID
field 8 when the decision carries an XGID, else default to XG's money
defaults (Jacoby on, Beaver off). On-roll player is normalized to
player 1. Output is byte-deterministic — no timestamps or random ids.
Clean exports **self-identify** via the match header's Location fields
(`"ConvertXgToJson_Lib"`) — Location is the ecosystem's producer
fingerprint (Galaxy writes `"BackgammonGalaxy"` there and
`IsGalaxyMoneyGame` keys on it), so the string is provenance and a stable
hook for ever special-casing our own exports; treat changing it as a
breaking change (`Export_SelfIdentifiesInLocation` pins it).

**Slice export** (`Write(XgFile, game, moveNumber, isCube, …)`) — for
callers that hold the parsed source file plus the decision's coordinates
(the same user-level selectors an `XgDecisionId` carries). Emits XG's own
save-from-match shape, learned from the XG-authored agreement fixtures:
match header and game header shared **verbatim** (source perspective,
player order, and metadata preserved — including a foreign Location like
`"BackgammonGalaxy"`), decision records copied **with analysis panes
intact**, referenced rollout contexts carried over with indices remapped
(a cube rollout is an adjacent context *pair* with the record pointing at
the second leg — both legs travel), comment indices cleared (comments not
carried). A play slice carries the real same-turn cube record when the
source has one, else synthesizes the incidental unanalysed pane.

Slice entry points also accept the decision's `XgDecisionId` directly
(destructured internally; `Filename` is not consulted — the caller
already resolved the source from it; the parameter is
`XgDecisionId`-typed so routing an `XgpDecisionId` there is a compile
error, and the Xgp-vs-Xg routing stays with the caller). An optional
`XgpSliceOptions` overrides the player names: each non-null override
rewrites that player's Unicode field *and* its ANSI twin (writer
truncation 128 chars / 40 bytes; non-Latin1 characters degrade to `?`
in the twin via the Latin1 replacement fallback), while every other
header field — Location provenance included — still passes through
verbatim. `XgpSliceOptions.Anonymized` ("Player 1" / "Player 2") is the
SSOT for what anonymized export means; overrides validate non-empty at
init, so an invalid options instance is unrepresentable.

**Iterator visibility differs by path, deliberately.** A clean export is
**XG-import-only**: unanalyzed, so this library's own iterator yields
**zero** decisions for it (rule 1 of the `.xgp` emission policy); the
ecosystem's re-ingestible format for analysis-less decisions remains
`BgDecisionData` JSON. A sliced analyzed decision is **visible** — exactly
one decision, analysis and rollout depth intact — because the panes travel
with it.

Ground-truth oracle beyond round-trips: `XgpExportXgAgreementTests`
compares exports field-level against two XG-authored `.xgp` saves of
decisions whose source `.xg` is also pinned (`MTCH4064_1_22.xgp`, a play;
`match35253054_2_37.xgp`, a rolled-out cube saved with player 2 on roll).
Clean-path comparisons run through the on-roll lens (XG preserves source
perspective, the clean path normalizes); slice comparisons are direct
record equivalence — analysis included — with one documented exclusion:
the tutor family (`ErrorTutor*`, `TutorPosition`), which XG initializes at
runtime while imported sources carry zeros. Never byte identity.

### XgDecisionIterator

Two iteration surfaces over the same underlying record stream:

* `Iterate` yields flat `DecisionRow` records (one per play or cube decision,
  CSV-shaped).
* `IterateDiagramRequests` yields `BgDecisionData` records. Cube decisions
  yield exactly **one** `BgDecisionData` per decision (see "Cube decisions"
  below for the producer-perspective contract).

Every emitted `DecisionRow` / `BgDecisionData` is stamped with a
`BgDataTypes_Lib.DecisionId` in its `Id` field. The stamp is built by the
internal `BuildDecisionId(sourceFile, game, moveNumber, isCube)` helper
called from each of the four `Build*` sites (`BuildMoveRow`,
`BuildMoveDiagramRequest`, `BuildCubeRows`, `BuildCubeDiagramRequests`).
Extension dispatch is case-insensitive invariant:

* `.xg` and `.json` → `XgDecisionId(Filename, Game, MoveNumber, IsCube)`
  — the multi-decision tuple shape. `.json` is treated as an
  XG-format-equivalent serialization (produced by
  `XgFileReader.WriteJsonAsync`, consumed via `XgFileReader.ReadJson`);
  record-level structure is identical to `.xg`, so the same Id shape
  applies. The resulting Id's `Filename` ends in `.json` by design — a
  `.xg` and a `.json` of the same content are distinct on-disk artifacts
  and legitimately carry different Ids. Cross-format Id identity is not
  an invariant; this parallels the existing `.xg` ↔ `.xgp` asymmetry
  (same decision, different storage shape, different Id).
* `.xgp` → `XgpDecisionId(Filename)` — filename-only; within-file
  coordinates are not part of the Id. XG itself does *not* guarantee one
  decision per `.xgp`: it always writes a cube pane alongside the move
  pane, and a position saved after the dice were rolled can carry analysis
  in both. What makes the bare filename a valid key is the iterator's
  emission policy — an `.xgp` yields **at most one** decision: the analysed
  checker-play if there is one, else the analysed cube. The move pane exists
  only because dice were rolled, so dice in the file mean the saved decision
  is the play; the cube pane is XG's incidental. Depth is not compared. A
  curated cube problem is a pre-roll position and carries no move pane at
  all.

Cube emissions stamp with `ctx.MoveNumber + 1` so the Id's `MoveNumber`
agrees with the emitted `DecisionRow.MoveNumber` /
`DescriptiveData.MoveNumber`, not the raw underlying counter. The
contract is that the Id's coordinate tuple matches the emitted row's
published fields, record-for-record.

`IterateXgDirectory` is the directory-level entry point: it enumerates
both `*.xg` (match files) and `*.xgp` (position files) — both formats
are XG-native and `XgFileReader` handles them uniformly, so callers
that point at a directory of mixed XG content get all decisions
regardless of extension. File discovery is delegated to
`XgFileReader.EnumerateXgFormatFiles` (see XgFileReader above) — the
single source of the `.xg`-then-`.xgp` rule. `IterateJsonDirectory` is
the parallel entry point for `*.json` exports.

Both surfaces report the "best play" as the **highest-equity** candidate in
`analysis.Evals[]`, not XG-native rank 0. `BgDecisionData.Plays[0]`,
`DecisionRow.Equity`, and `PlayOutcomeData.AfterBestBoard` all key off this
convention. XG's stored ranking is not always strict equity-descending, so
rank 0 and best-by-equity disagree on a subset of decisions. Use
`FindBestByEquityIndex(analysis)` to locate the best candidate when adding
code that reads from `analysis.Evals`, `analysis.Moves`,
`analysis.PositionsPlayed`, or `analysis.EvalLevels`. All four arrays
are rank-coupled with the same index.

Supporting helpers:

* `ExtractMatchInfo` — public helper that scans for the first
  `MatchHeaderRecord` and returns an `XgMatchInfo`, or `null` if no
  match header is present. Callers must handle the no-header case
  explicitly; `Iterate` / `IterateDiagramRequests` translate `null` into
  a thrown `InvalidDataException` at the iteration boundary rather than
  silently emitting decisions against a default-constructed header.
* `ToBoard` — converts a position to the 26-element board array from the
  on-roll player's perspective (see "Board format" below).
* `FlipPosition` — flips the position to bottom-player perspective for XGID
  encoding.
* `CubeValueActual` — internal static helper, called from `MatchContext`
  and `XgDecisionIterator`'s cube-row / cube-diagram builders.

### XgIteratorState

Pure read-only observer of producer-internal iteration state. Inspect for
per-row context; carries no caller-mutable surface.

* `MatchInfo` — populated by the iterator at the file boundary (start of
  every `Iterate` / `IterateDiagramRequests` invocation, including the
  per-file calls inside the directory walks).
* `GameInfo` — populated by the iterator at each new `GameHeaderRecord`.
  Reset to null at each file boundary.

Skip semantics — "skip this match", "skip this game", "stop after this
row" — live separately on `XgIteratorCallbacks` (see below). The state
type does not participate in iteration control.

### XgIteratorCallbacks

Optional predicate record supplied at call time to
`Iterate` / `IterateDiagramRequests` / `IterateXgDirectory` /
`IterateJsonDirectory`. Four predicates, each null by default:

* `SkipMatchAt(XgMatchInfo) → bool` — fires once per match at the match
  header. True = skip the entire match before any row yields.
* `SkipGameAt(XgGameInfo) → bool` — fires once per game at the game
  header. True = skip the rest of the current game before any row yields.
* `StopGameAfter(IDecisionFilterData) → bool` — fires after each yielded
  decision. True = advance to the next game.
* `StopMatchAfter(IDecisionFilterData) → bool` — fires after each yielded
  decision. True = advance to the next match.

Both `DecisionRow` and `BgDecisionData` implement `IDecisionFilterData`,
so the post-yield predicates work uniformly across both iterator surfaces.

### XgMoveTranslator

Internal static helper that converts the 8-element `sbyte[]` move
encoding XG stores in `BestMoveAnalysis.Moves[i]` into a
`BgDataTypes_Lib.Play`. Hits are pre-encoded into
`BgDataTypes_Lib.Move.ToPt`'s sign so
`BgMoveGen.MoveNotationFormatter.Format(Play)` can render notation
without seeing a board. The translator also performs the
on-roll-board mutation (sending hit blots to the bar). Its output is
consumed once per candidate by `BuildMoveDiagramRequest` and feeds
both `PlayCandidate.MoveNotation` (rendered) and `PlayCandidate.Play`
(structural). One producer call per candidate; one scratch-board
mutation. Point-index decoding (`from == 24` bar entry, `to < 0`
bear off including XG overshoot encodings) is shared with
`Parsing/AfterBoardBuilder` via `Parsing/XgMoveEncoding`. The
`from == -1` terminator is loop control kept by each consumer. The
`(0, 0)` "dance" sentinel is
**not** recognized at this layer; sentinel-only emission is gated
upstream — see Pitfalls.

### MatchContext

Internal class tracking match and game state during iteration. Exposes
match-length / score / cube state, plus `NeedsFor(activePlayer)`,
`PlayerName(activePlayer)`, and the `XgidCrawfordJacobyField` wire-format
helper consumed by `XgidEncoder`.

### BackgammonConstants

Shared backgammon constants and stateless helpers.

* `StandardOpeningPosition` — `internal static readonly sbyte[26]` holding
  the standard starting position in the canonical 26-point layout.
* `IsStandardOpeningPosition` — comparison helper against that constant.
* `Flip<T>` — single source of the perspective flip: mirror index `i`
  with `25 - i`, negate every value. Generic (`T : INumber<T>`) so it
  serves both `sbyte` position arrays and `int` board arrays.
* `AwayScore` — single source of the away-score rule
  (`matchLength - score`, 0 for money games).

### Board format

26-element array from the **on-roll player's** perspective throughout the
pipeline (matches `BgDataTypes_Lib.PositionData.Mop`):

* `[0]` = opponent's bar (≤ 0)
* `[1–24]` = points 1–24
* `[25]` = on-roll player's bar (≥ 0)
* Positive = on-roll player; negative = opponent.

### XGID encoding

XGIDs are always normalized to **bottom-player** perspective. The iterator
applies `FlipPosition` before handing the position to `XgidEncoder` — this is
a separate convention from the on-roll-relative board layout above.

### Cube decisions

Both `Iterate` and `IterateDiagramRequests` emit exactly **one** row per
cube decision — a single `DecisionRow` or `BgDecisionData` carrying the
doubler's board (no flip). Cube-side equity and error fields
(`analysis.EquityNoDouble`, `analysis.EquityDoubleTake`, `cube.ErrorCube`,
`cube.ErrorTake`) are written from the doubler's perspective; there is
no second taker-perspective row.

### `.xgp` file handling

`.xgp` files (positions-only) encode "no analysis" differently from `.xg`
match files:

* `MoveError` and `ErrorCube` use sentinel value `-1000` to mean "unanalyzed".
* `IsAnalysed` is gated on the analysis-level field, not on error presence.
* Error fields are treated as present when `> -999.0` (anything above the
  sentinel).
* `UserPlayError`, `UserDoubleError`, `UserTakeError` are populated from the
  raw XG fields with sentinel guards.
* `PlayCandidate` win / gammon / backgammon probabilities are populated from
  `EvalResult`.

### TestData

* Shared at `backgammon\TestData`. `TestPaths._root` resolves it by
  walking up from `AppContext.BaseDirectory` to the repo root.
* All file-touching tests use `[Collection("FileIO")]`.

## Public API

```csharp
public static class XgFileReader
{
    // File discovery
    public static IReadOnlyList<string>   XgFormatExtensions { get; }   // [".xg", ".xgp"]
    public static bool                    IsXgFormatFile(string path);
    public static IEnumerable<string>     EnumerateXgFormatFiles(string directory);
    public static IEnumerable<string>     EnumerateXgFormatFiles(string directory, SearchOption searchOption);

    // Full parse (.xg / .xgp — format detected from content)
    public static XgFile                ReadFile(string path);
    public static XgFile                ReadStream(Stream stream);

    // JSON serialization round-trip. ReadJson is load-bearing:
    // XgDecisionIterator.IterateJsonDirectory parses each export through it.
    public static string                ToJson(XgFile file, JsonSerializerOptions? options = null);
    public static string                ReadFileAsJson(string path, JsonSerializerOptions? options = null);
    public static Task                  WriteJsonAsync(XgFile file, string outputPath,
                                            JsonSerializerOptions? options = null,
                                            CancellationToken cancellationToken = default);
    public static Task                  ReadFileToJsonFileAsync(string inputPath, string outputPath,
                                            JsonSerializerOptions? options = null,
                                            CancellationToken cancellationToken = default);
    public static XgFile                ReadJson(string path);

    // Fast paths (first zlib stream only)
    public static XgMatchInfo?          ReadMatchInfo(string path);
    public static IEnumerable<XgGameInfo> ReadGameHeaders(string path, XgIteratorState state);
}

public static class XgFileWriter
{
    // Record-level serializer (reader's mirror). Semantic round-trip, not
    // byte identity: ReadStream(Write(f)) parses to an equal model.
    public static void   Write(XgFile file, Stream output);
    public static byte[] ToBytes(XgFile file);
    public static void   WriteFile(XgFile file, string path);
}

public static class XgpExporter
{
    // Clean-position path (caller holds only the decision record):
    // unanalyzed position, XG-import-only — the iterator yields zero
    // decisions for these exports, by design.
    public static void   Write(BgDecisionData decision, Stream output);
    public static byte[] ToBytes(BgDecisionData decision);
    public static void   WriteFile(BgDecisionData decision, string path);

    // Slice path (caller holds the parsed source file + the decision's
    // XgDecisionId coordinates): analysis carried through — the iterator
    // yields exactly one decision for a sliced analyzed decision.
    public static void   Write(XgFile source, int game, int moveNumber, bool isCube, Stream output);
    public static byte[] ToBytes(XgFile source, int game, int moveNumber, bool isCube);
    public static void   WriteFile(XgFile source, int game, int moveNumber, bool isCube, string path);

    // Slice path with options (player-name overrides; everything else
    // still verbatim).
    public static void   Write(XgFile source, int game, int moveNumber, bool isCube, XgpSliceOptions options, Stream output);
    public static byte[] ToBytes(XgFile source, int game, int moveNumber, bool isCube, XgpSliceOptions options);
    public static void   WriteFile(XgFile source, int game, int moveNumber, bool isCube, XgpSliceOptions options, string path);

    // Slice path addressed by the iterator-stamped XgDecisionId
    // (compile-time contract — XgpDecisionId has no coordinates and does
    // not fit; id.Filename is not consulted).
    public static void   Write(XgFile source, XgDecisionId id, Stream output);
    public static void   Write(XgFile source, XgDecisionId id, XgpSliceOptions options, Stream output);
    public static byte[] ToBytes(XgFile source, XgDecisionId id);
    public static byte[] ToBytes(XgFile source, XgDecisionId id, XgpSliceOptions options);
    public static void   WriteFile(XgFile source, XgDecisionId id, string path);
    public static void   WriteFile(XgFile source, XgDecisionId id, XgpSliceOptions options, string path);
}

public sealed record XgpSliceOptions
{
    // null = keep the source name; non-empty enforced at init (an invalid
    // instance is unrepresentable). Each override rewrites that player's
    // Unicode name field and its ANSI twin.
    public string? Player1Name { get; init; }
    public string? Player2Name { get; init; }

    // SSOT for anonymized export: "Player 1" / "Player 2".
    public static XgpSliceOptions Anonymized { get; }
}

public static class XgDecisionIterator
{
    public static IEnumerable<DecisionRow> Iterate(
        XgFile file, string? sourceFile,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null);

    public static IEnumerable<BgDecisionData> IterateDiagramRequests(
        XgFile file, string? sourceFile,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null);

    public static IEnumerable<DecisionRow> IterateXgDirectory(
        string xgDir,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null);

    public static IEnumerable<DecisionRow> IterateJsonDirectory(
        string jsonDir,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null);

    public static XgMatchInfo? ExtractMatchInfo(XgFile file);
}

public sealed class XgIteratorState
{
    public XgMatchInfo? MatchInfo { get; internal set; }
    public XgGameInfo?  GameInfo  { get; internal set; }
}

public sealed record XgIteratorCallbacks(
    Func<XgMatchInfo, bool>?          SkipMatchAt    = null,
    Func<XgGameInfo,  bool>?          SkipGameAt     = null,
    Func<IDecisionFilterData, bool>?  StopGameAfter  = null,
    Func<IDecisionFilterData, bool>?  StopMatchAfter = null);

public sealed class XgMatchInfo { /* match-level metadata */ }
public sealed class XgGameInfo  { /* game-level metadata  */ }
```

Produces types defined in `BgDataTypes_Lib`; see that subproject's
`INSTRUCTIONS.md` for their shapes and serialization contract.

## Pitfalls

* **Two perspectives, don't confuse them.** The board array is always
  on-roll-relative. The XGID is always bottom-player-relative. `FlipPosition`
  converts between them and is applied only at the XGID encoding boundary.
* **Cube and play decisions are 1:1 with emitted rows.** Both `Iterate`
  and `IterateDiagramRequests` produce exactly one `DecisionRow` /
  `BgDecisionData` per analyzed decision. For cube decisions this is
  the doubler's board (no flip); there is no second taker-perspective
  row. Consumers may safely count one row per decision.
* **`.xgp` sentinel handling is easy to regress.** `-1000` means "unanalyzed"
  for `MoveError` / `ErrorCube`; anything `> -999.0` is a real error. Using
  `!= 0` or `.HasValue` checks on raw fields will silently treat unanalyzed
  positions as zero-error.
* **Cube `IsAnalysed` must gate on `Analysis.Level`, not `LevelRequest`.** XG
  writes `LevelRequest` when the user *asks* for an analysis and `Level` when
  it *runs*. An `.xgp` saved before the requested rollout completes has
  `Level == -100` (queued) but a non-zero `LevelRequest`. Using `||` between
  the two re-admits these phantom cubes and yields rows with empty equity /
  eval fields.
* **`BuildMoveDiagramRequest` returns `null` on three conditions:**
  `analysis.MoveCount == 0`, `analysis.Evals.Length == 0`, or `dice == 0`.
  The iterator's call site gates emission on the non-null return; preserve
  this null-check when refactoring the move-diagram path.
* **`StopGameAfter` / `StopMatchAfter` fire *after* the yield.** The
  consumer sees the just-yielded row, *then* the predicate runs on the
  producer's next `MoveNext`. To suppress a row entirely (skip the game
  or match before any decision is emitted from it), return `true` from
  `SkipGameAt` / `SkipMatchAt` at the boundary instead. The post-yield
  predicates exist for "I've seen enough" early-exit, not pre-filtering.
* **`TestPaths._root` depends on a specific build output depth.** If
  `AppContext.BaseDirectory` moves relative to the repo root (e.g. a csproj
  layout change), the five-`..` walk breaks and every file-touching test
  fails. Fix by adjusting `TestPaths`, not by moving `TestData`.
* **Null `sourceFile` is rejected eagerly, not deferred.** `Iterate`
  and `IterateDiagramRequests` throw `InvalidOperationException`
  synchronously at the call site when `sourceFile` is null — before
  any deferred enumeration begins. This is the LINQ-style two-method
  pattern: the public surface validates and delegates to
  `IterateCore` / `IterateDiagramRequestsCore`, whose signatures carry
  the non-nullable post-validation invariant. Required because every
  yielded row carries a `DecisionId` stamped from `sourceFile`. The
  public parameter remains typed `string?` for source-compat with
  method-group conversions in `XgFilter_Lib.FilteredDecisionIterator`
  (which uses `Func<XgFile, string?, …>` delegate slots); the runtime
  contract is strictly non-null. Distinct from the
  `InvalidDataException` for missing match headers below — that throw
  is content-level and remains deferred to first `MoveNext`; this one
  is caller-contract and fires immediately. Unsupported source-file
  extensions (anything other than `.xg`, `.xgp`, or `.json`) throw
  `InvalidOperationException` from `BuildDecisionId` on first stamp;
  that path is deferred (it fires during enumeration, when a candidate
  reaches a `Build*` site) and is enforced per-record rather than at
  the API boundary.
* **Iteration throws on malformed match headers.** Both `Iterate` and
  `IterateDiagramRequests` throw `InvalidDataException` when
  `ExtractMatchInfo` returns `null` — files without a readable match
  header are not silently processed with default player names and a
  zero-length match. The throw is paired with `MatchContext`'s
  pre-existing `InvalidDataException` on `records[0] is not
  MatchHeaderRecord`, which fires first on standard fixtures and in
  practice shadows the iteration-boundary throw. The iteration-boundary
  throw is the contract-correct fallback for the more permissive scan
  ordering of `ExtractMatchInfo`. `ExtractMatchInfo` finds a header at
  any position; `MatchContext` requires one at index 0. Consumers
  iterating directories of unknown files must catch this if they want
  log-and-skip semantics; the producer's `Iterate*Directory` helpers
  swallow only `XgFileReader.ReadFile` failures, not iterator-time
  errors.
* **Sentinel-only analyses are filtered at the iterator boundary, not in
  the leaves.** XG emits two known patterns where the lone "candidate" in
  `BestMoveAnalysis.Moves[best]` is a non-play sentinel pair:
  `(-100, -100)` is XG's *illegal-play workaround* (the recorded play in
  the source file is illegal, XG forces the next position rather than
  refusing to load), and `(0, 0)` is XG's *no-legal-move* (dance)
  encoding. Neither is of interest downstream — there is no real
  candidate to evaluate — and feeding either to leaf computation has
  historically produced an `IndexOutOfRangeException` (the `(-100, -100)`
  case in `AfterBoardBuilder.ComputeAfterBoard`) or a "1/1" notation
  glitch (the `(0, 0)` case in `XgMoveTranslator.Translate`). Both surfaces
  (`Iterate` and `IterateDiagramRequests`) gate emission through
  `IsSentinelOnlyAnalysis` so neither leaf ever sees a sentinel. The
  existing `(0, 0)` no-op branch in `AfterBoardBuilder` is retained for
  defense in depth; it is unreachable on the standard iterator path but
  still exercised by `AfterBoardBuilderTests`. Do not add a `(-100, -100)`
  branch to either leaf. The encapsulation principle is that sentinel
  semantics belong with the iterator that decides what to emit, not with
  the leaf that operates on the resulting move encoding.
* **The TSaveRec byte layout is encoded twice — parser and writer.** A
  deliberate serializer duality: `Parsing/SaveRecordParser` and
  `Writing/SaveRecordWriter` (likewise the rollout, comment, and outer-header
  pairs) must change together, field-for-field and alignment-for-alignment.
  The guard is `SaveRecordWriterTests` (distinct value in every field, so a
  transposition cannot cancel) plus the corpus round-trip in
  `XgFileWriterTests`. Do not "single-source" this with a declarative field
  map — evaluated and rejected as over-engineering for Pascal variant
  records.
* **Clean-path exports yield zero iterator decisions — by design.**
  `XgpExporter`'s `BgDecisionData` path writes clean unanalyzed positions;
  rule 1 of the `.xgp` emission policy ("skip unanalysed") makes them
  invisible to `Iterate` / `IterateDiagramRequests`. The zero-rows
  assertions in `XgpExporterTests` pin that XG-import-only boundary — do
  not "fix" them to expect one row. Callers that want iterator-visible
  analyzed exports use the **slice path** (`Write(XgFile, game,
  moveNumber, isCube, …)`), which carries the analysis panes and emits
  exactly one decision.
* **`WithPlayerNames` is a full manual copy of `MatchHeaderRecord`.** The
  slice exporter's name-override helper copies every header field
  explicitly (the model is a class, not a record — no `with`). A new
  `MatchHeaderRecord` field must be added to the copy too, or slice
  exports with name overrides silently drop it. The guard is the
  byte-identity test in `XgpSliceExportTests`
  (`SliceOptions_MatchingSourceNames_AreByteInvisible`): overrides equal
  to the source names must produce a byte-identical file, so any dropped
  or transposed field fails it.
* **A cube rollout is an adjacent context pair; the record points at the
  second leg.** Ground truth from XG's own save (`match35253054_2_37.xgp`:
  two contexts, `RolloutIndex = 1`). Anything that copies or filters
  rollout tables for cube decisions must carry both legs and keep them
  adjacent and in order — carrying only `rollouts[RolloutIndex]` silently
  drops the companion leg. The slice exporter's `RemapCubeRollout` is the
  in-tree reference; move-candidate rollouts are individually indexed and
  have no pair rule.
* **`TDateTime` is a double of days — only quantized dates round-trip
  tick-for-tick.** Dates parsed from real files are already double-quantized
  and round-trip exactly; a synthetic `DateTime` in a writer test must use a
  binary-exact day fraction (midnight, noon, 18:00) or the re-read value can
  differ by a tick or two.
* **The RichGameHeader is packed; the container manifest is writer-only
  knowledge.** `ThumbnailOffset` (an Int64 at offset 12) must be written raw
  — an aligned 8-byte write would insert padding and corrupt the header. And
  because `XgFileReader` finds streams by scanning for zlib headers, it
  ignores the trailing manifest entirely; a reader-level round-trip passes
  even with a corrupt manifest. Real XG is presumed to consume it, so
  `XgFileWriterTests` asserts sizes / offsets / CRC32s against the raw
  written bytes — keep that test when refactoring the container writer.
* **A centred cube above 1 is not exportable.** The record encodes cube
  ownership in the sign of a log2 field, so "centred, above 1" (auto-doubled
  money positions) has no representation without XG's auto-double
  bookkeeping; `XgpExporter` throws `NotSupportedException` rather than
  misencode.
* **Backgammon Galaxy money games are detected and repaired at parse
  time.** Galaxy exports money games by abusing `MatchLength` as a
  cube-size limit (a real, even value) and setting an illegal Crawford
  flag, rather than writing XG's `99999` money sentinel.
  `SaveRecordParser.IsGalaxyMoneyGame` detects them — ANSI location
  `BackgammonGalaxy` (ordinal, trimmed), even `MatchLength`, `Crawford`
  set — and the match-header parser then rewrites `MatchLength` to
  `99999` (XG's canonical money sentinel) and sets `IsMoneyMatch = true`
  on the `MatchHeaderRecord`. Past the parser a Galaxy money game is
  indistinguishable from a native XG money game: one money representation
  on the record, normalized to `0` downstream by the existing
  `>= 99999 ? 0` checks in `XgMatchInfo.From` and `MatchContext`. One
  consequence for consumers: `MatchHeaderRecord.IsMoneyMatch` is *not*
  the raw XG byte — it is that byte OR'd with Galaxy detection.

## Subproject-internal next steps

* **Unify `EnumerateXgFormatFiles` ordering** — the single-arg overload
  keeps its historical extension-major, filesystem-order contract while
  the `SearchOption` overload sorts by full path (ordinal-insensitive,
  deterministic). Once ExtractFromXgToCsv consolidates its four private
  discovery copies onto the sorted overload, consider routing the
  single-arg form through `(directory, TopDirectoryOnly)` so the class
  carries one order contract. Deliberate behavior change, not a drive-by:
  it alters `IterateXgDirectory`'s file order — its own session.
* **Analysis carry-through landed as the slice exporter** (`XgpExporter`'s
  `XgFile` + coordinates surface) — the original "Option B"
  (reconstructing `BestMoveAnalysis` / `DoubleActionAnalysis` from
  `PlayCandidate` / `DecisionData`) is superseded for every caller that
  holds the parsed source file, and its open rollout-depth policy question
  dissolved: the slice carries the real rollout contexts, nothing is
  fabricated. Reconstruction remains *possible* if a JSON-sourced caller
  (holding only `BgDecisionData`) ever needs analyzed exports — unbooked;
  revisit only when such a caller exists. Feasibility notes preserved:
  eval vectors and per-candidate probabilities/equities are all in
  `BgDecisionData`; after-boards recomputable; the sbyte move encoding
  invertible; static levels invert exactly via the `LevelInfo` taxonomy;
  rollout contexts are the one unrecoverable piece (level 1002 with
  `RolloutIndex = -1` is XG-legal per the `DoubleAnalysis.xgp` fixture,
  unverified for move panes).
