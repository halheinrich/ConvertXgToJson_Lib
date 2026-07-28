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
  OpeningBook.cs
  OpeningBookEntry.cs
  OpeningBookKey.cs
  XgContainerLayout.cs
  XgDecisionIterator.cs
  XgFileReader.cs
  XgFileWriter.cs
  XgGameInfo.cs
  XgidEncoder.cs
  XgIteratorCallbacks.cs
  XgIteratorOptions.cs
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
    OpeningBookParser.cs
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

Container facts shared by both sides (decoded from the fixture corpus,
pinned by `XgFileWriterTests`, byte layout encoded once in
`XgContainerLayout` — the SSOT `XgContainerWriter` emits through and
`XgDecompressor` assigns streams through):

* Physical stream order is `temp.xg`, `temp.xgr` (only when rollouts exist),
  `temp.xgi`, `temp.xgc` (only when comments exist), then a manifest stream,
  then a 36-byte uncompressed end-record.
* The manifest is one 532-byte entry per inner file, in stream order, the
  manifest itself unlisted: Pascal ANSI filename padded to 512 bytes, then
  uncompressed size, compressed size, offset relative to content start,
  CRC32 (IEEE) of the uncompressed bytes, and constant `0x200`.
* The end-record is nine little-endian int32s XG seeks to from EOF to find
  the manifest (so its absence makes the file unloadable even though every
  other structure validates): CRC32 (IEEE) of the entire compressed body
  (all data streams **and** the manifest stream), count of data streams
  (manifest excluded), constant `1`, compressed size of the manifest stream,
  manifest offset from content start (= sum of the data streams' compressed
  sizes), constant `1`, then three zero int32s. Decoded byte-level against
  XG-authored `.xgp`/`.xg` (2-, 3-, and 4-stream files all validate) and
  pinned by `XgFileWriterTests` (writer trailer + `XgCorpus_EndRecord_*`
  corpus agreement).
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
match header and game header copied with their comment indices cleared
but otherwise **verbatim** (source perspective, player order, and
metadata preserved — including a foreign Location like
`"BackgammonGalaxy"`), decision records copied **with analysis panes
intact**, referenced rollout contexts carried over with indices remapped
(a cube rollout is an adjacent context *pair* with the record pointing at
the second leg — both legs travel), and the decision records' comments
carried into a fresh dense comment table (indices remapped, RTF payloads
verbatim — XG's own SaveAs of a commented move does the same:
`CommentExported.xgp` ground truth). Match- and game-level comments are
**not** carried — XG mirrors the match header comment into the outer
header's plain-text `Comments` field (`CommentsAddedToXgp.xgp` ground
truth), so carrying them would drag RTF→plain-text extraction in; the
header/footer comment indices are cleared so they cannot dangle into the
rebuilt table. A play slice carries the real same-turn cube record when
the source has one, else synthesizes the incidental unanalysed pane.

Slice entry points also accept the decision's `XgDecisionId` directly
(destructured internally; `Filename` is not consulted — the caller
already resolved the source from it; the parameter is
`XgDecisionId`-typed so routing an `XgpDecisionId` there is a compile
error, and the Xgp-vs-Xg routing stays with the caller). An optional
`XgpSliceOptions` overrides the player names on two axes: the
slot-based pair (`Player1Name`/`Player2Name`) renames by header slot;
the role-based pair (`OnRollName`/`OpponentName`) renames by decision
role, resolved by the exporter from the exported records' `ActivePlayer`
sign (`>= 0` is player 1 — the `MatchContext.PlayerName` convention; a
cube record's `ActivePlayer` is the doubler, so a take decision anchors
to the doubler with no special-casing). Roles are determinable iff the
exported records hold at least one move/cube record and all share one
sign — always true for a slice (one located decision); when
determinable, role names outrank the same slot's slot name; each
resolved override rewrites that player's Unicode field *and* its ANSI
twin (writer truncation 128 chars / 40 bytes; non-Latin1 characters
degrade to `?` in the twin via the Latin1 replacement fallback), while
every other header field — Location provenance included — still passes
through verbatim. `XgpSliceOptions.Anonymized` carries both pairs
("On-roll" / "Opponent" where a single decision defines roles,
"Player 1" / "Player 2" where roles are undefined) and is the SSOT for
what anonymized export means; overrides validate non-empty at init, so
an invalid options instance is unrepresentable.

**Anonymize-copy** (`Write(XgFile, XgpSliceOptions, …)`) — the third
surface: a whole-file re-emit for callers that already hold the finished
file shape (typically a parsed single-position `.xgp` being passed along
anonymized). Every record, rollout context, and comment travels verbatim
— no record selection, no comment or rollout remapping; the only rewrite
is the match header's name fields through the same `CopyMatchHeader`
copy the slice path uses (with its comment-index clearing off). With no
overrides it is a plain `XgFileWriter` re-emit, byte-for-byte. Role
names apply when the whole record stream determines roles — true for
every single-decision `.xgp` source; a multi-decision copy (a whole
`.xg` match) has per-move roles only, so slot names apply and role
names are deliberately unused — unless the options carry no slot
fallback at all, which throws `NotSupportedException` rather than
silently guessing a slot (precedent: the centred-cube-above-1 throw).
The `Anonymized` preset carries both pairs, so it never throws.

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

`IterateXgDirectory` (internal) is the directory-level walk: it
enumerates both `*.xg` (match files) and `*.xgp` (position files) —
both formats are XG-native and `XgFileReader` handles them uniformly,
so a directory of mixed XG content yields all decisions regardless of
extension. File discovery is delegated to
`XgFileReader.EnumerateXgFormatFiles` (see XgFileReader above) — the
single source of the `.xg`-then-`.xgp` rule. `IterateJsonDirectory`
(internal) is the parallel walk for `*.json` exports. Both are
internal: external consumers compose their own directory walks from
the public `EnumerateXgFormatFiles` + `Iterate` /
`IterateDiagramRequests` pieces (XgFilter_Lib's
`FilteredDecisionIterator` is the in-tree pattern — it adds
skip-and-log error handling and filter callbacks this producer
deliberately doesn't own).

Both surfaces report the "best play" as the **highest-equity** candidate in
`analysis.Evals[]`, not XG-native rank 0. `BgDecisionData.Plays[0]`,
`DecisionRow.Equity`, and `PlayOutcomeData.AfterBestBoard` all key off this
convention. XG's stored ranking is not always strict equity-descending, so
rank 0 and best-by-equity disagree on a subset of decisions. Use
`FindBestByEquityIndex(analysis)` to locate the best candidate when adding
code that reads from `analysis.Evals`, `analysis.Moves`,
`analysis.PositionsPlayed`, or `analysis.EvalLevels`. All four arrays
are rank-coupled with the same index.

**Depth resolution.** `ResolveDepthInfo` is the single source of a
candidate's analysis depth, projecting one XG level (plus optional rollout
context, plus an optional resolved `OpeningBookEntry`) into five parallel
forms: the human `Label`, a compact `Abbreviation`, an ordinal `Rank`
(higher = deeper), and the `AnalysisMode` × `AnalysisLevel` pair — the
machine-usable two-axis taxonomy behind depth filtering (BgDataTypes_Lib
owns the enums; this producer stamps them). The `LevelInfo` switch is the
taxonomy: N-ply → `Evaluation` + `Ply1`–`Ply7` (rank 1–7; XG level `12`
"3-ply red" collapses to `Ply3`), `1000/1001/1002` → `Evaluation` +
`XgRoller`/`XgRollerPlus`/`XgRollerPlusPlus` (rank 20–22), `999/998`
(Book V1/V2 — note the order: 999 is the *older* V1 book, 998 the V2 one)
→ `BookRollout` + `Unknown` (rank 99), the no-context `100` sentinel →
`Rollout` + `Unknown` (rank 100), and any unrecognised level → `Unknown` +
`Unknown` (rank 0). The mode distinguishes a book hit from an unrecognised
level where rank 0 could not — that separation is deliberate. The rollout
branch (valid `rolloutIndex`) computes `innerPly = plyLevel + 1` and stamps
`Rollout` + `Ply1`–`Ply7` (rank 100 + innerPly); an inner ply outside 1–7
degrades the *level* to `Unknown` while rank/abbreviation still reflect
the raw value (defensive — real rollouts always carry an in-range inner
ply). Trial count lives only in `Label`/`Abbreviation`, never the pair —
it is not a taxonomy axis. Rank and pair are projections of the *same*
resolution; the corpus invariant
`IterateDiagramRequests_DepthPairAndRank_AgreeTierWiseForEveryCandidate`
pins that they never land in different tiers.

The book branch (see "Book enrichment" below) fires when the caller
resolved a V2-book-stamped candidate to a *rollout* entry: label
`"Book V2: {trials} trials. {moves-level label}"`, abbreviation
`"B{moves-level token}p{trials}"` (e.g. `B4p12960` — the token is the ply
digit, or the Roller abbreviation for a Roller-family level), pair
`BookRollout` + the entry's `RolloutMovesLevel` mapped through the same
`LevelInfo` switch (the book's stored levels use the same PLAYERLEVEL code
space — one decoding site). **Rank stays 99 under enrichment** — the arc
holds `DepthRank` semantics stable (see next steps: revisit if the
diagram's out-of-order cue misleads).

The pair is stamped at all four emission sites from the same resolution
that produces the label: `BuildMoveRow` → `DecisionRow.AnalysisMode` /
`AnalysisLevel` (best-by-equity candidate), `BuildCubeRows` → the same
row members (cube analysis), `BuildMoveDiagramRequest` → per-candidate
`PlayCandidate.AnalysisMode`/`AnalysisLevel`, `BuildCubeDiagramRequests`
→ `DecisionData.CubeAnalysisMode`/`CubeAnalysisLevel`.
`BgDecisionData.AnalysisMode`/`AnalysisLevel` (the `IDecisionFilterData`
members) derive via `BestPlayIndex`, which the sorted `Plays` list makes
index 0 — the best-by-equity candidate, converging with the CSV surface's
best-by-equity resolution. That convergence is pinned twice: within the
diagram surface
(`IterateDiagramRequests_InterfaceDepthPair_MatchesBestByEquityCandidate`)
and across surfaces paired by `DecisionId`
(`DepthPair_CsvAndDiagramSurfaces_AgreePairedById`).

**Book enrichment.** `Iterate` / `IterateDiagramRequests` accept an
optional `XgIteratorOptions` whose `OpeningBook` member carries a loaded
book database (locating the `.ob` on disk is app configuration — this lib
takes the instance). For each V2-book-stamped (998) checker-play
candidate, `LookupBookEntry` builds the session-1-proven key —
`PositionsPlayed[i]` + decision context through the `OpeningBookKey`
factories — and hands the selected entry to `ResolveDepthInfo`; every
candidate resolves its own entry (a decision's candidates enrich to
*different* rollouts). Enrichment is strictly additive: it changes labels
and levels, never which decisions or candidates are emitted. Degradation
to the bare `"Book V1"`/`"Book V2"` label with `BookRollout` + `Unknown`
happens on: no book supplied, a V1 stamp (999 — the V2 database wasn't
its source), a lookup miss, a context outside the proven keying (cube not
centred at 1, away score < 1), a rollout-backed entry being absent — and
notably on a **Roller++-evaluation-backed hit**: fixture (a) proves XG
stamps 998 even when the book's best entry for that position is its
Roller++ baseline (Level 1002, zero trials), so there is no cached
rollout to recover and the fall-through is correct, not a bug. Cube rows
never look up: no book-stamped cube decision exists anywhere in the
fixture corpus (438 files / 23,736 cube records scanned — zero 998/999 in
cube `Level` or `LevelRequest`), so the cube-row keying convention
remains unproven and cube book stamps degrade by design.

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

### Opening book (OpeningBook / OpeningBookParser)

Parser + position-keyed lookup for XG's opening-book database
(`OpeningBookV2.ob`, installed with XG 2) — the rollout data behind the
bare 998/999 book level codes that `.xg` files stamp on book-analysed
candidates. Format decoded empirically; the oracle is XG's own tooltip
rendering of book entries (two pinned fixtures: the ajhhBG0407 game 9
Kazaross rollout and a Steven Carey 9-away/9-away rollout).

**File format.** Flat 256-byte blocks, no compression; file size is an
exact block multiple. Block 0 is the header (`"OBDB"` magic at +4, format
version, TDateTime, ShortString version text at +32, byte-length-prefixed
UTF-16 title at +41). Every later block leads with an int32 kind:
1 = long-description continuation (80 UTF-16 chars at +4, assembled text
NUL-terminated), 2 = entry, unknown kinds skipped. Blocks are Delphi
memory dumps — bytes past a block's live fields are stale heap garbage,
so the parser reads only documented extents. Entry layout (offsets
in-block): contributor WideChar[32] at +4; the keyed position sbyte[26]
at +68; context ints at +96 (cube value, cube owner, away pair — −1/−1 =
money — Jacoby, Beaver, Crawford); seven eval singles at +124 in
`EvalResult` slot order; entry level at +152 (100 = rollout,
1002 = Roller++ evaluation with zeroed rollout params); engine version
pair at +160/+164 (tooltip "XG 2.00"); trials at +168; per-game equity σ
at +172 (tooltip "±" = 1.96·σ/√trials); rollout moves/cube levels at
+176/+180 (full PLAYERLEVEL codes — cube can be a Roller code); dice
seed at +188; duration seconds at +196; two TDateTimes at +200/+208
(added-to-book, analysis date — the tooltip shows the latter). +184 and
+192 hold small unidentified values on ~2% of rollout entries and are
parsed over, not surfaced.

**Keying.** An entry describes the position *resulting* from a candidate
play, stored from the perspective of the player on roll after it (the
mover's opponent); the away pair is (new-on-roll away, mover away) in
that same frame; the eval vector is from the *mover's* perspective. For
a book hit XG copies the entry's seven floats into the `.xg` analysis
pane verbatim (bit-identical), so the pane's `PositionsPlayed[i]` +
score context is exactly the lookup key: `OpeningBookKey.ForMatchPlay` /
`ForMoneyPlay` own the normalization (flip player-1-relative record
positions when player 1 moved; reorder decision-frame away scores).
Jacoby keys money entries, Crawford keys match entries, Beaver is entry
data only.

**Selection.** One key can hold many entries (independent community
rollouts + XG's own Roller++ baseline). `TryGetEntry` returns the entry
XG displays, per the empirically pinned policy: entry-level rank first
(rollout > Roller++, via the `ResolveDepthInfo` rank taxonomy — the SSOT
for level ordering), then rollout moves-level rank, cube-level rank,
trials, analysis date, file position (import-append: later wins). XG
demonstrably prefers a deeper-level rollout over one with more games.
`GetEntries` returns all matches best-first.

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

### XgIteratorOptions

Optional producer configuration record supplied at call time to
`Iterate` / `IterateDiagramRequests` (and the internal directory walks) —
the third leg of the iterator's parameter pattern: `XgIteratorState`
observes, `XgIteratorCallbacks` controls iteration, `XgIteratorOptions`
configures how rows are built. One member today:

* `OpeningBook` — a loaded book database for depth enrichment (see "Book
  enrichment" above). Null = no enrichment; book hits degrade gracefully.

Members are caller-loaded resources, not per-decision knobs; null (or a
null member) always means "default behaviour".

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

`IterateDiagramRequests` also stamps the **played** cube action onto
`DecisionData.UserDoublerAction` / `UserTakerAction`, mapped from the raw
`CubeRecord.Doubled` / `Taken` pane state. `Doubled` is a pane-state field,
not a flag — only `1` (doubled) and `0` (no double) record a played action:

| `Doubled` | `Taken`    | Doubler    | Taker  |
|-----------|------------|------------|--------|
| `1`       | `1` / `2`  | `Double`   | `Take` |
| `1`       | `0`        | `Double`   | `Pass` |
| `1`       | `-1`       | `Double`   | *null* |
| `0`       | (any)      | `NoDouble` | *null* |
| `-1`/`-2` | (any)      | *null*     | *null* |

`-2` is the incidental cube pane beside a checker play (never analysed, so
it never reaches a row); `-1` is the pane XG writes where a game ended with
no cube action taken — every analysed `-1` record in the corpus is the last
record of its game, followed by a footer whose `Termination` is ≥ 100
(by resignation), and it is also what `XgpExporter` writes for a curated
`.xgp` cube problem. Both map to *null* — "played action not recorded" —
rather than being flattened into `NoDouble`.

The mapping is keyed off the pane state alone, **not** off `ErrorCube`'s
`-999` sentinel: the played action is a game fact, the error an analysis
fact. They coincide in the corpus only because a record with no action has
nothing to score. `Taken == 2` (beaver) maps to `Take` — the taker half
models the accept-or-decline axis and a beaver accepts; no beaver appears
in the corpus, so that arm is reasoned rather than fixture-pinned.

Cross-half consistency (a recorded taker response implies the doubler
doubled) is a producer contract `DecisionData` documents but does not
guard; it is enforced here by gating the taker half on the doubler half,
and pinned corpus-wide in `XgDecisionIteratorCubeActionTests`.

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

    // JSON serialization round-trip. ReadJson is load-bearing: the
    // internal XgDecisionIterator.IterateJsonDirectory and XgFilter_Lib's
    // FilteredDecisionIterator both parse each export through it.
    public static string                ToJson(XgFile file, JsonSerializerOptions? options = null);
    public static Task                  WriteJsonAsync(XgFile file, string outputPath,
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
}

public static class XgpExporter
{
    // Clean-position path (caller holds only the decision record):
    // unanalyzed position, XG-import-only — the iterator yields zero
    // decisions for these exports, by design.
    public static void   Write(BgDecisionData decision, Stream output);
    public static byte[] ToBytes(BgDecisionData decision);

    // Slice path (caller holds the parsed source file + the decision's
    // XgDecisionId coordinates): analysis carried through — the iterator
    // yields exactly one decision for a sliced analyzed decision.
    public static void   Write(XgFile source, int game, int moveNumber, bool isCube, Stream output);
    public static byte[] ToBytes(XgFile source, int game, int moveNumber, bool isCube);

    // Slice path with options (player-name overrides; everything else
    // still verbatim).
    public static void   Write(XgFile source, int game, int moveNumber, bool isCube, XgpSliceOptions options, Stream output);
    public static byte[] ToBytes(XgFile source, int game, int moveNumber, bool isCube, XgpSliceOptions options);

    // Slice path addressed by the iterator-stamped XgDecisionId
    // (compile-time contract — XgpDecisionId has no coordinates and does
    // not fit; id.Filename is not consulted). The only slice surface with
    // a path transport, matching its external callers.
    public static byte[] ToBytes(XgFile source, XgDecisionId id);
    public static byte[] ToBytes(XgFile source, XgDecisionId id, XgpSliceOptions options);
    public static void   WriteFile(XgFile source, XgDecisionId id, string path);
    public static void   WriteFile(XgFile source, XgDecisionId id, XgpSliceOptions options, string path);

    // Anonymize-copy: whole-file re-emit with name overrides — every
    // record, rollout context, and comment verbatim (no slicing, no
    // comment or rollout remap); only the match header's name fields
    // rewritten. No overrides = plain re-emit.
    public static void   Write(XgFile source, XgpSliceOptions options, Stream output);
    public static byte[] ToBytes(XgFile source, XgpSliceOptions options);
    public static void   WriteFile(XgFile source, XgpSliceOptions options, string path);
}

public sealed record XgpSliceOptions
{
    // null = no override; non-empty enforced at init (an invalid
    // instance is unrepresentable). Each resolved override rewrites that
    // player's Unicode name field and its ANSI twin.

    // Slot-based pair: renames by header slot.
    public string? Player1Name { get; init; }
    public string? Player2Name { get; init; }

    // Role-based pair: renames by decision role — the exporter resolves
    // the decision-maker's slot from ActivePlayer sign. Outranks the
    // slot names when roles are determinable; ignored (slot fallback)
    // on a multi-decision copy; role-only options against a
    // roles-undeterminable source throw NotSupportedException.
    public string? OnRollName   { get; init; }
    public string? OpponentName { get; init; }

    // SSOT for anonymized export, both pairs: "On-roll" / "Opponent"
    // where a single decision defines roles (every slice, every
    // single-decision .xgp copy), "Player 1" / "Player 2" where roles
    // are undefined (whole-.xg copy). Never throws.
    public static XgpSliceOptions Anonymized { get; }
}

public static class XgDecisionIterator
{
    public static IEnumerable<DecisionRow> Iterate(
        XgFile file, string? sourceFile,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null,
        XgIteratorOptions? options = null,
        ILogger? logger = null);

    public static IEnumerable<BgDecisionData> IterateDiagramRequests(
        XgFile file, string? sourceFile,
        XgIteratorState? state = null,
        XgIteratorCallbacks? callbacks = null,
        XgIteratorOptions? options = null,
        ILogger? logger = null);

    public static XgMatchInfo? ExtractMatchInfo(XgFile file);
}

public sealed record XgIteratorOptions(
    OpeningBook? OpeningBook = null);

public sealed class OpeningBook
{
    public static OpeningBook Load(string path);
    public static bool        TryLoad(string path, out OpeningBook? book);

    public int      EntryCount    { get; }
    public string   Title         { get; }
    public string   Description   { get; }
    public string   VersionText   { get; }   // "3.70" in the shipped DB
    public int      FormatVersion { get; }
    public DateTime CreatedOn     { get; }

    // Best entry per the documented selection policy (the one XG displays).
    public bool TryGetEntry(in OpeningBookKey key, out OpeningBookEntry? entry);
    // All entries for the key, best first; empty when none.
    public IReadOnlyList<OpeningBookEntry> GetEntries(in OpeningBookKey key);
}

public readonly struct OpeningBookKey : IEquatable<OpeningBookKey>
{
    // Factories own the two normalization conventions (perspective flip,
    // away-score orientation); positionPlayed is the candidate's resulting
    // position in the XG record convention (player-1-relative), e.g. an
    // element of BestMoveAnalysis.PositionsPlayed. Cube-centred-at-1
    // contexts only (see Pitfalls).
    public static OpeningBookKey ForMatchPlay(
        PositionEngine positionPlayed, int activePlayer,
        int moverAway, int opponentAway, bool isCrawford);
    public static OpeningBookKey ForMoneyPlay(
        PositionEngine positionPlayed, int activePlayer, bool jacoby);
}

public sealed class OpeningBookEntry
{
    public string         Contributor     { get; init; }
    public PositionEngine Position        { get; init; }  // new-on-roll perspective
    public int  CubeValue     { get; init; }
    public int  CubeOwnerSign { get; init; }              // raw; perspective unverified
    public bool IsMoneySession { get; }                   // OnRollAway < 0
    public int  OnRollAway    { get; init; }              // −1 = money
    public int  OpponentAway  { get; init; }              // the mover's away; −1 = money
    public bool Jacoby        { get; init; }
    public bool Beaver        { get; init; }
    public bool Crawford      { get; init; }
    public EvalResult Evaluation { get; init; }           // mover perspective; equity slot is cubeful
    public int    Level  { get; init; }                   // 100 rollout / 1002 Roller++
    public bool   IsRollout { get; }                      // Level == 100; gates rollout-parameter reads
    public int    Trials { get; init; }                   // 0 for evaluation entries
    public float  EquityStandardDeviation { get; init; }
    public double? ConfidenceInterval95 { get; }          // 1.96σ/√Trials; null for evals
    public int    RolloutMovesLevel { get; init; }        // PLAYERLEVEL codes
    public int    RolloutCubeLevel  { get; init; }
    public int    Seed { get; init; }
    public int    EngineVersionMajor { get; init; }
    public int    EngineVersionMinor { get; init; }
    public TimeSpan Duration  { get; init; }
    public DateTime AddedOn   { get; init; }
    public DateTime AnalyzedOn { get; init; }             // the tooltip date
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
* **`CopyMatchHeader` is a full manual copy of `MatchHeaderRecord`.** The
  exporter's single header-copy helper (name overrides + optional
  comment-index clearing) copies every header field explicitly (the
  model is a class, not a record — no `with`). A new
  `MatchHeaderRecord` field must be added to the copy too, or slice and
  anonymize-copy exports silently drop it. The guard is the byte-identity
  pair in `XgpSliceExportTests`
  (`SliceOptions_MatchingSourceNames_AreByteInvisible` and
  `Copy_WithSourceNames_IsByteIdenticalToXgFileWriterOutput`): overrides
  equal to the source names must produce a byte-identical file, so any
  dropped or transposed field fails them.
* **Role-based names resolve to slots *before* `CopyMatchHeader`.**
  `ResolveNameOverrides(options, records)` turns the role pair
  (`OnRollName`/`OpponentName`) into the slot pair the header copy takes;
  `CopyMatchHeader` must stay a dumb slot mechanism — do not teach it
  roles. Roles are determinable ⟺ the exported records hold at least one
  move/cube record and **all** share one `ActivePlayer` sign (`>= 0` is
  player 1; a cube record's `ActivePlayer` is the doubler) — true by
  construction for every slice and every single-decision `.xgp` copy
  source. A whole-`.xg` copy deliberately **ignores** role names when a
  slot fallback exists (user spec — roles are per-move there, undefined
  file-wide — not an omission); role names with **no** slot fallback
  against a roles-undeterminable source throw `NotSupportedException`
  rather than silently guess a slot.
* **An interior empty comment line is a real (empty) comment.**
  `temp.xgc` is CRLF-terminated lines joined by `CommentIndex`;
  `CommentWriter` writes an empty comment as a bare CRLF, so
  `CommentParser` may drop only the one empty segment after the final
  CRLF (a split artifact). Skipping interior empties shifts every later
  entry and silently desyncs all subsequent comment joins — it once
  shipped that way.
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
* **The RichGameHeader is packed; the container manifest + end-record are
  load-bearing for XG but silently optional for our reader.**
  `ThumbnailOffset` (an Int64 at offset 12) must be written raw — an aligned
  8-byte write would insert padding and corrupt the header. `XgFileReader`
  assigns streams by manifest name (located via the end-record, exactly as
  real XG loads a file) but degrades to record-size heuristics whenever the
  trailer or manifest fails validation — so a reader-level round-trip still
  passes with a corrupt manifest or a *missing trailer*. That gap once
  shipped: the writer omitted the end-record, round-trip tests stayed green,
  and real XG rejected every file (it seeks from EOF through the trailer to
  locate the manifest). So `XgFileWriterTests` asserts manifest sizes /
  offsets / CRC32s **and** the end-record's fields against the raw written
  bytes, plus `XgCorpus_EndRecord_*` pins the trailer decoding against
  XG-authored files — keep these when refactoring the container writer. The
  one smoke the suite cannot run: open a freshly written `.xgp` in real XG.
* **Stream assignment is manifest-first; the heuristic fallback cannot tell
  the manifest from a comment stream.** `XgDecompressor` names the four
  sub-streams from the manifest and falls back to record-size heuristics
  only for the old single-stream format and unvalidatable containers. Under
  the fallback, a commentless multi-stream container parses with one phantom
  garbage comment — the manifest (532-byte entries, matching no record size)
  lands in the xgc slot. That bug shipped silently for every commentless
  XG-authored file, masked because round-trips re-emitted the phantom as a
  real `temp.xgc`; files written during that window still parse with the
  garbage entry, which is correct for what their bytes say. The fallback's
  limitation is accepted (robust over minimal) and pinned by
  `Decompress_CorruptTrailer_FallsBackToRecordSizeHeuristics`; the corpus
  guard is `XgCorpus_NoParsedCommentIsManifestShaped`.
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
* **Two opening books, and the level codes read "backwards": 999 = Book V1,
  998 = Book V2.** XG 1's `OpeningBook.db` (V1) stamps level 999; XG 2's
  `OpeningBookV2.ob` (V2) stamps 998 — the *lower* code is the *newer*
  book. `LevelInfo` once had the labels reversed; the spec's PLAYERLEVEL
  table and the fixture corpus (ajhh openings are 998 = V2 hits) are the
  ground truth. Only the V2 database is parsed (`OpeningBook`); V1 is
  deliberately unsupported.
* **The opening-book key is doubly perspective-normalized — let
  `OpeningBookKey` do it.** A book entry keys on the position *resulting*
  from the candidate play, flipped to the player on roll after it, with
  the away pair stored as (new-on-roll away, mover away) in that flipped
  frame — while `.xg` record positions are player-1-relative and the
  decision's context is mover-framed. Hand-building the key invites both a
  missed flip (player-1 movers flip, player-2 movers don't) and a swapped
  away pair; the `ForMatchPlay` / `ForMoneyPlay` factories encapsulate
  exactly these two traps. The eval vector, by contrast, is from the
  *mover's* perspective (XG copies it into the `.xg` pane verbatim on a
  book hit — pinned bitwise by `RealDb_FixtureA_…`).
* **A book entry's equity slot is cubeful and score-contexted; the
  tooltip's cubeless number is derived.** The same resulting position
  stores wildly different equities under different away scores (+0.377 at
  (2,4)-away vs −0.38 at (4,2)-away vs +0.01 at (9,9)-away) — the equity
  slot is the normalized cubeful candidate equity XG displays, not a
  money constant. XG's tooltip "cubeless" is
  (win − lose) + (winG − loseG) + (winBG − loseBG) over the probability
  slots. Don't compare equities across score contexts, and don't read the
  slot as cubeless (the `EvalResult.Equity` doc is written for cube
  panes).
* **Book enrichment changes labels and levels, never emission — and a 998
  stamp is not always rollout-backed.** The optional
  `XgIteratorOptions.OpeningBook` threading is strictly additive: with and
  without a book, the same decisions and candidates are emitted (pinned by
  the fixture (a) with/without pair). Each candidate resolves its *own*
  entry — fixture (a)'s decision enriches different candidates to
  different rollouts (12,960-game 4-ply vs 15,552-game 3-ply). And two of
  its five 998-stamped candidates resolve to the book's **Roller++
  evaluation baseline** entries (Level 1002, zero trials): XG stamps 998
  whenever the book supplied the pane numbers, rollout or not. Those hits
  deliberately stay at the bare "Book V2" label with
  `BookRollout` + `Unknown` — there is no cached rollout to recover. Do
  not "fix" that degradation, and never read `RolloutMovesLevel` /
  `Trials` off an entry without gating on `IsRollout` (evaluation entries
  store zeros there — a zero moves level would decode as a bogus
  "1-ply").
* **Cube rows never book-enrich — the keying is unproven, so they degrade
  rather than guess.** Session 1 proved the checker-play keying only; the
  turned-cube owner-sign convention is unknown and the key factories
  cover centred-cube contexts only. The full fixture corpus was scanned
  for an oracle (438 files, 23,736 cube records): **zero** cube analyses
  carry a book code (998/999) in `Level` or `LevelRequest`, against 3,817
  book-stamped checker-play candidates. With nothing to pin a cube-row
  key against, a book-stamped cube resolves to `BookRollout` + `Unknown`
  by design (`BuildCubeRows` / `BuildCubeDiagramRequests` pass no entry).
  If a book-stamped cube decision ever surfaces, pin the key against it
  before wiring cube enrichment — `ResolveDepthInfo` would also need to
  select `RolloutCubeLevel` rather than `RolloutMovesLevel` for that
  path.
* **Book selection: deeper rollout levels beat more games.** One key
  commonly holds several entries; XG demonstrably shows a 12,960-game
  4-ply/4-ply rollout over a 20,736-game 3-ply/3-ply one, and any rollout
  over its own Roller++ baseline — recency and file order are only final
  tiebreaks. One residual ambiguity, documented on `OpeningBook`: moves
  level is compared before cube level (lexicographic), a choice the
  shipped DB offers no discriminating case for. Also unverified: the cube
  *owner sign* convention (22 turned-cube entries, no tooltip oracle), so
  the public key factories cover centred-cube contexts only; and the
  entry fields at +184/+192 (small values on ~2% of rollout entries) are
  parsed over, not surfaced.
* **`temp.xgc` may carry unreferenced leftovers — orphaned comment-table
  entries are format reality, not a parse failure.** XG saves by bundling
  its working-directory temp files wholesale, so a stale comment table
  from an earlier commented session rides into unrelated saves.
  `match35041658.xg` and `MoneyTest.xg` each parse with three real RTF
  comment-table entries (two URLs + XG rollout-settings text, same
  Japanese-locale RTF — *identical* across the two different matches)
  referenced by nothing: every parsed `CommentIndex` / header-footer
  comment index in both files is `-1`. **Never assert comment-table
  emptiness for a "commentless" match file, and don't "fix" orphans** —
  whole-file copy preserves them (they're in the source bytes), slice
  drops them (only *referenced* comments are carried); both are correct.
  The footer-record hypothesis (an undecoded comment-index field on
  `GameFooterRecord` / `MatchFooterRecord`) was **refuted** by a
  byte-level sweep: footer records contain no −1-defaulted dwords at any
  common offset, so there is no hidden comment-index field there. The only
  offsets the sweep lit up were the already-parsed `RolloutIndices` (the
  rolled-out move referencing contexts 0–3) and cube `Taken` fields —
  never a comment reference.

## Subproject-internal next steps

* **`DepthRank` stays 99 for enriched book hits — revisit if the diagram
  cue misleads.** Enrichment recovers a book hit's real rollout depth
  (e.g. 4-ply / 12,960 games) but the rank deliberately does not move:
  this arc holds `DepthRank` semantics stable, and
  BackgammonDiagram_Lib's out-of-order-depth italic keys on rank. If a
  book hit rendering as "shallower" than an explicit rollout (99 < 100+)
  proves misleading next to its enriched 4-ply label, promoting enriched
  ranks is a deliberate future change — its own arc, coordinated with the
  diagram consumer.
* **Unify `EnumerateXgFormatFiles` ordering** — the single-arg overload
  keeps its historical extension-major, filesystem-order contract while
  the `SearchOption` overload sorts by full path (ordinal-insensitive,
  deterministic). Once ExtractFromXgToCsv consolidates its four private
  discovery copies onto the sorted overload, consider routing the
  single-arg form through `(directory, TopDirectoryOnly)` so the class
  carries one order contract. Deliberate behavior change, not a drive-by:
  it alters `IterateXgDirectory`'s file order — its own session.
* **Probe the corpus for cube depth labels resolved from the wrong field.**
  Cube depth labels resolve from `DoubleActionAnalysis.LevelRequest` (what the
  user *asked* XG to run) while the emission gate checks `Level` (what
  actually *ran*) — `BuildCubeRows` / `BuildCubeDiagramRequests` vs
  `IsAnalysed(CubeRecord)`. The known divergence (phantom cube: requested
  `1002`, ran `-100`) is gated out, but a cube where both are positive and
  different would pass the gate with equities from the ran level and a label
  naming the requested one. Unverified whether XG ever writes that state — and
  the int `Level`'s cube-side encoding may not match the `LevelInfo` short
  taxonomy, which may be *why* the code uses `LevelRequest`. Deferred work:
  probe the corpus for gated-in cubes with `Level ≠ LevelRequest`; if they
  exist, label from what ran. Surfaced (and deliberately not folded in) during
  the `.xgp` play-over-cube arc, which removed depth from the emission policy
  and made this harmless to that arc.
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
