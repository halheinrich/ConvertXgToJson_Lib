# ConvertXgToJson_Lib

> Session conventions: [`../CLAUDE.md`](../CLAUDE.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## Stack

C# / .NET 10 / Class Library / xUnit. Parses XG Gammon `.xg` and `.xgp` binary files into records defined by `BgDataTypes_Lib`.

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\ConvertXgToJson_Lib\ConvertXgToJson_Lib.slnx`

## Repo

https://github.com/halheinrich/ConvertXgToJson_Lib — branch `main`.

## Depends on

* **BgDataTypes_Lib** — record types produced by this library: `DecisionRow`, `BgDecisionData`, `PositionData`, `DecisionData`, `DescriptiveData`, `PlayCandidate`, `CubeOwner`. Also the `Move` / `Play` value types — these moved here from `BgMoveGen` so non-move-gen consumers can use them without dragging in the generator.
* **BgMoveGen** — `MoveNotationFormatter` for rendering candidate plays as standard backgammon notation. The producer-side bridge between XG's raw `sbyte[]` move encoding and the shared `Play` primitive lives here in `XgMoveTranslator`.

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
  XgIteratorState.cs
  XgMatchInfo.cs
  XgMoveTranslator.cs
  Json/
    XgJsonOptions.cs
  Models/
    Models.cs
  Parsing/
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
  XgpIterateTests.cs
```

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
  `GameHeaderRecord` entries only.

### XgDecisionIterator

Two iteration surfaces over the same underlying record stream:

* `Iterate` yields flat `DecisionRow` records (one per play or cube decision,
  CSV-shaped).
* `IterateDiagramRequests` yields `BgDecisionData` records. Cube decisions
  yield exactly **one** `BgDecisionData`, not two — the doubler and the taker
  share a single record.

`IterateXgDirectory` is the directory-level entry point: it enumerates
both `*.xg` (match files) and `*.xgp` (position files) — both formats
are XG-native and `XgFileReader` handles them uniformly, so callers
that point at a directory of mixed XG content get all decisions
regardless of extension. `IterateJsonDirectory` is the parallel
entry point for `*.json` exports. Two explicit `Directory.EnumerateFiles`
calls rather than a `*.xg*` glob: the broader pattern would also match
hypothetical `.xgz` / `.xgr` files we don't assume are XG-format.

Both surfaces report the "best play" as the **highest-equity** candidate in
`analysis.Evals[]`, not XG-native rank 0. `BgDecisionData.Plays[0]`,
`DecisionRow.Equity`, and `PlayOutcomeData.AfterBestBoard` all key off this
convention. XG's stored ranking is not always strict equity-descending, so
rank 0 and best-by-equity disagree on a subset of decisions. Use
`FindBestByEquityIndex(analysis)` to locate the best candidate when adding
code that reads from `analysis.Evals`, `analysis.Moves`,
`analysis.PositionsPlayed`, or `analysis.EvalLevels` (all four are
rank-coupled with the same index).

Supporting helpers:

* `ExtractMatchInfo` — public helper that scans for the `MatchHeaderRecord`
  and returns an `XgMatchInfo`, without iterating decisions.
* `ToBoard` — converts a position to the 26-element board array from the
  on-roll player's perspective (see "Board format" below).
* `FlipPosition` — flips the position to bottom-player perspective for XGID
  encoding.
* `BuildMoveDiagramRequest` — returns `null` if `dice == 0`.
* `CubeValueActual` — internal static helper, called from `MatchContext`.

### XgIteratorState

Carries cross-row state and caller-controllable early-exit flags:

* `AdvanceNextGame` / `AdvanceNextMatch` — caller-set flags. When set, the
  iterator skips to the next game / match boundary on its next step.
* `MatchInfo` / `GameInfo` — populated by the iterator before the first row
  of each match / game.
* Flags reset at file boundaries.

### XgMoveTranslator

Internal static helper that converts the 8-element `sbyte[]` move
encoding XG stores in `BestMoveAnalysis.Moves[i]` into a
`BgDataTypes_Lib.Play`. Hits are pre-encoded into
`BgDataTypes_Lib.Move.ToPt`'s sign so
`BgMoveGen.MoveNotationFormatter.Format(Play)` can render notation
without seeing a board. The translator also performs the
on-roll-board mutation (sending hit blots to the bar), which the
local `MoveNotationFormatter` used to do inline. The translator's
output is consumed once per candidate by `BuildMoveDiagramRequest`
and feeds both `PlayCandidate.MoveNotation` (rendered) and
`PlayCandidate.Play` (structural) — single producer call, single
scratch-board mutation. Sentinel handling matches
`Parsing/AfterBoardBuilder` (`from == -1` terminator,
`from == 24` bar entry, `to < 0` bear off including XG overshoot
encodings); the `(0, 0)` "dance" sentinel is **not** recognized —
preserving the prior local formatter's "1/1" garbage rendering of
no-legal-move decisions, deferred to a follow-up.

### MatchContext

Internal class tracking match and game state during iteration.

* `MatchScoreFor(int activePlayer)` — perspective-correct match score for
  a given active player. Used by the taker side of a cube decision so its
  row reflects the taker's perspective, not the doubler's.

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

For a cube decision, the iterator produces a doubler row and a taker row.
Both rows carry the **doubler's** board (no flip for the taker row). The
taker row's match score comes from `MatchContext.MatchScoreFor(cube.ActivePlayer)`
so the score reflects the taker's perspective.

### `.xgp` file handling

`.xgp` files (positions-only) encode "no analysis" differently from `.xg`
match files:

* `MoveError` and `ErrorCube` use sentinel value `-1000` to mean "unanalysed".
* `IsAnalysed` is gated on the analysis-level field, not on error presence.
* Error fields are treated as present when `> -999.0` (anything above the
  sentinel).
* `UserPlayError`, `UserDoubleError`, `UserTakeError` are populated from the
  raw XG fields with sentinel guards.
* `PlayCandidate` win / gammon / backgammon probabilities are populated from
  `EvalResult`.

### TestData

* Shared at `backgammon\TestData`. `TestPaths._root` resolves it via
  five `..` segments from `AppContext.BaseDirectory`.
* All file-touching tests use `[Collection("FileIO")]`.

## Public API

```csharp
public static class XgFileReader
{
    public static IEnumerable<XgRecord> ReadFile(string path);
    public static XgMatchInfo?          ReadMatchInfo(string path);
    public static IEnumerable<GameHeaderRecord> ReadGameHeaders(string path);
}

public static class XgDecisionIterator
{
    public static IEnumerable<DecisionRow> Iterate(
        XgFile file, string? sourceFile, XgIteratorState? state = null);

    public static IEnumerable<BgDecisionData> IterateDiagramRequests(
        XgFile file, string? sourceFile, XgIteratorState? state = null);

    public static IEnumerable<DecisionRow> IterateXgDirectory(
        string xgDir, XgIteratorState? state = null);

    public static IEnumerable<DecisionRow> IterateJsonDirectory(
        string jsonDir, XgIteratorState? state = null);

    public static XgMatchInfo ExtractMatchInfo(XgFile file);
}

public sealed class XgIteratorState
{
    public bool AdvanceNextGame  { get; set; }
    public bool AdvanceNextMatch { get; set; }
    public XgMatchInfo? MatchInfo { get; internal set; }
    public XgGameInfo?  GameInfo  { get; internal set; }
}

public sealed class XgMatchInfo { /* match-level metadata */ }
public sealed class XgGameInfo  { /* game-level metadata  */ }

public static class BackgammonConstants
{
    public static bool IsStandardOpeningPosition(ReadOnlySpan<sbyte> position);
}
```

Produces types defined in `BgDataTypes_Lib`; see that subproject's
`INSTRUCTIONS.md` for their shapes and serialization contract.

## Pitfalls

* **Two perspectives, don't confuse them.** The board array is always
  on-roll-relative. The XGID is always bottom-player-relative. `FlipPosition`
  converts between them and is applied only at the XGID encoding boundary.
* **Taker cube row uses doubler's board.** Do not add a flip for the taker
  side — it was deliberately removed. The taker row is distinguished only
  by its match score (via `MatchContext.MatchScoreFor(cube.ActivePlayer)`),
  not by a board flip.
* **`IterateDiagramRequests` yields one row per cube decision.** Consumers
  expecting symmetric doubler/taker pairs will be off by a factor of two.
  Flat `Iterate` still emits two `DecisionRow`s per cube decision.
* **`.xgp` sentinel handling is easy to regress.** `-1000` means "unanalysed"
  for `MoveError` / `ErrorCube`; anything `> -999.0` is a real error. Using
  `!= 0` or `.HasValue` checks on raw fields will silently treat unanalysed
  positions as zero-error.
* **Cube `IsAnalysed` must gate on `Analysis.Level`, not `LevelRequest`.** XG
  writes `LevelRequest` when the user *asks* for an analysis and `Level` when
  it *runs*. An `.xgp` saved before the requested rollout completes has
  `Level == -100` (queued) but a non-zero `LevelRequest`. Using `||` between
  the two re-admits these phantom cubes and yields rows with empty equity /
  eval fields.
* **`BuildMoveDiagramRequest` returns `null` when `dice == 0`.** Callers must
  null-check rather than assuming every decision yields a diagram request.
* **`XgIteratorState.AdvanceNextGame` / `AdvanceNextMatch` reset at file
  boundaries.** Do not rely on them persisting across files in batch runs.
* **`TestPaths._root` depends on a specific build output depth.** If
  `AppContext.BaseDirectory` moves relative to the repo root (e.g. a csproj
  layout change), the five-`..` walk breaks and every file-touching test
  fails. Fix by adjusting `TestPaths`, not by moving `TestData`.
* **`XgMoveTranslator` does not handle XG's `(0, 0)` dance sentinel.**
  No-legal-move decisions render as the garbage notation "1/1" today,
  matching the prior local formatter. The corpus integration test
  `IterateDiagramRequests_AllBestCandidates_HaveNonEmptyNotation` relies
  on this by asserting non-empty notation for every analysed move. Adding
  a dance break in the translator without also relaxing or replacing that
  test will fail the suite. The right fix is to render a recognizable
  dance sentinel (e.g. "(no play)") in `XgMoveTranslator` *and* update
  the test contract — they ship together, not separately.

## Subproject-internal next steps

None. Cross-cutting items (downstream bug reports, feature proposals spanning
multiple subprojects) belong in the umbrella `INSTRUCTIONS.md`
"Next up" / "Pending" sections, not here.
