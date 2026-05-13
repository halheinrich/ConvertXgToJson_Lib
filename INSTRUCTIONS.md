# ConvertXgToJson_Lib

> Session conventions: [`../CLAUDE.md`](../CLAUDE.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## Stack

C# / .NET 10 / Class Library / xUnit.

Parses eXtreme Gammon `.xg` and `.xgp` binary files into records defined by `BgDataTypes_Lib`.

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
  XgGameInfo.cs
  XgidEncoder.cs
  XgIteratorCallbacks.cs
  XgIteratorState.cs
  XgMatchInfo.cs
  XgMoveTranslator.cs
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
ConvertXgToJson_Lib.Tests/
  ConvertXgToJson_Lib.Tests.csproj
  BoardTests.cs
  DecisionCsvTests.cs
  DiagramRequestIteratorTests.cs
  FileIOCollection.cs
  GlobalUsings.cs
  ReadMatchInfoBenchmarkTests.cs
  RealFileTests.cs
  TestPaths.cs
  XgDecisionIteratorTests.cs
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

Entry points:

* `ReadFile` — full parse of a `.xg` file, yielding all records.
* `ReadMatchInfo` — fast path. Reads only the first zlib stream and stops at
  the `MatchHeaderRecord`. Used when a caller only needs match metadata.
* `ReadGameHeaders` — fast path. Reads the first zlib stream and yields
  `XgGameInfo` entries; populates `XgIteratorState.MatchInfo` before the
  first yield. To stop early, the consumer breaks out of the foreach;
  disposing the enumerator stops further yields. There is no imperative
  skip flag.

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
* `.xgp` → `XgpDecisionId(Filename)` — filename-only. `.xgp` files are
  single-decision-per-file by XG's design, so within-file coordinates
  are not part of the Id.

Cube emissions stamp with `ctx.MoveNumber + 1` so the Id's `MoveNumber`
agrees with the emitted `DecisionRow.MoveNumber` /
`DescriptiveData.MoveNumber`, not the raw underlying counter. The
contract is that the Id's coordinate tuple matches the emitted row's
published fields, record-for-record.

`IterateXgDirectory` is the directory-level entry point: it enumerates
both `*.xg` (match files) and `*.xgp` (position files) — both formats
are XG-native and `XgFileReader` handles them uniformly, so callers
that point at a directory of mixed XG content get all decisions
regardless of extension. `IterateJsonDirectory` is the parallel
entry point for `*.json` exports. The implementation uses two explicit
`Directory.EnumerateFiles` calls rather than a `*.xg*` glob — the broader
pattern would also match hypothetical `.xgz` / `.xgr` files we don't
assume are XG-format.

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
mutation. Sentinel handling matches `Parsing/AfterBoardBuilder`
(`from == -1` terminator, `from == 24` bar entry, `to < 0` bear off
including XG overshoot encodings). The `(0, 0)` "dance" sentinel is
**not** recognized at this layer; sentinel-only emission is gated
upstream — see Pitfalls.

### MatchContext

Internal class tracking match and game state during iteration. Exposes
match-length / score / cube state, plus `NeedsFor(activePlayer)`,
`PlayerName(activePlayer)`, and the `XgidCrawfordJacobyField` wire-format
helper consumed by `XgidEncoder`.

### BackgammonConstants

* `StandardOpeningPosition` — `internal static readonly sbyte[26]` holding
  the standard starting position in the canonical 26-point layout.
* `IsStandardOpeningPosition` — comparison helper against that constant.

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
    public static XgFile                ReadFile(string path);
    public static XgMatchInfo?          ReadMatchInfo(string path);
    public static IEnumerable<XgGameInfo> ReadGameHeaders(string path, XgIteratorState state);
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

## Subproject-internal next steps

None.
