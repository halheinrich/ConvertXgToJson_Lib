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

* **BgDataTypes_Lib** — the records this library produces and everything
  they derive: the two decision kinds (`CheckerPlayDecision`,
  `CubeDecision`) and their categories, the session kinds built through
  `Session.Create` from the header types' `SessionTerms` and
  `GameStanding`, `DecisionRow` (a record's projection for a
  `PlayRanking`), `BoardPosition` / `BoardState` (the one "same position",
  the one flip, the one play rule) and `Move` / `Play`, spelt by
  `Play.ToNotation()`. The test project also references
  `BgDataTypes_Lib.TestSupport`, whose `TestRecords` builds the records a
  test needs by hand.

## Layout

Two projects under `ConvertXgToJson_Lib.slnx`, governed by repo-root
`Directory.Build.props` (TFM, `TreatWarningsAsErrors`, XML doc generation)
and `Directory.Packages.props` (Central Package Management — no inline
`Version=` anywhere).

**`ConvertXgToJson_Lib/`** — the library. Seven areas:

- **Reading** — `XgFileReader` (discovery, full parse, the fast match-info
  and game-header paths, the JSON document) over `Parsing/`:
  `XgDecompressor` (the zlib container), `PascalBinaryReader` (Delphi
  record alignment), `RichGameHeaderParser`, `SaveRecordParser` (the six
  record variants, and `UnknownRecord` for a code it does not know),
  `RolloutContextParser`, `CommentParser`, `XgMoveEncoding` (the
  candidate-move byte encoding and its non-play sentinels), and
  `OpeningBookParser`.
- **The record model** — `Models/Models.cs`: `SaveRecord` and its
  variants, `RolloutContext`, `RichGameHeader`, the analysis and eval
  carriers and their enums, all internal (see "Record model is internal");
  `XgFile` is the public document root. `XgContainerLayout` is the one
  spelling of the container's directory layout, read by the decompressor
  and written by the container writer.
- **Writing** — `XgFileWriter` (the reader's mirror: a semantic
  round-trip, not byte identity) and `XgpExporter` (single-decision
  `.xgp` export, sliced per `XgpSliceOptions`) over `Writing/`:
  `XgContainerWriter`, `PascalBinaryWriter`, `RichGameHeaderWriter`,
  `SaveRecordWriter`, `RolloutContextWriter`, `CommentWriter`. A value the
  format cannot represent is refused as `XgUnrepresentableValueException`,
  its constraint typed as `XgUnrepresentableValueReason`.
- **Synthesis** — `XgFileBuilder` / `XgGameBuilder`, through which a
  consumer says what a match *is* without seeing the record structure;
  `XgRecordFactory`, the record construction the builders and the exporter
  share; and the intent-level value types they take, `XgPlayer`,
  `XgCubeEquities`, `XgPlayCandidate`.
- **Decision iteration** — `XgDecisionIterator`: the two surfaces
  (`BgDecisionData` records, and `DecisionRow` rows built from them for a
  ranking) over one walk, and the emission rules. With it `XgDepthFacts`
  (XG's level codes, rollout contexts and book stamps decoded into the
  typed depth facts a record stores), `MatchContext` (the header metadata,
  the cube and the counters as the walk advances; a decision's session,
  names and comment), `RtfPlainText` (the one reduction of XG's RTF
  comment to the text a reader sees), `XgIteratorState` /
  `XgIteratorCallbacks` / `XgIteratorOptions`, the public header types
  `XgMatchInfo` / `XgGameInfo` (the terms and the standing, read at the
  parse boundary), and `XgMoveTranslator` (XG move bytes to the shared
  `Play`).
- **Opening book** — `OpeningBook` (load, keyed lookup, the selection
  policy), `OpeningBookEntry`, `OpeningBookKey`.
- **JSON** — `Json/`: `XgJsonOptions` (the shipped options and the two
  document converters, `PositionEngineConverter` and
  `SaveRecordConverter`) and `XgJsonContext` (the source-generated
  metadata).

**`ConvertXgToJson_Lib.Tests/`** — xUnit, one class per surface or
behaviour area (parsers, writers, builders, the iterator's facets, the
opening book, JSON). `Golden/` holds the embedded pre-change `ToJson`
captures that `JsonContractTests` pins as the document's byte contract;
`Helpers/` the shared builders and mirrors (`BinaryBuilder`, the
`ResolverPaths` options trio, `EmissionMirror`, `CapturingLogger`). Gating
tests synthesize their files through the builders; corpus and fixture tests
read the umbrella's `TestData/` through `TestPaths` — see "TestData" below.
`XgCorpusAgreementTests` measures the converter against XG's own numbers
over the local corpus, and `TooGoodPassFixtureTests` holds permanent
fixtures reaching the fourth cube answer, read as Too good (see "Measured
against XG" under XgDecisionIterator).

## Architecture

### Record model is internal

The XG on-disk record model (`Models/Models.cs`: `SaveRecord` and its six
variants, `RolloutContext`, `RichGameHeader`, the analysis/eval carriers,
their enums; plus `OpeningBookEntry` / `OpeningBookKey`) is `internal` by
design (halheinrich/backgammon#131 — the deep-module cut). `XgFile` is the
public opaque handle: internal constructor, internal `[JsonInclude]`
collections, no public members. Consumers say what a match *is* through
`XgFileBuilder` and consume decisions through the iterator; nothing outside
this assembly (plus its test project via `InternalsVisibleTo`) sees a
record. `PublicSurfaceTests` pins this; `JsonContractTests` pins that the
JSON document (XgToJson's output contract) survived the change
byte-for-byte, against goldens embedded in the test project.

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
  first yield. A stream whose first record is not the match header is
  refused as `InvalidDataException`: a game's standing is read under the
  match's terms, so there is no game header to yield without one. To stop early, the consumer breaks out of the foreach;
  disposing the enumerator stops further yields. There is no imperative
  skip flag.
* `ToJson` / `WriteJsonAsync` / `ReadJson` — the JSON document, through
  `XgJsonOptions.Default`. Each record in `records` is written with a
  `$type` discriminator first (`"HeaderMatch"`, `"Cube"`, …, the
  `RecordType` member name) and read back to its class by
  `SaveRecordConverter`, whose one table maps every `RecordType` member to
  the class that carries it — `Comment` and `Missing` both to
  `UnknownRecord`, which is why the mapping is a converter rather than
  `[JsonPolymorphic]` (one discriminator per derived type there; measured
  under halheinrich/backgammon#177). Every member round-trips, and both
  directions check the table: a write refuses a record whose runtime type
  is not its tag's class or whose tag is unnamed, a read refuses an unknown
  discriminator or one that disagrees with the record's `entryType`, all
  as `JsonException`. The converter claims the abstract `SaveRecord` alone,
  so the concrete record inside it resolves to the same options' object
  contract — no derived options object exists (halheinrich/backgammon#178).

### The header types (XgMatchInfo / XgGameInfo)

The public metadata a match header and a game header state, projected at
the parse boundary (`XgMatchInfo.From`, `XgGameInfo.From`, the one
projection each) and handed to the state, the callbacks and the fast
paths:

* `XgMatchInfo` — `Player1` and `Player2` as XG stores them, and `Terms`,
  a `SessionTerms`: `MoneyTerms` (the Jacoby and beaver rules and the cube
  limit) when the header's length is XG's money sentinel 99999 — Galaxy's
  money games included, see Pitfalls — else `MatchTerms` (the length). The
  sentinel is never surfaced as a length.
* `XgGameInfo` — `IsStandardStart` (the game's initial position is
  `BoardPosition.Standard`) and `Standing`, a `GameStanding`:
  `MoneyStanding` (the scores) under money terms, `MatchStanding` (each
  player's away score, and whether the game is the Crawford game) under
  match terms.

A decision's `Session` is the two turned to the player on roll, through
`Session.Create(terms, standing, onRoll)` — `MatchContext.SessionFor`, the
one construction. Both types are serialized through this library's JSON
options, under BgDataTypes_Lib's absence rule: every member is required on
the wire, a document missing one or stating null for one is a
`JsonException`, and each kinded member is read through BgDataTypes_Lib's
one dispatch (its `"Kind"`, under the options' naming policy). The members
have internal setters, so code outside this library never assembles one.

**The Max Cube field.** XG's match header states a maximum cube value as
an exponent (`MatchHeaderRecord.CubeLimit`). It is read with the header
and stays on the parsed record, which the writers copy through, so a round
trip and a slice reproduce it. For money terms it is the limit:
`MoneyTerms.CubeLimit` is the power of two the exponent spells, and an
exponent that spells none (outside 0–30) is refused as
`InvalidDataException`. For a match it is read and states nothing on the
terms: `MatchTerms` carries no cube limit, no match is refused for its
value, and the value is neither derived from nor checked against the
match's length (Hal's ruling on halheinrich/backgammon#273, 2026-09-27).
Whether a match's Max Cube is a rule of its own is open
(halheinrich/backgammon#289). XG typically writes 10 (a limit of 1024),
but its files hold other values too: every match header of the 2026-09-27
`xg`/`xgp` corpus states 10, and the fixture files hold matches stating 3
and 4. `MetadataContractTests` pins that a match header reads whatever its
Max Cube, with no limit on its terms.

### Writing (XgFileWriter / XgpExporter)

The reader's mirror. Layered exactly like the read path:

* `Writing/` internals mirror `Parsing/` file-for-file:
  `PascalBinaryWriter` (alignment-mirroring primitive writes; padding is
  explicit zeros), `SaveRecordWriter` (all six TSaveRec variants → complete
  zero-padded 2560-byte records), `RolloutContextWriter` (2184-byte records),
  `CommentWriter` (CRLF lines, `#1#2` escape; it rejects a comment the
  table cannot carry faithfully with `XgUnrepresentableValueException`, and
  its doc comment is the one statement of what that is —
  halheinrich/backgammon#234), `RichGameHeaderWriter`
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
XG re-analyzes on import. The on-roll player is normalized to player 1,
so the record's session maps onto the header verbatim: its terms are
written by `XgRecordFactory.MatchHeader` — money terms as XG's
money-sentinel length with their Jacoby and beaver rules and their cube
limit as the Max Cube exponent, a match's as its length with XG's default
exponent (the terms state no limit to write) — and its standing as the game
header's scores, a money session's scores included (the clean path once
parsed the rules back out of the stored XGID and wrote a money session's
scores as 0). A record is well-formed by construction, so the one check
left is the encoding's own: a centred cube above 1 is refused (see
Pitfalls). Output is byte-deterministic — no timestamps or random ids.
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
sign (`>= 0` is player 1 — the `MatchContext.SeatOf` convention; a
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

### Synthesis (XgFileBuilder / XgGameBuilder)

The one public way to make an in-memory `XgFile` — intent-level inputs, the
record stream synthesized behind the handle. `XgFileBuilder.ForMatch` /
`ForMoneySession` name the players and the match form; `AddGame` takes the
entering score, optional Crawford flag, and optional initial position (all
positions in the public surface are 26-cell boards in XG's player-1 frame);
the returned `XgGameBuilder` records decisions in play order:

* `Play(player, dice, played)` — analysed play whose analysis is the played
  move as its only 1-ply candidate (XG's analysed forced play); the
  `candidates` overload takes explicit `XgPlayCandidate`s (Play + equity +
  ply). `UnanalysedPlay` advances the position but never emits.
  `Dance` / `IllegalPlay` record XG's two non-play sentinels as intents —
  the raw encodings stay the builder's business.
* `CubeDecision(doubler, XgCubeEquities, ply, doublerAction, takerAction,
  requestedPly)` — analysed cube; XG's stored errors are *derived* from the
  equities and the played actions, and the played actions map onto the
  `Doubled`/`Taken` pane state (the inverse of the iterator's
  UserDoublerAction mapping). `ply` is what *ran* (the provenance of the
  equities and the depth facts the record states); the optional `requestedPly`
  synthesizes XG's ordinary requested ≠ ran governor shape (see "Level
  semantics" above), stamped on the pane pair and the record-level
  play-time stamp alike; null means the request matches `ply`.
  `UnanalysedCube` is the incidental pane; its actions still move the cube.
* `Play` (both overloads) and `CubeDecision` take an optional `comment` —
  the decision's comment, surfaced as the emitted decision's
  `DescriptiveData.Comment` (halheinrich/backgammon#31). The builder owns
  the file-level comment table: `XgFileBuilder` appends the text and the
  record stamps the index, so a caller never sees one; `Build()` snapshots
  the table as it does the records. The text is stored verbatim (XG's own
  comments are RTF; the builder neither wraps nor converts — see "Comment
  text" below for where the conversion does happen); null or empty
  means none and adds no entry. The skipped shapes (`UnanalysedPlay`,
  `UnanalysedCube`, `Dance`, `IllegalPlay`) take no comment — the iterator
  never emits them, and the corpus is no guide either way (590 files, one
  referenced comment, on an analysed play; measured 2026-09-15).
* State is tracked per game: plays advance the position (validated against
  the board through `BgDataTypes_Lib.BoardState` — from-point occupied, no
  blocked destination, hit flag agreeing with a blot; dice legality is
  deliberately not checked), a taken double doubles the cube to the taker,
  a pass ends the game (further decisions throw). `AtPosition` resets the
  tracked position for mid-game problem positions.
* Validation is eager and loud (`ArgumentException` at the offending call);
  an unrepresentable match — Crawford at a non-Crawford score, a 16-checker
  board, a reply without a double — never reaches `Build()`.
* Output is deterministic (no timestamps or random ids; two `Build()` calls
  write identical bytes) and self-identifies via the Location fingerprint
  shared with `XgpExporter`.

`XgRecordFactory` (internal) is the SSOT both synthesis paths draw from —
XG's editor-save header defaults, the never-analysed panes, the incidental
cube pane, the signed-log2 cube encoding, the producer fingerprint.
`XgpExporter`'s clean-position path and the builder must keep sharing it.

Two level-code facts live here: XG's PLAYERLEVEL code for an N-ply
evaluation is **N − 1** (`ToLevelCode`; `XgDepthFacts.OfLevel` is the
decode direction), and because the iterator gates analysed cubes on `Level > 0`, a
1-ply cube pane (level 0) is downstream-indistinguishable from an
unanalysed one — `CubeDecision` therefore refuses `ply: 1` (min 2) rather
than synthesize a decision that silently never emits.

### XgDecisionIterator

Two iteration surfaces over one walk of the record stream:

* `IterateDiagramRequests` yields the `BgDecisionData` records — one
  `CheckerPlayDecision` or `CubeDecision` per analysed decision. A cube
  decision is one record, from the doubler's side (see "Cube decisions"
  below).
* `Iterate` yields each as a flat `DecisionRow`, built from its record by
  `DecisionRow.From(record, ranking)` for the options' ranking (see "The
  ranking" below). A row and its record therefore cannot disagree: the row
  and record constructions this iterator once kept side by side, and the
  agreement pin that held them together, are gone.

**A record states what XG stores** (halheinrich/backgammon#273: no stored
copy of a derivable value; the boundary is BgDataTypes_Lib's "Stored or
derived" table). It states the board (on-roll frame), the cube and its
owner, the session (the match header's terms and the game's standing,
turned to the player on roll by `Session.Create`), the roll as rolled, the
candidates in XG's order — each its play, its typed depth facts, its
equity and probabilities — the played candidate, the played cube actions,
and XG's error only where the record states no move to derive it from
(`UnlistedPlayError` for a play XG did not list, the `Unstated*ActionError`
of a cube half whose action is not recorded), with the names, comment,
flag and standard start. Everything those determine BgDataTypes_Lib
derives, so nothing here produces it: the XGID, the pip counts, the
after-boards, the notation, the depth label, abbreviation and rank, the
best play and each play's error under a ranking, the loss probabilities,
the error of a stated cube action. The copies this library kept of those
are retired — the second copy of the play rule (`AfterBoardBuilder`), the
XGID encoder, the label/abbreviation/rank projection (`LevelInfo`'s text
and ranks, `InnerLevelToken`, `DepthAbbreviationFormat`),
`BackgammonConstants` (the standard layout and the flip, which
`BoardPosition` owns, and the money stand-in's away-score rule), and the
BgMoveGen reference behind the old notation formatter.

**Candidates keep XG's order.** `CheckerPlayDecisionData.Plays[0]` is XG's
own rank 0; the equity sort `3f1920d` introduced is gone. Which play is
best, the order a consumer shows, each play's error and whether it is
scored are a ranking's (SPEC-scoring §2a: "Where both keys are equal, the
analyser's order stands"), derived by `RankedBy(ranking)`. The played
candidate is found by resulting position: the candidate whose
`PositionsPlayed` XG stores equal to its `FinalPosition`, compared as
`BoardPosition`s; none matches when the move was unlisted or not made.

**What is not a record.** The emission rules sit at the one dispatch both
surfaces share (`IterateAnalysedDecisions`), so the two can never disagree
about which source decisions become records:

* **Unanalysed panes** — a move record without a move count and an
  evaluation (`IsAnalysed(MoveRecord)`), a cube pane whose `Level` is not
  above 0 (`IsAnalysed(CubeRecord)`; see "Level semantics").
* **A Crawford game's cube pane** (halheinrich/backgammon#201). Doubling is
  prohibited in the Crawford game, so the cube pane XG writes there — and
  does analyse; the corpus carries such panes, so this is a format fact the
  converter drops, not an anomaly, and no log line marks it — describes no
  decision (`AdmitsCubeDecision(ctx)`). The rule's owner is the record type
  (a `CubeDecision` refuses a Crawford position); the predicate keeps the
  walk from reaching that guard. A cube's id reads `ctx.MoveNumber + 1`
  without incrementing the counter, so the plays after a dropped pane
  number as before, and the stop callbacks see only emitted decisions.
* **A position that is not a decision** — a side has borne off all its
  checkers (Hal's ruling of 2026-09-27, halheinrich/backgammon#273).
  Legitimate XG/XGP data, simply not a decision in the record model:
  passed by silently, as ordinary filtering — no warning, no log line —
  for both kinds. The rule is `PositionData.IsDecisionPosition`'s, asked,
  never restated: for a cube as the last clause of the cube emission gate
  (`IsCubeDecision`, of the stored position, since the answer does not
  depend on the frame), for a play first in `ReadCheckerPlay`.
* **XG's two non-play sentinels** — the illegal-play marker, skipped with
  a warning naming the file, game, move and roll, and the dance, skipped
  silently (see Pitfalls).
* **A move record that states no roll** (`StatesRoll`).
* **A corrupt candidate** — a candidate invalid from its own position,
  which the record would refuse (Hal's ruling of 2026-09-25 on invalid
  plays, applied to stored candidates). `ReadCheckerPlay` tests every
  candidate with `BoardState.TryApplyPlay` — the non-throwing door to the
  same play rule the record's own check runs — on a **fresh board per
  candidate**, since the method applies a valid play and turns the board,
  so a shared one would test later candidates from the wrong position. The
  decision is skipped the way the illegal-play marker is, with a warning:
  `Corrupt candidate in {SourceFile}, game {Game}, move {MoveNumber},
  roll {Roll}: invalid from the decision's position: {InvalidCandidates}`,
  the last listing each invalid candidate by its 1-based number and its
  notation (`candidate 2 (13/12)`). The rest of the file still emits.

Nothing is caught to find any of these, and nothing else is caught or
skipped: any other refusal from BgDataTypes_Lib fails loud. For an `.xgp`
every rule acts upstream of the single-decision policy, so a play skipped
for any reason never suppresses the file's analysed cube.

**The ranking.** `XgIteratorOptions.Ranking` (a `PlayRanking`,
`PlayRanking.Equity` by default — SPEC-scoring §2a: "Apps without the
setting use the default") is the ranking the walk judges decisions under.
`Iterate` builds every row for it, and the post-yield callbacks of both
surfaces see each decision through a view built for it — the row itself on
the row surface, `record.ViewFor(ranking)` on the record surface, where the
record itself does not depend on the ranking. It sits on the options
because the options are the iterator's configuration of how rows are
built; an undefined value is refused when the options are made, by
construction and by `with` alike. `XgDecisionIteratorRankingTests` pins
each half.

Every record — and so every row — is stamped with a
`BgDataTypes_Lib.DecisionId` in its `Id` field, built by the internal
`BuildDecisionId(sourceFile, game, moveNumber, isCube)` helper the two
record builders call. A record's and a row's game and move number are
derived from it (halheinrich/backgammon#124). Extension dispatch is
case-insensitive invariant:

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
  emission policy — an `.xgp` yields **at most one** decision: the
  checker play if the walk built one, else the analysed cube. The move pane
  exists only because dice were rolled, so dice in the file mean the saved
  decision is the play; the cube pane is XG's incidental. Depth is not
  compared. A curated cube problem is a pre-roll position and carries no
  move pane at all.

**A standalone position states no game, move or standard start**
(halheinrich/backgammon#124): a decision with an `XgpDecisionId` has no
game or move number, and its `DescriptiveData.IsStandardStart` is `null` —
read off the id at the one site both kinds share, so the record, which
holds the two to agreement, never sees them differ.

Cube decisions stamp `ctx.MoveNumber + 1`, so a cube decision numbers as
the move its turn goes on to play: the cube and the play that follows
share a move number, told apart by their kind.

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

**Depth: the typed facts.** A candidate's and a cube analysis's depth is
stored as typed facts — the `AnalysisMode` × `AnalysisLevel` pair, the
rollout trial count, the book edition, an unrecognized level's raw code —
and BgDataTypes_Lib derives the label, abbreviation and rank from them (its
internal `DepthTaxonomy`, to which the rank grid and the text grammar moved
unchanged). `XgDepthFacts` is what stays here: the one decoding of XG's
PLAYERLEVEL codes, rollout contexts and book stamps into the facts. Its
class doc is the statement of the table; in short:

| XG code | Facts |
|---|---|
| `0` | Evaluation, `Ply1` |
| `1`, `11` | Evaluation, `Ply2` |
| `12` | Evaluation, `Ply3Red` |
| `2` | Evaluation, `Ply3` |
| `1000` | Evaluation, `XgRoller` |
| `3` | Evaluation, `Ply4` |
| `1001` | Evaluation, `XgRollerPlus` |
| `4`, `5`, `6` | Evaluation, `Ply5`, `Ply6`, `Ply7` |
| `1002` | Evaluation, `XgRollerPlusPlus` |
| `998` / `999` | BookRollout, level `Unknown`, edition `V2` / `V1` |
| `100` | Rollout, level `Unknown` (no context) |
| *anything else* | mode and level `Unknown`, the raw code |

Codes `1` and `11` share one arm: code 11 was identified as plain 2-ply by
XG's own display (user-ruled 2026-08-28, halheinrich/backgammon#160). XG
level `12` is its own `Ply3Red`, and the evaluation order — XG's menu
order, the ply and XG Roller families interleaved — is `AnalysisLevel`'s
contractual declaration order (ruled 2026-08-28). Note the book order: 999
is the *older* V1 book, 998 the V2 one.

A **rollout** (a valid `rolloutIndex`) states `Rollout`, its inner
evaluation level and its trial count (`GamesRolled`; a count of 0 is none
recorded). The inner level is the **first** phase's (`Level1`) when a first
phase exists, otherwise the second phase's (`Level2`), which then plays
throughout — the user's ruling on halheinrich/backgammon#251: the first
phase is the strength the user set, a later cheaper phase is an economy, so
XG's "First 2 moves: 4-ply … Remaining moves: XG Roller" is a 4-ply
rollout. Whether a first phase exists is `LevelCut > 0` (the number of
moves it covers); no branch tests a *level* for "set" — level 0 is 1-ply —
and `LevelTrunc` (truncation) is not a depth input. An inner code that is
not an evaluation level states its raw code with the level `Unknown`.
Trial counts are a fact, never part of the pair.

A cube analysis's depth resolves from `Level` — the level that produced
its stored equities — never the `LevelRequest` setting
(halheinrich/backgammon#161; see "Level semantics").

**Book enrichment.** `Iterate` / `IterateDiagramRequests` accept an
optional `XgIteratorOptions` whose `OpeningBook` member carries a loaded
book database (locating the `.ob` on disk is app configuration — this lib
takes the instance). For each V2-book-stamped (998) checker-play
candidate, `LookupBookEntry` builds the session-1-proven key —
`PositionsPlayed[i]` + the decision's session through the `OpeningBookKey`
factories; it misses for player-2 movers (see Pitfalls) — and hands the selected entry to `XgDepthFacts.Resolve`; every
candidate resolves its own entry (a decision's candidates enrich to
*different* rollouts). A rollout entry enriches the hit: it states the
entry's rollout moves level and trial count beside its edition. The rank
stays the book's whatever the enrichment (BgDataTypes_Lib's grid ranks
every book hit 99, ruled 2026-08-28: the rank answers "what does the file
record?"). Enrichment is strictly additive: it changes facts, never which
decisions or candidates are emitted. The bare book facts
(`BookRollout`, level `Unknown`, the edition) stand on: no book supplied,
a V1 stamp (999 — the V2 database wasn't its source), a lookup miss, a
context outside the proven keying (cube not centred at 1), a rollout-backed
entry being absent — and notably on a **Roller++-evaluation-backed hit**:
fixture (a) proves XG stamps 998 even when the book's best entry for that
position is its Roller++ baseline (Level 1002, zero trials), so there is
no cached rollout to recover and the fall-through is correct, not a bug.
Cube decisions never look up: no book-stamped cube decision exists
anywhere in the fixture corpus (438 files / 23,736 cube records scanned —
zero 998/999 in cube `Level` or `LevelRequest`), so the cube keying
convention remains unproven and cube book stamps state the bare facts by
design.

**Measured against XG.** `XgCorpusAgreementTests` walks the umbrella's
local `TestData/xg/` and `TestData/xgp/` and holds the converter to XG's
own numbers; the corpus is gitignored, so it gates nothing and passes
vacuously on an empty corpus. It pins invariants, never a count:

* every analysed decision builds a record, is passed by as not a decision,
  or is skipped for a corrupt candidate, with the warning naming it —
  nothing else fails;
* every candidate XG stored, in every analysed decision position, has a
  derived after-board equal to XG's stored resulting position for it, and
  the player's is XG's position after the played move — XG stores both in
  the mover's frame, so each is compared turned (`BoardPosition.Flipped`),
  for either seat (see "Board format"). The candidate count is XG's, not
  the built records', so a decision skipped for a corrupt candidate is a
  disagreement and fails this invariant, naming the decision: a
  translation regression cannot leave the comparison unseen (the
  umbrella's ruling on halheinrich/backgammon#273);
* XG's recorded error for the player's move is the played candidate's
  error under depth first, save where the depth-first best is an
  opening-book candidate XG's recorded analysis evidently did not rank (the
  umbrella's measurement on halheinrich/backgammon#282);
* XG's cube errors are the scoring policy's errors of the stated actions,
  within `1e-4` (a few of XG's are rounded to the fourth decimal);
* XG's stored double/pass equity (the analysis pane's `EquityDoubleDrop`)
  is the record's pass equity, `CubeDecisionData.ActionEquity(Pass)`,
  exactly — each built cube record paired with the pane it was built from
  by its `DecisionId`, both in the doubler's perspective (the pane's
  equities are the doubler's, stated verbatim). The value is read from the
  production API, never restated; no production guard refuses a pane for
  its stored value;
* read as the `.xgp` it is, a position file emits the play its walk built,
  else its cube.

**The cube truth's evidence is kept in three kinds** (SPEC-scoring §3, "The
truth-claim derivation"; Hal's ruling of 2026-09-27 on
halheinrich/backgammon#273). The truth is one of the four cube answers,
`CubeDecisionData.BestAnswer`, and the fourth reads Too good or No double /
Pass by the decision (`CubeDecision.ClaimOf`); SPEC-scoring §3, as amended
on halheinrich/backgammon#326, is the model. BgDataTypes_Lib owns the rule
and the reading and verifies them with synthetic tests; this repo owns
every comparison against facts XG actually stores:

1. *Agreement with facts XG stores* — the invariants above, the truth's
   inputs among them (XG's cube errors, its double/pass equity).
2. *Measured counts* — the corpus test's report states how many built cube
   records have each answer as their truth (`BestAnswer`), and how many of
   those with the fourth read Too good and how many No double / Pass. A
   report, never asserted; the corpus stays free to change.
3. *Permanent real input reaching the fourth answer, read as Too good* —
   `TooGoodPassFixtureTests`, `RequiresFixtureFiles`: named permanent
   fixtures, a money session (`MoneyTest.xg` game 1 move 16, the only money
   case among the fixtures) and a match (`match35253054.xg` game 1 move 28),
   each of whose cube records the production path builds with `BestAnswer`
   the fourth answer, `NoDoublePass`, and with gammons possible, so that it
   reads Too good; the test pins all three, as measured. It fails when a
   named file is missing. It shows that our rule, applied to XG's stored
   numbers, reaches that answer on real input — not agreement with an XG
   Too Good label: XG's files carry no analysis text, and its two stored
   cube choice fields (`ComputerChoice`, `DoubleChoice3`) are undocumented
   and agree with the pane's stored equities only at 1-ply (the umbrella's
   measurement, 2026-09-27), so nothing here reads them.

Measured 2026-09-27 over the 571-file corpus of that day (378 `.xg`, 193
`.xgp`): 54,991 analysed decisions, all built (37,269 plays, 17,722 cubes),
none passed by, none skipped; candidate after-boards 282,183/282,183 and
player after-boards 37,240/37,240 equal to XG's; the player's error
37,217/37,240 under depth first (equity: 35,832), the 23 exceptions all
opening moves (moves 2–4) whose depth-first best is a book candidate; cube
errors 17,357/17,357 (doubler) and 851/851 (taker). Measured 2026-09-28
(UTC) over the same corpus: XG's double/pass equity is the record's pass
equity in 17,722/17,722, the one value XG stored being 1. Measured
2026-10-01 over the 573-file corpus of that day (193 `.xgp`), 18,170 built
cube records: their truths are No double 15,913, Double / Take 752,
Double / Pass 580 and the fourth answer 925, which reads Too good in 902
and No double / Pass in 23. Among the `.xg` fixtures the same day, 133 cube
records across eleven files have the fourth answer as their truth, 129
reading Too good and 4 No double / Pass. Dated evidence, not pins.

Supporting helpers:

* `ExtractMatchInfo` — public helper that scans for the first
  `MatchHeaderRecord` and returns an `XgMatchInfo`, or `null` if no
  match header is present. Callers must handle the no-header case
  explicitly; `Iterate` / `IterateDiagramRequests` translate `null` into
  a thrown `InvalidDataException` at the iteration boundary rather than
  silently emitting decisions against a default-constructed header.
* `OnRollBoard` — a record's stored position seen from the player on
  roll, as a `BoardPosition` (see "Board format").
* `CubeValueActual` — internal static helper, called from `MatchContext`
  and the cube builder.

### Level semantics: `LevelRequest` vs `Level`

The model behind every level field a cube record carries — written for
halheinrich/backgammon#161 (ruled 2026-08-31), the third defect cluster
on these fields. Corpus numbers were re-measured 2026-08-31 against the
local gitignored corpus as it stood that day: 360 `TestData/xg` match
files (39,103 cube records), 193 `TestData/xgp` positions (193 cube
panes), and the 30 XG-format `TestData/FixtureFiles` (3,644 cube panes).
The corpus churns by design, so these counts are dated evidence, not
pins; nothing gating depends on them.

**The two pane fields answer different questions.**
`DoubleActionAnalysis.LevelRequest` is a *setting*: the analysis level
the user asked XG to run. `DoubleActionAnalysis.Level` is *provenance*:
the level whose evaluation actually produced the pane's stored numbers —
the three cubeful `Equity*` scalars and the eval vectors. A record's depth
facts describe the stored equities, so they must resolve from `Level` —
the gate's question ("did anything run at all?") and the depth's question
("what ran?") are both provenance questions, and only `Level` answers
them. Consumers today:

* `Analysis.Level` → the `IsAnalysed(CubeRecord)` emission gate
  (`Level > 0`) **and** the cube's depth facts (`BuildCube` resolves
  `XgDepthFacts.Resolve(analysis.Level, …)`) — matching the checker side,
  where each candidate states the facts of what ran for it
  (`analysis.EvalLevels[i].Level`).
* `Analysis.LevelRequest` → **nothing**. Until the
  halheinrich/backgammon#161 fix the two cube label sites read it, so
  59% of analysed cube rows named a level that did not run; resolving
  from `Level` was the ruling, and `CubeLevelSemanticsTests` pins both
  divergence directions plus the gate. (`BestMoveAnalysis.Level`, the
  move pane's own header-level field, is likewise read by nothing.)

**The governor: XG deepens close decisions and cheapens lopsided ones,
recording the request either way.** The `(LevelRequest, Level)` census
over the 360-file `.xg` corpus:

| req | lvl | count | reading |
|----:|----:|------:|---------|
| 0 | 0 | 23,036 | never-written pane — see below |
| 4 | 1 | **9,348** | asked 5-ply, **ran 2-ply** (255 files) |
| 4 | 4 | 5,540 | asked 5-ply, ran 5-ply |
| 1002 | 1002 | 849 | XG Roller++ as asked |
| 3 | 1 | **114** | asked 4-ply, **ran 2-ply** (10 files) |
| 2 | 1002 | **80** | asked 3-ply, **ran XG Roller++** (20 files, e.g. `gobetzu_algo_18072026_42248634.xg`) |
| 3 | 3 | 73 | asked 4-ply, ran 4-ply |
| 1001 | 1001 | 34 | XG Roller+ as asked |
| 100 | 100 | 29 | rollout as asked |

9,542 of the 16,067 analysed panes (59%) diverge, in **both
directions**: the dominant shape is cheapening (a 5-ply request served
by a 2-ply evaluation), but `req=2 → lvl=1002` ran a level *more*
rigorous than requested, so a "requested ≥ ran" mental model is wrong.
The close-vs-lopsided reading has direct corpus support: among the
14,888 `req=4` panes, every one of the 401 played doubles sits in the
`lvl=4` group — the 9,348 cheapened panes contain not a single played
double. Decisions near the doubling window kept the full request;
lopsided no-double rolls were served at 2-ply.

**`Level == 0` is a never-written pane, not 1-ply.** All 23,036
`Level == 0` records in the `.xg` corpus are all-zero unwritten panes:
every one has `LevelRequest == 0`, zero in all three cubeful equities,
all-zero eval vectors in `EvalNoDouble` / `EvalDoubleTake`,
`IsBeaver == -100` (the never-analysed sentinel), and `-1/-1` in the
record-level pair — the `Doubled == -2` incidental pane XG writes
beside every checker play (`gobetzu-XG Roller++ 2026-07-21.xg` cube record 1
is the type specimen). The `Level > 0` gate excludes exactly these;
what it discards is structurally empty, not shallow analysis. (This
re-measures and upholds the halheinrich/backgammon#132 ruling booked
in Pitfalls.)

**The −100 sentinel is real but marginal — and it is not the gate's
justification.** The record model preserves XG's format claim that a
queued-but-unfinished analysis carries `Level == -100` with a non-zero
`LevelRequest`. Measured: **zero** `-100` panes in the 39,103-record
`.xg` corpus and zero in the 193-file `.xgp` corpus; FixtureFiles holds
five — four all-zero editor-save shapes (`NoAnalysis.xgp`,
`PlayAnalysis.xgp`, `3-ply Red.xgp`, `CommentsAddedToXgp.xgp`, all
`LevelRequest == 0`) and exactly one queued-with-request pane
(`Opening 32 65 64 31 65.xgp`: `req=1002, lvl=-100` on a
`Doubled == -2` incidental pane — notably carrying non-zero cubeful
equities). So the queued shape is an `.xgp`-editor-save phenomenon
observed once, never in a match file. The gate's justification is the
measured `Level == 0` story above; the `-100` exclusion rides the same
predicate as a fixture-backed extra.

**The record-level pair is a play-time stamp, not a depth source.**
`CubeRecord.AnalyzeLevel` / `AnalyzeLevelRequested` duplicate the pane
pair as XG recorded it at play time. Measured: they equal the pane pair
on all 15,075 ply-family analysed panes (divergence included —
`pane=(4,1)` carries `rec=(4,1)`), hold `-1/-1` on all 23,036
never-written panes, and disagree on precisely the 992 Roller/rollout
panes, where they hold a stale ply-family value
(`pane=(100,100) → rec=(4,4)`; `pane=(1001,1001) → rec=(3,3)`×28,
`(4,4)`×6; `pane=(1002,1002) → rec=(4,4)`×838, `(4,1)`×11;
`pane=(2,1002) → rec=(4,4)`×80) — consistent with a stamp a later,
deeper interactive re-analysis does not update. The pane is the
authority for what produced the stored equities. No depth consumer
reads the record pair: its only traffic is the parser↔writer
round-trip, the slice exporter's verbatim copy, and the builder's
synthesis stamps.

The `.xgp` side showed the same defect at smaller scale: 36 of the 193
corpus cube problems carry `req=4 → lvl=1` (labelled 5-ply before the
fix, equities from 2-ply); the 155 rollout panes resolve through the
rollout context (`RolloutIndex`), so their labels were already truthful.

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
rollouts + XG's own Roller++ baseline). `TryGetEntry` (internal, like the
whole keyed-lookup surface — the key needs the internal record position
convention, so the public intent is `XgIteratorOptions.OpeningBook`
enrichment, never direct lookup) returns the most rigorous entry. The
policy is this library's, not a prediction of XG's display: rollout
entries before evaluations, then the entry's level, its rollout moves
level and its rollout cube level, each compared by the `AnalysisLevel` its
code decodes to (`XgDepthFacts.OfLevel`; the enum's declaration order is
the contractual rigor order, and an unrecognized code orders below every
level), then trials, analysis date, file position (import-append: later
wins). XG's tooltip has been read on two keys where rollout depths compete
and it went one way each — deeper on `ajhhBG0407.xg` g9 m1, shallower on
`match26212229.xg` g3 m2 — so no parity claim is made; the class doc
records both cases and the halheinrich/backgammon#203 ruling that the
deeper entry stands. `GetEntries` returns all matches best-first.

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

The post-yield predicates see each decision under the options' ranking
(see "The ranking"): on `Iterate` the row itself, which is an
`IDecisionFilterData`; on `IterateDiagramRequests` the record's
`ViewFor(ranking)` — a record is not itself a filter view, since which play
is best, and each play's error, are a ranking's.

### XgIteratorOptions

Optional producer configuration record supplied at call time to
`Iterate` / `IterateDiagramRequests` (and the internal directory walks) —
the third leg of the iterator's parameter pattern: `XgIteratorState`
observes, `XgIteratorCallbacks` controls iteration, `XgIteratorOptions`
configures how rows are built. Two members:

* `OpeningBook` — a loaded book database for depth enrichment (see "Book
  enrichment" above). Null = no enrichment; book hits state their bare
  book facts.
* `Ranking` — the `PlayRanking` rows are built for and the post-yield
  callbacks judge under (see "The ranking" above). `PlayRanking.Equity` by
  default; an undefined value is refused when the options are made.

Null options mean the defaults. A positional construction names the book
first, so `new XgIteratorOptions(book)` still reads as before.

### XgMoveTranslator

Internal static helper that converts the 8-element `sbyte[]` move
encoding XG stores in `BestMoveAnalysis.Moves[i]` into a
`BgDataTypes_Lib.Play`: `Translate(moves, board)`, against the decision's
board in the mover's frame. XG's encoding carries no hit mark, so the
translator reads each hit off the board — a move landing on an opposing
blot of the starting position negates its `ToPt`, and that point stops
counting as a blot for the rest of the play. That is the whole of its
board reading: it does not apply the play, and the caller's board is only
read. XG's stored encodings are kept as they are, multi-die moves included
(halheinrich/backgammon#277).

Everything else about a play is the `Play`'s own: its notation
(`Play.ToNotation()` — this library renders none), its validity from a
position (BgDataTypes_Lib's play rule, which the iterator asks of every
candidate — see "What is not a record"), and its equality, whose one
statement is the doc on `Play` (halheinrich/backgammon#278). Point-index
decoding (`from == 24` bar entry, `to < 0` bear off including XG overshoot
encodings) lives in `Parsing/XgMoveEncoding`; the `from == -1` terminator
is loop control. The `(0, 0)` dance sentinel is **not** recognized at this
layer; sentinel-only analyses are skipped upstream — see Pitfalls.

### MatchContext

Internal class tracking match and game state during iteration: the
match's `XgMatchInfo`, the current game's `XgGameInfo` (with the
standing's Crawford flag as `IsCrawford`), the cube value and position,
and the game and move counters, advanced by `Update(record)`. What a
record states about the players is asked of it: `SeatOf(activePlayer)`
(XG's sign convention, the one place it is read), `SessionFor(onRoll)` —
the header's terms and the game's standing through `Session.Create`,
refused as `InvalidDataException` before any game header — `NameOf(seat)`
and `CommentAt(commentIndex)` (see "Comment text"), each of which states
no text (null) where XG records only empty or white-space text.

### Comment text

XG stores a decision's comment as an **RTF document**, and this library is
the one member that knows that: it stamps `DescriptiveData.Comment` with
the text a reader would see, never the document. BgQuiz shows the comment
verbatim and must never inspect its format (SPEC-quiz-view.md §4, the
2026-09-15 amendment), which is why the whole RTF source reached the screen
before this existed (halheinrich/backgammon#233).

The conversion happens in **one place**: `MatchContext.CommentAt`, which
the one `DescriptiveData.Comment` stamp in `XgDecisionIterator` — shared by
the play and the cube — goes through. An index out of the table (XG's `-1`
is "no comment") and a comment whose text is empty or white space both
state no comment: null. `RtfPlainText` owns the conversion itself
— a pure string-to-string mapping with no dependency on the walk — and
**its XML doc is the one statement of the contract**, not restated here.
The shape of it: a comment that does not open with `{\rtf` is plain text and
passes through byte for byte; otherwise textless destinations (the font
table, `\*` groups) contribute nothing, `\par` and `\line` give a CRLF —
the same break form `CommentParser` restores for a plain comment — `\tab` a
tab, every other control word nothing, `\'hh` decodes in the document's ANSI
code page (Latin-1 plus a Windows-1252 table for `0x80`–`0x9F`, the stated
limit), `\uN` emits its code unit and skips its fallback, trailing breaks and
whitespace are trimmed while interior ones are kept, and malformed RTF
degrades to the text extracted so far rather than throwing.

The comment **table** is untouched by all of this: `XgFile.Comments`,
`CommentParser`, `CommentWriter` and the `.xgp` slice export all still copy
XG's bytes verbatim, so a round trip reproduces them. Only the stamp path
converts.

### Board format

A record's board is a `BgDataTypes_Lib.BoardPosition` from the **on-roll
player's** perspective (`PositionData.Mop`). `BoardPosition` owns the
layout, the standard start (`BoardPosition.Standard`) and the flip
(`Flipped`); its doc is their one statement, so none is restated or kept
here. XG stores positions as 26-cell `sbyte` arrays (`PositionEngine`,
turned into a `BoardPosition` by `ToBoardPosition`), in two frames:

* `InitialPosition` — of a move record and a cube record alike — is in
  **player 1's frame**. `OnRollBoard` turns it to the player on roll: as
  stored for player 1, flipped for player 2.
* `PositionsPlayed` (each candidate's resulting position) and
  `FinalPosition` (the position after the played move) are in the
  **mover's frame**. A record's derived after-board is in the next mover's
  frame, so it is XG's stored position flipped, for either seat —
  measured on every candidate and every played move of the 2026-09-27
  corpus (see "Measured against XG").

The XGID, the pip counts and the after-boards are BgDataTypes_Lib's
derivations from the record; this library encodes none of them.

### Cube decisions

A cube decision is exactly **one** record — one `CubeDecision`, and so one
row — carrying the doubler's board (no flip); there is no second
taker-perspective record. Its stored equities (`EquityNoDouble`,
`EquityDoubleTake`, the cubeless eval equities) and probabilities are the
doubler's, verbatim. XG's errors of the stated actions (`cube.ErrorCube`,
`cube.ErrorTake`) are not stored: BgDataTypes_Lib's scoring policy derives
the error of a stated action from the equities, and `XgCorpusAgreementTests`
holds the two to XG's own. XG's error of a half is stored only where the
record states no action of that half to derive it from
(`UnstatedDoublerActionError`; `UnstatedTakerActionError` only once a
double was offered). Which cube panes are decisions at all is decided
upstream of the builder — an analysed pane, in a non-Crawford game, of a
decision position; see "What is not a record".

Each record also stamps the **played** cube action onto
`CubeDecisionData.UserDoublerAction` / `UserTakerAction`, mapped from the
raw `CubeRecord.Doubled` / `Taken` pane state. `Doubled` is a pane-state field,
not a flag — only `1` (doubled) and `0` (no double) record a played action:

| `Doubled` | `Taken`    | Doubler    | Taker  |
|-----------|------------|------------|--------|
| `1`       | `1` / `2`  | `Double`   | `Take` |
| `1`       | `0`        | `Double`   | `Pass` |
| `1`       | `-1`       | `Double`   | *null* |
| `0`       | (any)      | `NoDouble` | *null* |
| `-1`/`-2` | (any)      | *null*     | *null* |

`-2` is the incidental cube pane beside a checker play (never analysed, so
it never reaches a record); `-1` is the pane XG writes where a game ended with
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
* `UnlistedPlayError`, `UnstatedDoublerActionError` and
  `UnstatedTakerActionError` are read from the raw XG fields behind that
  guard, and only where the record states no move to derive the error
  from; the error of a stated move is derived (see "Cube decisions").
* `PlayCandidate` win / gammon / backgammon probabilities are populated from
  `EvalResult`.

### TestData

* Shared at `backgammon\TestData`. `TestPaths._root` resolves it by
  walking up from `AppContext.BaseDirectory` to the repo root.
* All file-touching tests use `[Collection("FileIO")]`.

## Public API

```csharp
// The opaque handle every surface exchanges. No public members: reading,
// writing, iterating, exporting, and synthesis all take or return the
// handle whole. (Namespace ConvertXgToJson_Lib.Models — the model types
// around it are internal; the namespace placement predates that and moves
// with the halheinrich/backgammon#19 rename, not before.)
public sealed class XgFile { }

// Intent-level synthesis — the one public way to make an in-memory XgFile.
// Positions are 26-cell boards in XG's player-1 frame; plays are
// BgDataTypes_Lib.Play in the mover's numbering. Eager, loud validation;
// deterministic output. See "Synthesis" above for semantics.
public sealed class XgFileBuilder
{
    public static XgFileBuilder ForMatch(int matchLength, string player1, string player2);
    public static XgFileBuilder ForMoneySession(string player1, string player2,
                                                bool jacoby = true, bool beaver = false);

    public int    MatchLength    { get; }   // 0 = money
    public bool   IsMoneySession { get; }
    public string Player1        { get; }
    public string Player2        { get; }
    public bool   IsJacoby       { get; }
    public bool   IsBeaver       { get; }

    public XgGameBuilder AddGame(int score1 = 0, int score2 = 0, bool isCrawford = false,
                                 IReadOnlyList<int>? initialPosition = null);
    public XgFile        Build();           // header-only file when no games
}

public sealed class XgGameBuilder           // from AddGame; methods chain
{
    public int  GameNumber { get; }
    public int  Score1 { get; }
    public int  Score2 { get; }
    public bool IsCrawford { get; }
    public int  DecisionCount { get; }

    public XgGameBuilder AtPosition(IReadOnlyList<int> position);

    // comment: the decision's comment, verbatim; null or empty = none
    public XgGameBuilder Play(XgPlayer player, DiceRoll dice, Play played,
                              string? comment = null);
    public XgGameBuilder Play(XgPlayer player, DiceRoll dice, Play played,
                              IReadOnlyList<XgPlayCandidate> candidates,
                              string? comment = null);
    public XgGameBuilder UnanalysedPlay(XgPlayer player, DiceRoll dice, Play played);
    public XgGameBuilder Dance(XgPlayer player, DiceRoll dice);
    public XgGameBuilder IllegalPlay(XgPlayer player, DiceRoll dice);

    public XgGameBuilder CubeDecision(XgPlayer doubler, XgCubeEquities equities,
                                      int ply = 2,   // 2–7; 1-ply cube is unrepresentable
                                      CubeAction? doublerAction = null,
                                      CubeAction? takerAction = null,
                                      int? requestedPly = null,  // 2–7; null = matches ply
                                      string? comment = null);
    public XgGameBuilder UnanalysedCube(XgPlayer doubler,
                                        CubeAction? doublerAction = null,
                                        CubeAction? takerAction = null);
}

public enum XgPlayer { Player1, Player2 }   // header slots, not roles

// One analysed candidate: the play, its equity (mover's perspective), and
// its depth (1–7 plies). Invalid depth unrepresentable.
public sealed record XgPlayCandidate(Play Play, double Equity, int Ply = 1);

// The three cubeful equities of an analysed cube decision, doubler's
// perspective; proper actions and played-action errors derive from these.
public readonly record struct XgCubeEquities(
    double NoDouble, double DoubleTake, double DoubleDrop);

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
    // FilteredDecisionIterator both parse each export through it. Every
    // record variant round-trips, UnknownRecord's two tags included: the
    // $type mapping is SaveRecordConverter's one table (see XgFileReader
    // under Architecture).
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

// A value valid in memory and in JSON that the XG format cannot represent
// — a limit of the wire, not a bad argument; the read side's mirror is
// InvalidDataException. Thrown at write (today: a comment the comment
// table cannot carry). Sealed; the three standard constructors. The data
// is typed and set only by the library; the message is composed from it.
public sealed class XgUnrepresentableValueException : Exception
{
    public XgUnrepresentableValueReason Reason { get; }   // Unspecified only via a standard ctor
    public int?  CommentIndex { get; }   // the comment's index in the written table; null = not a comment
    public Rune? Character    { get; }   // set exactly when Reason is UnencodableCharacter
}

public enum XgUnrepresentableValueReason
{
    Unspecified = 0, UnencodableCharacter = 1, UnpairedSurrogate = 2, ReservedCrlfEscape = 3,
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
    OpeningBook? OpeningBook = null,
    PlayRanking  Ranking     = PlayRanking.Equity);   // undefined refused

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

    // The keyed lookup (TryGetEntry / GetEntries over OpeningBookKey) and
    // OpeningBookEntry are internal: keying needs the internal record
    // position convention. Public intent is handing the instance to
    // XgIteratorOptions.OpeningBook for depth enrichment.
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

// The header types (see "The header types" under Architecture). Every
// member is required on the wire and never null; built only by this library.
public sealed class XgMatchInfo : IMatchInfo
{
    public string       Player1 { get; }
    public string       Player2 { get; }
    public SessionTerms Terms   { get; }   // MoneyTerms or MatchTerms
}

public sealed class XgGameInfo : IGameInfo
{
    public bool         IsStandardStart { get; }
    public GameStanding Standing        { get; }   // MoneyStanding or MatchStanding
}
```

Produces types defined in `BgDataTypes_Lib`; see that subproject's
`INSTRUCTIONS.md` for their shapes and serialization contract.

## Pitfalls

* **XG's files use two frames; the record uses one.** A record's board is
  on-roll-relative. XG stores a decision's starting position in player 1's
  frame, but each candidate's resulting position and the position after
  the move in the *mover's* frame (see "Board format"). `OnRollBoard` turns
  only the starting position; a derived after-board is compared with XG's
  stored one turned (`BoardPosition.Flipped`), for either seat. The XGID is
  BgDataTypes_Lib's derivation, not a frame this library handles.
* **Cube and play decisions are 1:1 with emitted rows.** Both `Iterate`
  and `IterateDiagramRequests` produce exactly one `DecisionRow` /
  `BgDecisionData` per decision they emit, and the same decisions: a row
  is its record's projection. For cube decisions this is
  the doubler's board (no flip); there is no second taker-perspective
  row. Consumers may safely count one row per decision.
* **A Crawford game's cube pane is not a decision and is not emitted.**
  XG writes and analyses cube panes in the Crawford game like any other,
  so a fixture's cube-record count is not its cube-decision count. Both
  surfaces drop them at the shared dispatch site (`AdmitsCubeDecision`),
  and the wire types would refuse the record anyway (halheinrich/backgammon#201).
* **The `$type` discriminator has one home.** `SaveRecordConverter`'s
  table maps every `RecordType` member to its class, and the strings on
  the wire are the members' names. Adding a record variant means one table
  row; a variant missing from it fails at write (unnamed or mismatched
  tag) and at read (unknown discriminator), never silently. Do not reach
  for `[JsonPolymorphic]`: `UnknownRecord` carries two tags, and the
  built-in mechanism binds one discriminator per derived type
  (halheinrich/backgammon#177).
* **`.xgp` sentinel handling is easy to regress.** `-1000` means "unanalyzed"
  for `MoveError` / `ErrorCube`; anything `> -999.0` is a real error. Using
  `!= 0` or `.HasValue` checks on raw fields will silently treat unanalyzed
  positions as zero-error.
* **Cube `IsAnalysed` must gate on `Analysis.Level`, not `LevelRequest`.**
  `LevelRequest` is a setting (what the user asked XG to run), `Level` is
  provenance (what produced the pane's stored equities) — the full model,
  with the re-measured census, is "Level semantics: `LevelRequest` vs
  `Level`" in Architecture. Widening the gate with `LevelRequest` (`||`
  between the two) buys nothing — every analysed pane in the corpus
  carries a positive request too — and re-admits the queued-never-ran
  phantom (`Level == -100` with a non-zero request; observed once, in
  `FixtureFiles/Opening 32 65 64 31 65.xgp`).
* **On a cube record, `Level == 0` means *unanalysed*, not "1-ply."** The
  gate above is `Analysis.Level > 0`, and the `> 0` is deliberate — not an
  off-by-one. Code `0` is a legitimate level in XG's code space
  (`XgDepthFacts.OfLevel`: 1-ply), so `>= 0` looks like the more correct spelling; it is not. Ruled
  2026-08-28 (halheinrich/backgammon#132): XG never runs a 1-ply cube
  analysis, so on the cube side a zero `Level` is the default-valued,
  never-analysed pane. Corpus-verified over 553 local files — **23,049** cube
  records carry `Level == 0`, and of those **not one** carries any of the
  three cubeful equities, **not one** carries a non-zero `LevelRequest`, and
  **all 23,049** carry `IsBeaver == -100`, the documented never-analysed pane
  sentinel. Every genuinely analysed cube in the same corpus ran at level 1
  (2-ply, 9,487), 3 (4-ply, 73), 4 (5-ply, 5,537), 100 (rollout, 184), 1001
  (34) or 1002 (932); level 0 never appears as real analysis. Tightening the
  gate to `>= 0` would admit 23,049 empty cube rows. Re-measured 2026-08-31
  over the then-360-file corpus (halheinrich/backgammon#161): 23,036
  records, identical all-zero pattern — see "Level semantics" in
  Architecture. Note the asymmetry with
  the checker side, where 1-ply *is* a real analysis level that files do
  carry — which is why `IsAnalysed(MoveRecord)` gates structurally
  (`MoveCount` / `Evals`) instead of on a level at all.
* **`ReadCheckerPlay` returns `null` for every move record that is not a
  record** — a position that is not a decision, a sentinel, no roll, a
  corrupt candidate (see "What is not a record"); the dispatch emits only a
  non-null result. Keep each rule there, ahead of `BuildCheckerPlay`, so
  the two surfaces cannot disagree about which plays become records.
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
  `IterateCore`, whose signature carries the non-nullable
  post-validation invariant. Required because every
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
  that path is deferred (it fires during enumeration, when a decision
  reaches one of the two record builders) and is enforced per-record
  rather than at the API boundary.
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
  case, in the since-retired after-board builder) or a "1/1" notation
  glitch (the `(0, 0)` case in `XgMoveTranslator.Translate`). Both surfaces
  gate emission in `ReadCheckerPlay` (`ClassifySentinelAnalysis`), so the
  translator never sees a sentinel. Do not add a sentinel branch to the
  translator: sentinel semantics belong with the iterator that decides
  what to emit, not with the leaf that operates on the move encoding.
* **The record model is internal by design; the builder is the only
  synthesis path.** Do not re-publicize a record type to unblock a caller —
  the caller's need is a missing intent on `XgFileBuilder` (or, for tests
  of this repo only, `InternalsVisibleTo` already covers it).
  `PublicSurfaceTests` fails on any drift back to `public`, and
  `JsonContractTests` pins the JSON output contract against embedded
  goldens captured before the change — regenerate those goldens only for a
  deliberate, documented contract change, and re-tie them to the binary
  fixtures (the `Golden_ParsesToTheSameModel` leg) when you do.
* **`XgRecordFactory` is the synthesized-record SSOT.** The builder and
  `XgpExporter`'s clean path must keep drawing header defaults, sentinel
  panes, and the cube encoding from it; a field added to a synthesized
  record shape belongs there, not in either caller.
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
  on the record, read as money terms by `XgMatchInfo.From` (a length at or
  above the sentinel), the one reading. One
  consequence for consumers: `MatchHeaderRecord.IsMoneyMatch` is *not*
  the raw XG byte — it is that byte OR'd with Galaxy detection.
* **Two opening books, and the level codes read "backwards": 999 = Book V1,
  998 = Book V2.** XG 1's `OpeningBook.db` (V1) stamps level 999; XG 2's
  `OpeningBookV2.ob` (V2) stamps 998 — the *lower* code is the *newer*
  book. The depth decoding once had the labels reversed; the spec's PLAYERLEVEL
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
  book hit — pinned bitwise by `RealDb_FixtureA_…`). **Measured
  2026-09-27, not changed here: the key misses for player-2 movers.** The
  keying was proven on player-1 movers only, and XG stores each
  candidate's resulting position in the mover's frame for either seat (see
  "Board format"), so a player-2 mover's key needs the flip too. Over the
  corpus's centred-cube V2-book-stamped candidates, the key as built hits
  216 of 3,574 player-2 candidates (6 with an entry's evaluation
  bit-identical to the pane's); flipped for either mover it would hit
  3,571 (3,513 bit-identical), with player-1 movers unchanged (3,684 of
  3,688 either way). A player-2 mover's book hit therefore states its bare
  book facts today.
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
* **Book enrichment changes depth facts, never emission — and a 998
  stamp is not always rollout-backed.** The optional
  `XgIteratorOptions.OpeningBook` threading is strictly additive: with and
  without a book, the same decisions and candidates are emitted (pinned by
  the fixture (a) with/without pair). Each candidate resolves its *own*
  entry — fixture (a)'s decision enriches different candidates to
  different rollouts (12,960-game 4-ply vs 15,552-game 3-ply). And two of
  its five 998-stamped candidates resolve to the book's **Roller++
  evaluation baseline** entries (Level 1002, zero trials): XG stamps 998
  whenever the book supplied the pane numbers, rollout or not. Those hits
  deliberately state the bare book facts (`BookRollout`, level `Unknown`,
  edition V2) — there is no cached rollout to recover. Do
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
  by design (`BuildCube` passes no entry). If a book-stamped cube
  decision ever surfaces, pin the key against it before wiring cube
  enrichment — `XgDepthFacts.Resolve` would also need to select
  `RolloutCubeLevel` rather than `RolloutMovesLevel` for that path.
* **Book selection: deeper rollout levels beat more games — by this
  library's policy, which is not XG's display rule.** One key commonly
  holds several entries (5,113 keys with more than one rollout, 2,099 of
  them at differing moves levels), and the most rigorous wins: any rollout
  over the Roller++ baseline, then the deeper level over more games, with
  recency and file order only final tiebreaks. XG's tooltip agrees on one
  observed key and disagrees on another; both cases and the ruling that
  the policy stands are on `OpeningBook`'s class doc
  (halheinrich/backgammon#203) — cite them from there rather than claiming
  parity anywhere. One residual ambiguity, documented on `OpeningBook`: moves
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
  invertible; static levels invert exactly via `XgDepthFacts.OfLevel`;
  rollout contexts are the one unrecoverable piece (level 1002 with
  `RolloutIndex = -1` is XG-legal per the `DoubleAnalysis.xgp` fixture,
  unverified for move panes).
