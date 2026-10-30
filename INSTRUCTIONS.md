# ConvertXgToJson_Lib

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)
> Open work: the [tracker](https://github.com/halheinrich/backgammon/issues), label `ConvertXgToJson_Lib`.

## What it is for

The ecosystem's reader and writer of eXtreme Gammon's binary formats. It
parses `.xg` match files and `.xgp` position files into records defined by
`BgDataTypes_Lib`, and writes XG binary files back out, including `.xgp`
position export from a `BgDecisionData`. What XG's files look like stops at
this library: the on-disk record model is internal, and a consumer sees a
file only as the opaque `XgFile`.

A file flows one way through it: `XgFileReader` unwraps the binary container
into an `XgFile`, `XgDecisionIterator` walks its record stream, and each
analysed decision comes out as a `BgDecisionData` record or a `DecisionRow`.
The writers run the first step backwards.

## Which surface for which task

Each type's XML doc, built on every build, is its contract and its signatures;
this table says where to start.

| To… | Use |
|---|---|
| read a source in a walk, a failure being an outcome to skip or report | `XgSourceReader` (see "The read operation") |
| read one file, a failure being an exception | `XgFileReader.ReadFile`, `ReadStream`, `ReadJson` |
| list a directory's XG files | `XgFileReader.EnumerateXgFormatFiles` (the two overloads order differently; see "XgFileReader") |
| read only the players and terms, or the games' standings | `XgFileReader.ReadMatchInfo`, `ReadGameHeaders` |
| get the decisions of a parsed file | `XgDecisionIterator.IterateDiagramRequests` (records) or `Iterate` (rows for a ranking) |
| observe, stop or configure a walk | `XgIteratorState`, `XgIteratorCallbacks`, `XgIteratorOptions` |
| say which rollout stands behind a book-stamped candidate | `OpeningBook.Load` / `TryLoad`, handed to `XgIteratorOptions.OpeningBook` |
| write or read the JSON document | `XgFileReader.ToJson`, `WriteJsonAsync`, `ReadJson` |
| write an `XgFile` back to XG binary | `XgFileWriter` |
| export one decision as an `.xgp` | `XgpExporter`, by what you hold (see "Writing") |
| make a file in memory, for a test or a tool | `XgFileBuilder` / `XgGameBuilder` |

The usual walk over one file, exporting each decision of a match on the way:

```csharp
XgSourceRead read = XgSourceReader.ReadFile(path, XgSourceFormat.Xg);
read.Switch(
    decoded =>
    {
        string name = Path.GetFileName(path);   // the extension chooses the id's shape
        foreach (BgDecisionData decision in XgDecisionIterator.IterateDiagramRequests(decoded.File, name))
            if (decision.Id is XgDecisionId id)   // an .xgp's XgpDecisionId has no coordinates
                Save(XgpExporter.ToBytes(decoded.File, id, XgpSliceOptions.Anonymized));
    },
    failed => Report(failed.SourceName, failed.Exception));
```

## Stack

C# / .NET 10 / Class Library / xUnit.

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\ConvertXgToJson_Lib\ConvertXgToJson_Lib.slnx`

From this directory:

```bash
dotnet build ConvertXgToJson_Lib.slnx
```

```bash
dotnet test ConvertXgToJson_Lib.slnx
```

The benchmarks run by hand, never in the test run (see Layout).

**What the tests read.** `TestData` is the umbrella's, shared at
`backgammon\TestData` and gitignored; `TestPaths._root` reaches it from
`AppContext.BaseDirectory`. The suite took about six minutes against the
local corpus on 2026-10-30. Three kinds of test read differently:

* Gating tests synthesize their files through the builders and need nothing.
* Corpus tests sweep whatever `TestData/xg` and `TestData/xgp` hold. Without
  the corpus some pass vacuously, by design (`XgCorpusAgreementTests`,
  `ReadMatchInfoTests`, `ReadGameHeadersTests`), and others fail, asserting
  they saw at least one decision (`XgDecisionIdStampingTests`). A file added
  to the corpus can fail one: it holds the invariant, not the sample.
* Fixture tests name files under `TestData/FixtureFiles` and fail when a file
  is missing (`TooGoodPassFixtureTests`, `OpeningBookRealDbTests`,
  `XgpExportXgAgreementTests`).

A new test class that reads or writes files joins `[Collection("FileIO")]`,
which disables parallelization among them.

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
  `Play.ToNotation()`. Its `INSTRUCTIONS.md` gives their shapes and
  serialization contract. The test project also references
  `BgDataTypes_Lib.TestSupport`, whose `TestRecords` builds the records a
  test needs by hand.

## Layout

Three projects under `ConvertXgToJson_Lib.slnx`, governed by repo-root
`Directory.Build.props` (TFM, `TreatWarningsAsErrors`, XML doc generation)
and `Directory.Packages.props` (Central Package Management — no inline
`Version=` anywhere). `docs/xg-format/` holds XG's own format sources; its
`README.md` says what each is and where it came from.

**`ConvertXgToJson_Lib/`** — the library. Seven areas:

- **Reading** — `XgSourceReader`, the one read of one source (acquiring
  and decoding it under one failure policy, returning an `XgSourceRead`:
  `XgSourceDecoded` or `XgSourceFailed`, in the `XgSourceFormat` the caller
  states), and `XgFileReader` (discovery, and the decode primitives: full
  parse, the fast match-info and game-header paths, the JSON document) over
  `Parsing/`:
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
  its constraint typed as `XgUnrepresentableValueReason`; `CommentWriter`'s
  doc comment is the one statement of what a comment cannot carry
  (halheinrich/backgammon#234).
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
`ResolverPaths` options trio, `EmissionMirror`, `CapturingLogger` among
them). `XgCorpusAgreementTests` measures the converter against XG's own
numbers over the local corpus, and `TooGoodPassFixtureTests` holds permanent
fixtures reaching the fourth cube answer, read as Too good (see "Measured
against XG" under XgDecisionIterator).

**`ConvertXgToJson_Lib.Benchmarks/`** — a console tool, run by hand and
never by `dotnet test` (halheinrich/backgammon#438): the fast paths
(`ReadMatchInfo`, `ReadGameHeaders`) timed against the full parse over
`TestData/xg`, each printed with its speed-up and whether the expected 3×
held. Run it in Release, as the startup project in Visual Studio or
`dotnet run -c Release --project ConvertXgToJson_Lib.Benchmarks [<xg dir>]`;
it exits 1 when an expectation did not hold, 2 on fewer than two files.

## Architecture

### Record model is internal

The XG on-disk record model (`Models/Models.cs`, plus `OpeningBookEntry` /
`OpeningBookKey`) is `internal` by design (halheinrich/backgammon#131 — the
deep-module cut). `XgFile` is the public opaque handle: internal constructor,
internal `[JsonInclude]` collections, no public members. Consumers say what a
match *is* through `XgFileBuilder` and consume decisions through the iterator;
nothing outside this assembly (plus its test project via
`InternalsVisibleTo`) sees a record. `PublicSurfaceTests` pins this;
`JsonContractTests` pins that the JSON document (XgToJson's output contract)
survived the change byte-for-byte, against goldens embedded in the test
project.

`XgFile`'s namespace is still `ConvertXgToJson_Lib.Models`, an accident of
history: the placement predates the internal model and moves with the repo's
rename (halheinrich/backgammon#19), not before.

### XgFileReader

Discovery and the decode primitives. A primitive throws for a source it cannot
read; the read that returns the failure instead is `XgSourceReader` (below),
which decodes through them.

* **Discovery.** `XgFormatExtensions` is the one extension set, and a caller's
  walk lists through `EnumerateXgFormatFiles`, never a `*.xg*` glob. The two
  overloads order differently, and the difference is documented on both: the
  single-argument one is extension-major in filesystem order; the
  `SearchOption` one is sorted by full path, deterministic and contractual.
  Pin user-visible sequencing, export numbering say, to the second.
* **`ReadFile` / `ReadStream`** — the full parse. Format is detected from
  content, so a walk reads `.xg` and `.xgp` alike. A payload it cannot read is
  refused as `InvalidDataException` naming the failed check (see "Reading —
  refuse, never guess" under Writing).
* **`ReadMatchInfo` / `ReadGameHeaders`** — the fast paths: temp.xg alone,
  selected through the same validated directory the full read uses. A result
  certifies temp.xg and nothing else: `ReadFile` can still refuse the file
  over an inner file this read never extracted. Every refusal of
  `ReadGameHeaders` is deferred to the first `MoveNext`, and a consumer stops
  early by breaking out of the foreach; there is no imperative skip flag.
* **`ToJson` / `WriteJsonAsync` / `ReadJson`** — the JSON document, through
  `XgJsonOptions.Default`. `ReadJson` decodes through the internal
  `DecodeJson`, the one JSON decoder, which `XgSourceReader` calls too.
  `SaveRecordConverter`'s class doc states the `$type` mapping and why it is a
  converter (halheinrich/backgammon#177); see Pitfalls.

### The read operation (XgSourceReader)

Reading one source — acquiring its bytes and decoding them — has one owner
(Hal's ruling of 2026-10-08 on halheinrich/backgammon#373; the work is
halheinrich/backgammon#359): `XgSourceReader`, which returns the outcome
instead of throwing for a source that cannot be read, so every walk over
many sources reads each one the same way and decides for itself what a
failed source means. It reads and nothing more: converting (the iterator,
its opening book and ranking) and filtering stay with the caller.
`XgSourceReader`'s class doc is the statement of the contract, its resources,
cancellation and exception precedence included; the decisions behind its
shape:

* **One entry point per kind of source**, named after `XgFileReader`'s:
  `ReadFile` / `ReadFileAsync` (a path), `ReadStream` (a stream the caller
  holds) and `ReadAsync` (an acquisition the caller supplies, awaited with
  the caller's token — a browser upload's stream). The kinds differ in who
  owns what and in whether there is a token, so each is its own entry
  point, and a synchronous caller cannot reach an asynchronous acquisition:
  one source-descriptor type taken by a sync and an async read would have
  had to refuse that pairing at run time.
* **The format is the caller's statement** (`XgSourceFormat.Xg` or
  `XgSourceFormat.Json`): a required argument on every entry point, with no
  default (Hal's ruling of 2026-10-11; `PublicSurfaceTests` pins it), and
  never inferred from the name, which is attribution only. The enum's zero
  value is `Unspecified`, which every entry point refuses, as it refuses an
  integer that is no member (Hal's ruling of 2026-10-11, after
  `XgUnrepresentableValueReason.Unspecified`): an unset field never chooses
  a decoder. That refuses a format nobody stated; it cannot show that a
  stated one fits the bytes. One switch, `DecoderFor`, states which formats
  decode and how, so the validation and the dispatch cannot disagree.
* **The outcome is a closed pair**: `XgSourceRead` is `XgSourceDecoded` or
  `XgSourceFailed`, each carrying `SourceName`, with `Match` / `Switch` as on
  BgDataTypes_Lib's closed pairs (`GameStanding`). Reference semantics: an
  outcome is one read's result. The failure is the exception itself, typed,
  never flattened to text; which stage raised it is not recorded, since one
  policy covers both and the exception's type already says.
* **One failure policy, stated once in code** (`FailsTheSource`): every
  exception raised while acquiring or decoding fails the source, whatever
  its type, save an `OperationCanceledException`. Usage errors stay outside
  that boundary and are thrown, by the asynchronous entry points from the
  call itself, before a task exists (the two-method shape the iterator uses
  for `sourceFile`).
* **Retained bytes are opt-in** (`retainBytes: true`, on the entry points
  that acquire): a caller that copies an unchanged `.xgp` verbatim keeps
  them and never acquires the source again; a caller that does not ask
  keeps no copy. `ReadStream` never retains — its caller holds the source.

### The header types (XgMatchInfo / XgGameInfo)

The public metadata a match header and a game header state, projected at
the parse boundary (`XgMatchInfo.From`, `XgGameInfo.From`, the one
projection each) and handed to the state, the callbacks and the fast paths.
`Terms` is a `MoneyTerms` when the header's length is XG's money sentinel
99999, Galaxy's money games included (see Pitfalls), else a `MatchTerms`; the
sentinel is never surfaced as a length. `Standing` is the matching
`MoneyStanding` or `MatchStanding`. A decision's `Session` is the two turned
to the player on roll, through `Session.Create(terms, standing, onRoll)` —
`MatchContext.SessionFor`, the one construction. Each type's remarks state
its wire rules.

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

The reader's mirror, layered like the read path: `Writing/` mirrors the binary
parsers under `Parsing/`, and `XgContainerLayout` is the one spelling of the
container both sides go through.

* **`XgFileWriter`** — record-level serializer: `XgFile` → stream / bytes.
  Format-generic by construction (it writes whatever record list the model
  holds, so full-`.xg` output works), but only the `.xgp` shape has been
  imported into real XG, by hand, the sample exports under
  `TestData/XgpExportSamples`. It writes only the inner files the model holds,
  so a file whose manifest named one the reader set aside (temp.xgr2) loses
  that entry when read and written back; the JSON round trip in
  `XgFileWriterTests` cannot see that, and preserving such entries is not
  supported (halheinrich/backgammon#419).
* **`XgpExporter`** — decision-level export, three surfaces chosen by what the
  caller holds. "Exporter" per the ecosystem convention (`MatExporter` is the
  precedent): an Exporter translates semantics, a Writer/Reader mirrors byte
  layout. Consumers never touch record internals.

| Surface | The caller holds | It emits | This library's iterator then yields |
|---|---|---|---|
| Clean, `Write(BgDecisionData, …)` | only the record (JSON-sourced) | an unanalyzed position; XG re-analyzes on import | nothing, by design (see Pitfalls) |
| Slice, `Write(XgFile, game, moveNumber, isCube, …)` or by `XgDecisionId` | the parsed source and the decision's coordinates | XG's own save-from-match shape, analysis intact | exactly one decision, analysis and rollout depth intact |
| Anonymize-copy, `Write(XgFile, XgpSliceOptions, …)` | the finished file shape, typically a parsed single-position `.xgp` | every record, rollout context and comment verbatim; only the header's names rewritten | what the source yields |

The class doc states each surface. What it does not carry:

**Clean-position export.** The on-roll player is normalized to player 1, so
the record's session maps onto the header verbatim; a match's Max Cube is
written as XG's default exponent, since its terms state no limit. A centred
cube above 1 has no representation without XG's auto-double bookkeeping and is
refused as `NotSupportedException` rather than misencoded. Output is
byte-deterministic: no timestamps or random ids. Clean exports
**self-identify** via the match header's Location fields
(`"ConvertXgToJson_Lib"`): Location is the ecosystem's producer fingerprint
(Galaxy writes `"BackgammonGalaxy"` there and `IsGalaxyMoneyGame` keys on it),
so the string is provenance and a stable hook for ever special-casing our own
exports. Treat changing it as a breaking change
(`Export_SelfIdentifiesInLocation` pins it).

**Slice export** follows XG's own save-from-match, learned from the
XG-authored agreement fixtures. Match- and game-level comments are **not**
carried: XG mirrors the match header comment into the outer header's
plain-text `Comments` field (`CommentsAddedToXgp.xgp` ground truth), so
carrying them would drag RTF→plain-text extraction in. The decision records'
comments are, into a fresh dense table, as XG's own SaveAs of a commented move
does (`CommentExported.xgp` ground truth). `XgpSliceOptions`' remarks state
how slot names and role names resolve; `Anonymized` is the SSOT for what
anonymized export means.

**No analysed export from a record alone.** A caller holding only
`BgDecisionData` gets the clean export; the slice needs the parsed source.
Reconstructing `BestMoveAnalysis` / `DoubleActionAnalysis` from a record is
a hypothetical, worth revisiting only if a JSON-sourced caller ever needs
analysed exports. What is known of it: the record carries the candidates'
equities and probabilities and the cube's; the after-boards are recomputable;
the sbyte move encoding inverts; static levels invert by `XgDepthFacts`'s
table (codes 1 and 11 both read as 2-ply, so 11 would come back as 1); and
rollout contexts are the one piece that cannot be recovered (level 1002 with
`RolloutIndex = -1` is XG-legal per the `DoubleAnalysis.xgp` fixture,
unverified for move panes).

Ground-truth oracle beyond round-trips: `XgpExportXgAgreementTests`
compares exports field-level against two XG-authored `.xgp` saves of
decisions whose source `.xg` is also pinned (`MTCH4064_1_22.xgp`, a play;
`match35253054_2_37.xgp`, a rolled-out cube saved with player 2 on roll).
Clean-path comparisons run through the on-roll lens (XG preserves source
perspective, the clean path normalizes); slice comparisons are direct
record equivalence — analysis included — with one documented exclusion:
the tutor family (`ErrorTutor*`, `TutorPosition`), which XG initializes at
runtime while imported sources carry zeros. Never byte identity.

**Container facts.** `XgContainerLayout`'s class doc is the statement of the
layout and of its evidence: the manifest and end-record tables, the 434 files
read byte by byte on 2026-10-05 (halheinrich/backgammon#343), xgdatatools'
`xgzarc.py`, XG's own sources in `docs/xg-format/`, and which values are
observed rather than specified. `XgContainerWriter`'s doc states what is
written and `XgDecompressor`'s the reading contract, check by check.
`XgFileWriterTests` pins the layout; see Pitfalls for what a round trip cannot
vouch for. What those docs leave to here:

* **Reading — refuse, never guess** (Hal, 2026-10-05: "Refuse with clear
  error"). The directory decides first: a payload whose directory validates
  is a container, read through it, even if it might also parse as one zlib
  stream. Only when the directory fails a check is the payload the legacy
  single-stream format, exactly when one zlib stream begins at its first byte
  and ends at its last; otherwise it is refused, naming the check that failed.
  No inner file is ever assigned by record size. An entry naming none of the
  four inner files passes the same checks and is set aside, because XG's
  archive unit accepts any name and real files carry a fifth entry, temp.xgr2
  (Hal's ruling on halheinrich/backgammon#419). One limit: a container cut off
  exactly at the end of its first stream has no directory and is one complete
  zlib stream, so it cannot be told from a legacy file and reads as one.
* `temp.xgi` holds exactly two records: byte-copies of the first and last
  records of the emitted stream. (XG's own `.xgp` saves carry its session's
  first/last, the "last" often not in the `.xgp` at all, so XG evidently does
  not hold the pair to the file's records. That is inferred from what XG
  writes; no file has been put to XG to test it. Self-consistent first/last
  is the clean choice either way.)
* XG stamps one constant GUID into its RichGameHeaders, and the exporter
  reproduces it; `XgRecordFactory.XgGameId` holds the value and its basis.

### Synthesis (XgFileBuilder / XgGameBuilder)

The one public way to make an in-memory `XgFile` — intent-level inputs, the
record stream synthesized behind the handle. The two classes' remarks state
the position frame, the tracked state, the validation and the comment rule.
What a test author most needs: the builder writes XG's own two frames as XG
does, a decision's starting position in player 1's and every resulting
position in the mover's, for either seat (see "Board format";
halheinrich/backgammon#290), and the decision shapes it records are XG's:

* `Play` — an analysed play, the played move as its only 1-ply candidate
  (XG's analysed forced play) or explicit `XgPlayCandidate`s.
  `UnanalysedPlay` advances the position but never emits. `Dance` /
  `IllegalPlay` record XG's two non-play sentinels as intents.
* `CubeDecision` — an analysed cube; XG's stored errors are *derived* from the
  equities and the played actions, and the played actions map onto the
  `Doubled`/`Taken` pane state (the inverse of the iterator's mapping; see
  "Cube decisions"). `ply` is what *ran*; the optional `requestedPly`
  synthesizes XG's ordinary requested ≠ ran governor shape (see "Level
  semantics"), stamped on the pane pair and the record-level play-time stamp
  alike. `UnanalysedCube` is the incidental pane; its actions still move the
  cube.

Validation is eager and loud at the offending call, so an unrepresentable
match — Crawford at a non-Crawford score, a 16-checker board, a reply without
a double — never reaches `Build()`. Dice legality is deliberately not checked.

`XgRecordFactory` is the SSOT both synthesis paths draw from (see Pitfalls).
Two level-code facts live here: XG's PLAYERLEVEL code for an N-ply evaluation
is **N − 1** (`ToLevelCode`), and because the iterator gates analysed cubes on
`Level > 0`, a 1-ply cube pane (level 0) is downstream-indistinguishable from
an unanalysed one — `CubeDecision` therefore refuses `ply: 1` rather than
synthesize a decision that silently never emits.

### XgDecisionIterator

Two iteration surfaces over one walk of the record stream:

* `IterateDiagramRequests` yields the `BgDecisionData` records — one
  `CheckerPlayDecision` or `CubeDecision` per analysed decision.
* `Iterate` yields each as a flat `DecisionRow`, built from its record by
  `DecisionRow.From(record, ranking)` for the options' ranking. A row and its
  record therefore cannot disagree.

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
the error of a stated cube action. Add none of those back here.

**Candidates keep XG's order.** `CheckerPlayDecisionData.Plays[0]` is XG's
own rank 0. Which play is best, the order a consumer shows, each play's error
and whether it is scored are a ranking's (SPEC-scoring §2a: "Where both keys
are equal, the analyser's order stands"), derived by `RankedBy(ranking)`. The
played candidate is found by resulting position: the candidate whose
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
  walk from reaching that guard.
* **A position that is not a decision** — a side has borne off all its
  checkers (Hal's ruling of 2026-09-27, halheinrich/backgammon#273).
  Legitimate XG/XGP data, simply not a decision in the record model:
  passed by silently, as ordinary filtering, for both kinds. The rule is
  `PositionData.IsDecisionPosition`'s, asked, never restated.
* **XG's two non-play sentinels** — the illegal-play marker, skipped with
  a warning naming the file, game, move and roll, and the dance, skipped
  silently (see Pitfalls).
* **A move record that states no roll** (`StatesRoll`).
* **A corrupt candidate** — a candidate invalid from its own position,
  which the record would refuse (Hal's ruling of 2026-09-25 on invalid
  plays, applied to stored candidates). `ReadCheckerPlay` tests every
  candidate with `BoardState.TryApplyPlay` on a **fresh board per
  candidate**, since the method applies a valid play and turns the board,
  so a shared one would test later candidates from the wrong position. The
  decision is skipped the way the illegal-play marker is, with a warning
  listing each invalid candidate; the rest of the file still emits.

Nothing is caught to find any of these, and nothing else is caught or
skipped: any other refusal from BgDataTypes_Lib fails loud. For an `.xgp`
every rule acts upstream of the single-decision policy, so a play skipped
for any reason never suppresses the file's analysed cube.

**The ranking.** `XgIteratorOptions.Ranking` is the `PlayRanking` the walk
judges decisions under, `PlayRanking.Equity` by default (SPEC-scoring §2a:
"Apps without the setting use the default"). `Iterate` builds every row for
it, and the post-yield callbacks of both surfaces see each decision through a
view built for it: the row itself, or the record's `ViewFor(ranking)`, since a
record does not depend on the ranking. `XgDecisionIteratorRankingTests` pins
each half.

**Ids.** Every record, and so every row, is stamped with a
`BgDataTypes_Lib.DecisionId`; a record's and a row's game and move number are
derived from it (halheinrich/backgammon#124). `BuildDecisionId`'s doc states
the dispatch on the source file's extension: `.xg` and `.json` give an
`XgDecisionId`, `.xgp` an `XgpDecisionId` of the bare filename. A `.xg` and a
`.json` of the same content are distinct artifacts and carry different Ids;
cross-format Id identity is not an invariant.

What makes the bare filename a valid key is the emission policy: XG itself
does *not* guarantee one decision per `.xgp`, since a position saved after the
dice were rolled can carry analysis in both panes, so an `.xgp` yields **at
most one** decision: the checker play if the walk built one, else the analysed
cube. The move pane exists only because dice were rolled, so dice in the file
mean the saved decision is the play; depth is not compared. The selection is
made before the stop callbacks judge anything (see "The iterator's parameters").

A decision with an `XgpDecisionId` has no game or move number, and its
`DescriptiveData.IsStandardStart` is `null` — read off the id at the one site
both kinds share, so the record, which holds the two to agreement, never sees
them differ (halheinrich/backgammon#124). Cube decisions stamp
`ctx.MoveNumber + 1`, so a cube decision numbers as the move its turn goes on
to play: the cube and the play that follows share a move number, told apart by
their kind.

**A walk over many files is the caller's.** This library owns no
directory walk: its two internal ones, test-only, dropped a file that
failed to read without a word, and were removed rather than given a
reporting channel nobody consumed (halheinrich/backgammon#359). A walk
composes the public pieces, as the example at the top does, under each file's
name with the extension. XgFilter_Lib's `FilteredDecisionIterator` is the
in-tree walk, adding the skip report and filter callbacks this producer
deliberately doesn't own.

**Depth: the typed facts.** A candidate's and a cube analysis's depth is
stored as typed facts — the `AnalysisMode` × `AnalysisLevel` pair, the
rollout trial count, the book edition, an unrecognized level's raw code —
and BgDataTypes_Lib derives the label, abbreviation and rank from them.
`XgDepthFacts` is what stays here: the one decoding of XG's PLAYERLEVEL codes,
rollout contexts and book stamps. Its `OfLevel` is the table and its remarks
state the rollout rule. The rulings behind them: code 11 reads as plain 2-ply,
identified by XG's own display (user-ruled 2026-08-28,
halheinrich/backgammon#160); XG level 12 is its own `Ply3Red`, and the
evaluation order, XG's menu order with the ply and XG Roller families
interleaved, is `AnalysisLevel`'s contractual declaration order (ruled
2026-08-28); a rollout's inner level is its **first** phase's when a first
phase exists, since the first phase is the strength the user set and a later
cheaper phase an economy (the user's ruling on halheinrich/backgammon#251). A
cube analysis's depth resolves from `Level`, never the `LevelRequest` setting
(halheinrich/backgammon#161; see "Level semantics").

**Book enrichment.** `XgIteratorOptions.OpeningBook` carries a loaded book
database (locating the `.ob` on disk is app configuration). For each
V2-book-stamped (998) checker-play candidate, `LookupBookEntry` builds the key
`OpeningBookRealDbTests` pins on its fixture decisions, `PositionsPlayed[i]`
plus the decision's session through the `OpeningBookKey` factories, and hands
the selected entry to `XgDepthFacts.Resolve`. Every candidate resolves its own
entry, so a decision's candidates enrich to *different* rollouts. A rollout
entry enriches the hit: it states the entry's rollout moves level and trial
count beside its edition. The rank stays the book's whatever the enrichment
(BgDataTypes_Lib's grid ranks every book hit 99, ruled 2026-08-28: the rank
answers "what does the file record?"). The bare book facts (`BookRollout`,
level `Unknown`, the edition) stand on no book supplied, a V1 stamp (999 — the
V2 database wasn't its source), a lookup miss, a context outside the pinned
keying (cube not centred at 1), and a hit on an evaluation entry; see Pitfalls
for that one and for cube decisions, which never look up.

**Measured against XG.** BgDataTypes_Lib owns the cube truth's rule and its
reading and verifies them with synthetic tests; this repo owns every comparison
against facts XG actually stores, its evidence in three kinds (SPEC-scoring §3,
"The truth-claim derivation"; Hal's ruling of 2026-09-27 on
halheinrich/backgammon#273). `XgCorpusAgreementTests`' class doc states the
first two and each invariant it asserts: agreement with facts XG stores, over
the local `TestData/xg/` and `TestData/xgp/`, and measured counts it reports
and never asserts. It pins invariants, never a count. The third is
`TooGoodPassFixtureTests`: named permanent fixtures whose cube records reach
the fourth cube answer, read as Too good, on real input. That shows our rule
reaches the answer on XG's stored numbers, not agreement with an XG Too Good
label: XG's files carry no analysis text, and its two stored cube choice fields
(`ComputerChoice`, `DoubleChoice3`) are undocumented and agree with the pane's
stored equities only at 1-ply (the umbrella's measurement, 2026-09-27), so
nothing here reads them.

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
and No double / Pass in 23. Measured 2026-10-02 (UTC) over the 21 `.xg`
fixtures, all of which parsed (none failed): 133 cube records, across
eleven of the files, have the fourth answer as their truth, 129 reading
Too good and 4 No double / Pass. Dated evidence, not pins.

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
panes, where they hold a stale ply-family value — consistent with a stamp a
later, deeper interactive re-analysis does not update. The pane is the
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
candidates. XG's format document does not cover this file. The layout was
worked out from its bytes (d571b29, 2026-07-18): every datum XG's tooltip
shows for two entries, the ajhhBG0407 game 9 Kazaross rollout and a Steven
Carey 9-away/9-away rollout, was matched to a field, and
`OpeningBookRealDbTests` pins both, with a hit's evaluation bit-identical to
the pane XG copied it into. A field the tooltip does not show has no such
check.

**File format.** `OpeningBookParser`'s class doc and `ParseEntry` state the
blocks and every offset, and `OpeningBookEntry` what each field means,
tooltip derivations included. One thing to carry into any change: blocks are
Delphi memory dumps, so bytes past a block's live fields are stale heap
garbage, and the parser reads only documented extents.

**Keying.** An entry describes the position *resulting* from a candidate
play. For a book hit XG copies the entry's seven floats into the `.xg`
analysis pane verbatim (bit-identical), so the pane's `PositionsPlayed[i]` +
score context is exactly the lookup key. `OpeningBookKey`'s class doc states
the frames, and its factories own the normalization (see Pitfalls). Jacoby
keys money entries, Crawford keys match entries, Beaver is entry data only.

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

### The iterator's parameters

`Iterate` and `IterateDiagramRequests` take three optional parameters, one
concern each: `XgIteratorState` observes, `XgIteratorCallbacks` controls
iteration, `XgIteratorOptions` configures how rows are built. Null means the
defaults, and each type's doc states its members.

* `XgIteratorState` is a pure read-only observer of per-row context: `MatchInfo`
  is populated at the file boundary and `GameInfo` at each game header. It
  carries no caller-mutable surface and takes no part in iteration control.
* `XgIteratorCallbacks` holds four predicates: `SkipMatchAt` and `SkipGameAt`
  at the headers, before any row yields; `StopGameAfter` and `StopMatchAfter`
  after a decision has yielded.
* `XgIteratorOptions` holds the `OpeningBook` (see "Book enrichment") and the
  `Ranking` (see "The ranking"); an undefined ranking is refused when the
  options are made.

**"Yielded" means yielded to the caller** (halheinrich/backgammon#392). The
stop predicates judge each decision the iterator hands its caller, on the
caller's next `MoveNext`; for an `.xgp` that is after the single-decision
selection, so a stop vote cannot change the selection. A caller that stops
after its first result triggers no callback. In `IterateCore` the stop
callbacks are the last stage — walk, then the `.xgp` selection, then the
callbacks — and a game stop reaches back into the walk through the
per-enumeration `GameStopRequest`, so the rest of the game is skipped before
any of it is built and `.xg` iteration still streams.

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
state no comment: null. `RtfPlainText` owns the conversion itself, and its
XML doc is the one statement of the contract.

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
  corpus (see "Measured against XG"). Measured 2026-10-10 over 412 `.xg`
  files (the 391 of `TestData/xg` and the 21 of `FixtureFiles`, all read):
  in all 46,064 pairs of consecutive move records within a game, the first
  record's `FinalPosition` is the second's `InitialPosition` turned to the
  first mover's frame (43 of the pairs at a position that reads the same
  either way) — after analysed plays, unanalysed plays, dances and
  illegal-play markers alike; and every dance's sentinel candidate stores
  its unchanged position in the mover's frame (4,704 of 4,704), as does
  its `FinalPosition` save 2 player-1 dances whose `FinalPosition` is in
  neither frame. `XgGameBuilder` writes the same frames.

### Cube decisions

A cube decision is exactly **one** record — one `CubeDecision`, and so one
row — carrying the doubler's board (no flip); there is no second
taker-perspective record, so a consumer may count one row per decision. Its
stored equities and probabilities are the doubler's, verbatim. XG's errors of
the stated actions (`cube.ErrorCube`, `cube.ErrorTake`) are not stored:
BgDataTypes_Lib's scoring policy derives the error of a stated action from the
equities, and `XgCorpusAgreementTests` holds the two to XG's own. Which cube
panes are decisions at all is decided upstream of the builder; see "What is
not a record".

Each record also stamps the **played** cube action, mapped from the raw
`CubeRecord.Doubled` / `Taken` pane state. `Doubled` is a pane-state field,
not a flag — only `1` (doubled) and `0` (no double) record a played action:

| `Doubled` | `Taken`    | Doubler    | Taker  |
|-----------|------------|------------|--------|
| `1`       | `1` / `2`  | `Double`   | `Take` |
| `1`       | `0`        | `Double`   | `Pass` |
| `1`       | `-1`       | `Double`   | *null* |
| `0`       | (any)      | `NoDouble` | *null* |
| `-1`/`-2` | (any)      | *null*     | *null* |

`-2` is the incidental cube pane beside a checker play; `-1` is the pane XG
writes where a game ended with no cube action taken — every analysed `-1`
record in the corpus of the time (e60f9d0, 2026-07-28) was the last record of
its game, followed by a footer whose `Termination` is ≥ 100 (by resignation),
and it is also what `XgpExporter` writes for a curated `.xgp` cube problem.
Both map to *null* — "played action not recorded" — rather than being
flattened into `NoDouble`.

The mapping is keyed off the pane state alone, **not** off `ErrorCube`'s
`-999` sentinel: the played action is a game fact, the error an analysis
fact. They coincide in the corpus only because a record with no action has
nothing to score. `Taken == 2` (beaver) maps to `Take` — the taker half
models the accept-or-decline axis and a beaver accepts; no beaver appears
in the corpus, so that arm is reasoned rather than fixture-pinned.

Cross-half consistency (a recorded taker response implies the doubler
doubled) is enforced by gating the taker half on the doubler half
(`UserTakerActionOf`); `RecordedCubeActions` refuses a record that breaks it,
and `XgDecisionIteratorCubeActionTests` pins it corpus-wide.

## Pitfalls

### Reading a source

* **A failed source is an outcome, so a catch around `XgSourceReader` is
  wrong.** What still throws out of a read is a usage error or a
  cancellation, and both must reach the caller: catching them turns a
  caller's bug into a skipped file and a cancelled run into a completed
  one. The read replaces exactly that pattern — a walk catching every
  exception from a source's read, cancellation included. And keep the
  policy in `FailsTheSource`: a second filter in one entry point is a
  second policy.
* **An acquisition's own cancellation ends the whole operation.** A
  `ReadAsync` acquisition that times out by throwing
  `OperationCanceledException` stops the caller's walk, by the ruling that
  a cancellation stays a cancellation. An acquisition that means "this one
  source timed out" throws something else (`TimeoutException`), which fails
  the source alone.
* **No `await using` on the acquired stream.** `await using` is a
  `finally`, and an exception thrown in a `finally` replaces the one
  propagating: a stream that cancelled its own read and then failed to
  close turned the cancellation into a failed source, and one whose read
  failed and whose close failed too reported the close's exception as the
  cause (both found in review of the read operation,
  halheinrich/backgammon#374). `BufferAsync` disposes in a `finally` whose
  filter is `ReadExceptionStands`, the precedence stated once;
  `TheReadsExceptionAndTheDisposals_ResolveByOnePrecedence` pins every
  combination of the read's result and the disposal's.
* **Iteration throws on a file with no match header, and `XgSourceReader`
  does not cover it.** `Iterate` and `IterateDiagramRequests` throw
  `InvalidDataException` when a file's records do not begin with a match
  header, deferred to the first `MoveNext`, rather than process it with default
  player names and a zero-length match. `MatchContext`'s constructor runs first
  and requires the header at index 0, so it is what throws; the throw for a
  null `ExtractMatchInfo` behind it, which finds a header at any position,
  cannot then be reached. A consumer iterating directories of unknown files
  must catch this if it wants log-and-skip semantics: `XgSourceReader` covers
  reading a file, not iterating it.

### The container and the record bytes

* **The RichGameHeader is packed; the container manifest + end-record are
  load-bearing, and a round trip cannot vouch for them.**
  `ThumbnailOffset` (an Int64 at offset 12) must be written raw — an aligned
  8-byte write would insert padding and corrupt the header. The reader
  reads every container through its manifest and refuses one it cannot
  use, but writer and reader share `XgContainerLayout`, so a reader-level
  round trip passes with any layout mistake the two share. The gap once
  shipped in another form: while the reader still degraded to record-size
  heuristics, the writer omitted the end-record, round-trip tests stayed
  green, and real XG rejected every file (it seeks from EOF through the
  trailer to locate the manifest). So `XgFileWriterTests` asserts manifest
  sizes / offsets / CRC32s / storage bytes **and** the end-record's fields
  against the raw written bytes, through a manifest decoder and stream
  splitter of its own, plus `XgCorpus_EndRecord_*` pins the trailer
  decoding against XG-authored files — keep these independent of the
  production layout when refactoring the container writer. The one smoke
  the suite cannot run: open a freshly written `.xgp` in real XG.
* **An empty inner file is stored, never compressed.** .NET's `ZLibStream`
  writes no bytes at all for an empty input, so an empty inner file
  "compressed" leaves a zero-byte extent the reader refuses as not a zlib
  stream. `XgContainerWriter` stores it with no body bytes, as XG's own
  zero-length entries are stored (halheinrich/backgammon#357;
  `AnEmptyInnerFile_IsWrittenAsAStoredZeroLengthEntry_AndReadsBack` pins
  the written entry through the test class's own manifest decoder).
* **A container is read through its directory or refused — never guessed
  at** (halheinrich/backgammon#343; see "Reading — refuse, never guess"). The
  reader used to fall back to record-size heuristics whenever the end-record
  or manifest failed validation, and the fallback put the manifest stream in
  the comment slot, which shipped twice: for every commentless XG-authored
  file, and for every file holding a stored entry, where BgQuiz's Notes showed
  "temp.xg … temp.xgr". Files written then still parse with that entry, which
  is correct for what their bytes say. `Decompress_CorruptTrailer_IsRefused`
  and the refusal controls in `XgContainerDirectoryTests` pin the refusal, and
  `XgCorpus_NoParsedCommentIsManifestShaped` stays as the corpus guard. Two
  traps for whoever touches the inflation. .NET's `ZLibStream` reads a
  truncated stream without complaint and never says where a stream ended,
  and `DeflateStream.CopyTo` pumps its whole source through the inflater —
  so `XgDecompressor` learns whether a stream fills its extent exactly from
  its extent feed, driven by `Read`, never `CopyTo`. And a zlib header
  asking for a preset dictionary raises `ZLibException`, an `IOException`
  outside the public surface, which the reader maps to its own
  `InvalidDataException`.
* **The TSaveRec byte layout is encoded twice — parser and writer.** A
  deliberate serializer duality: `Parsing/SaveRecordParser` and
  `Writing/SaveRecordWriter` (likewise the rollout, comment, and outer-header
  pairs) must change together, field-for-field and alignment-for-alignment.
  The guard is `SaveRecordWriterTests` (distinct value in every field, so a
  transposition cannot cancel) plus the corpus round-trip in
  `XgFileWriterTests`. Do not "single-source" this with a declarative field
  map — evaluated and rejected as over-engineering for Pascal variant
  records.
* **`TDateTime` is a double of days — only quantized dates round-trip
  tick-for-tick.** Dates parsed from real files are already double-quantized
  and round-trip exactly; a synthetic `DateTime` in a writer test must use a
  binary-exact day fraction (midnight, noon, 18:00) or the re-read value can
  differ by a tick or two.
* **`.xgp` files encode "no analysis" by sentinel, and it is easy to
  regress.** `MoveError` and `ErrorCube` hold `-1000` for "unanalyzed", and
  anything `> -999.0` is a real error. Using `!= 0` or `.HasValue` checks on
  raw fields will silently treat unanalyzed positions as zero-error. Whether a
  pane is analysed is not read off an error at all: a cube's gate reads the
  pane's `Level`, a move's the pane's move count and evaluations.
* **Backgammon Galaxy money games are detected and repaired at parse
  time.** Galaxy exports money games by abusing `MatchLength` as a
  cube-size limit (a real, even value) and setting an illegal Crawford
  flag, rather than writing XG's `99999` money sentinel.
  `SaveRecordParser.IsGalaxyMoneyGame` detects them — ANSI location
  `BackgammonGalaxy` (ordinal, trimmed), even `MatchLength`, `Crawford`
  set — and the match-header parser then rewrites `MatchLength` to
  `99999` and sets `IsMoneyMatch = true` on the `MatchHeaderRecord`. Past the
  parser a Galaxy money game is indistinguishable from a native XG money game.
  One consequence for consumers: `MatchHeaderRecord.IsMoneyMatch` is *not* the
  raw XG byte — it is that byte OR'd with Galaxy detection.

### Comments

* **An interior empty comment line is a real (empty) comment.**
  `temp.xgc` is CRLF-terminated lines joined by `CommentIndex`;
  `CommentWriter` writes an empty comment as a bare CRLF, so
  `CommentParser` may drop only the one empty segment after the final
  CRLF (a split artifact). Skipping interior empties shifts every later
  entry and silently desyncs all subsequent comment joins — it once
  shipped that way.
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
  whole-file copy preserves them, slice drops them (only *referenced* comments
  are carried); both are correct. The footer-record hypothesis (an undecoded
  comment-index field on `GameFooterRecord` / `MatchFooterRecord`) was
  **refuted** by a byte-level sweep: footer records contain no −1-defaulted
  dwords at any common offset, so there is no hidden comment-index field there.

### Iteration

* **Two frames in XG's files, one in the record.** A record's board is
  on-roll-relative, but XG stores a decision's starting position in player 1's
  frame and each resulting position in the *mover's* (see "Board format").
  `OnRollBoard` turns only the starting position; a derived after-board is
  compared with XG's stored one turned (`BoardPosition.Flipped`), for either
  seat. The XGID is BgDataTypes_Lib's derivation, not a frame this library
  handles.
* **A fixture's cube-record count is not its cube-decision count.** XG writes
  and analyses cube panes in the Crawford game like any other, and neither
  surface emits them (see "What is not a record").
* **Cube `IsAnalysed` gates on `Analysis.Level > 0`: not on `LevelRequest`,
  and not `>= 0`.** The model and the census are "Level semantics". Widening
  the gate with `LevelRequest` (`||` between the two) buys nothing — every
  analysed pane in the corpus carries a positive request too — and re-admits
  the queued-never-ran phantom (`Level == -100` with a non-zero request;
  observed once, in `FixtureFiles/Opening 32 65 64 31 65.xgp`). And the `> 0`
  is deliberate, not an off-by-one. Code `0` is a legitimate level in XG's code
  space (`XgDepthFacts.OfLevel`: 1-ply), so `>= 0` looks like the more correct
  spelling; it is not. Ruled 2026-08-28 (halheinrich/backgammon#132): XG never
  runs a 1-ply cube analysis, so on the cube side a zero `Level` is the
  never-written pane, and `>= 0` would admit every one of them as an empty cube
  row. Note the asymmetry with the checker side, where 1-ply *is* a real
  analysis level that files do carry — which is why `IsAnalysed(MoveRecord)`
  gates structurally (`MoveCount` / `Evals`) instead of on a level at all.
* **`ReadCheckerPlay` returns `null` for every move record that is not a
  record** — a position that is not a decision, a sentinel, no roll, a
  corrupt candidate (see "What is not a record"); the dispatch emits only a
  non-null result. Keep each rule there, ahead of `BuildCheckerPlay`, so
  the two surfaces cannot disagree about which plays become records.
* **Sentinel-only analyses are filtered at the iterator boundary, not in
  the leaves.** XG emits two known patterns where a candidate slot of
  `BestMoveAnalysis.Moves`, within its `MoveCount`, holds a non-play
  sentinel: a `from` of `-100` is XG's *illegal-play workaround* (the
  recorded play in the source file is illegal, XG forces the next position
  rather than refusing to load), and `(0, 0)` is XG's *no-legal-move*
  (dance) encoding. Neither is of interest downstream, and feeding either to
  leaf computation has historically produced an `IndexOutOfRangeException` or
  a "1/1" notation glitch. Both surfaces gate emission in `ReadCheckerPlay`
  (`ClassifySentinelAnalysis`), so the translator never sees a sentinel. Do not
  add a sentinel branch to the translator: sentinel semantics belong with the
  iterator that decides what to emit, not with the leaf that operates on the
  move encoding.
* **`StopGameAfter` / `StopMatchAfter` fire *after* the yield.** The
  consumer sees the just-yielded row, *then* the predicate runs on the
  producer's next `MoveNext`. To suppress a row entirely, return `true` from
  `SkipGameAt` / `SkipMatchAt` at the boundary instead; the post-yield
  predicates exist for "I've seen enough" early-exit, not pre-filtering. Keep
  them the last stage of `IterateCore`: running the stop callbacks inside the
  walk again would let a stop vote on an `.xgp`'s cube end the walk before the
  play is reached (halheinrich/backgammon#392).
* **Null `sourceFile` is rejected eagerly, not deferred.** `Iterate`
  and `IterateDiagramRequests` throw `InvalidOperationException`
  synchronously at the call site, before any deferred enumeration begins
  (the LINQ-style two-method pattern), because every yielded row carries a
  `DecisionId` stamped from `sourceFile`. The public parameter remains typed
  `string?` so a method group fits a `Func<XgFile, string?, …>` slot, as
  XgFilter_Lib's `FilteredDecisionIteratorIdTests` holds it, and so tests can
  pass null; the runtime contract is strictly non-null. An unsupported
  extension (anything other than `.xg`, `.xgp`, or `.json`) throws
  `InvalidOperationException` from `BuildDecisionId`, deferred: it fires when
  a decision reaches one of the two record builders, not at the API boundary.

### Synthesis and export

* **The record model is internal by design; the builder is the only
  synthesis path.** Do not re-publicize a record type to unblock a caller —
  the caller's need is a missing intent on `XgFileBuilder` (or, for tests
  of this repo only, `InternalsVisibleTo` already covers it).
  `PublicSurfaceTests` fails on any drift back to `public`. Regenerate the
  goldens `JsonContractTests` pins only for a deliberate, documented contract
  change, and re-tie them to the binary fixtures
  (`Golden_ParsesToTheSameModel_AsTheBinaryFixture`) when you do.
* **`XgRecordFactory` is the synthesized-record SSOT.** The builder and
  `XgpExporter`'s clean path must keep drawing header defaults, sentinel
  panes, and the cube encoding from it; a field added to a synthesized
  record shape belongs there, not in either caller.
* **Clean-path exports yield zero iterator decisions — by design.**
  An unanalysed pane is not a record, which makes them invisible to
  `Iterate` / `IterateDiagramRequests`; the ecosystem's re-ingestible format
  for analysis-less decisions remains `BgDecisionData` JSON. The zero-rows
  assertions in `XgpExporterTests` pin that XG-import-only boundary — do
  not "fix" them to expect one row. Callers that want iterator-visible
  analyzed exports use the **slice path**.
* **`CopyMatchHeader` is a full manual copy of `MatchHeaderRecord`.** The
  exporter's single header-copy helper copies every header field explicitly
  (the model is a class, not a record — no `with`). A new
  `MatchHeaderRecord` field must be added to the copy too, or slice and
  anonymize-copy exports silently drop it. The guard is the byte-identity
  pair in `XgpSliceExportTests`
  (`SliceOptions_MatchingSourceNames_AreByteInvisible` and
  `Copy_WithSourceNames_IsByteIdenticalToXgFileWriterOutput`): overrides
  equal to the source names must produce a byte-identical file, so any
  dropped or transposed field fails them.
* **Role-based names resolve to slots *before* `CopyMatchHeader`.**
  `ResolveNameOverrides` turns the role pair into the slot pair the header
  copy takes; `CopyMatchHeader` must stay a dumb slot mechanism — do not teach
  it roles. A whole-`.xg` copy deliberately **ignores** role names when a slot
  fallback exists (user spec — roles are per-move there, undefined file-wide —
  not an omission); role names with **no** slot fallback against a
  roles-undeterminable source throw `NotSupportedException` rather than
  silently guess a slot.
* **A cube rollout is an adjacent context pair; the record points at the
  second leg.** Ground truth from XG's own save (`match35253054_2_37.xgp`:
  two contexts, `RolloutIndex = 1`). Anything that copies or filters
  rollout tables for cube decisions must carry both legs and keep them
  adjacent and in order — carrying only `rollouts[RolloutIndex]` silently
  drops the companion leg. The slice exporter's `RemapCubeRollout` is the
  in-tree reference; move-candidate rollouts are individually indexed and
  have no pair rule.

### The JSON document

* **The `$type` discriminator has one home.** `SaveRecordConverter`'s
  table maps every `RecordType` member to its class, and the strings on
  the wire are the members' names. Adding a record variant means one table
  row; a variant missing from it fails at write (unnamed or mismatched
  tag) and at read (unknown discriminator), never silently. Do not reach
  for `[JsonPolymorphic]`: `UnknownRecord` carries two tags, and the
  built-in mechanism binds one discriminator per derived type
  (halheinrich/backgammon#177).

### The opening book

* **Two opening books, and the level codes read "backwards": 999 = Book V1,
  998 = Book V2.** XG 1's `OpeningBook.db` (V1) stamps level 999; XG 2's
  `OpeningBookV2.ob` (V2) stamps 998 — the *lower* code is the *newer*
  book. The depth decoding once had the labels reversed; XG's PLAYERLEVEL
  table (`docs/xg-format/xg_format.pas`) is the ground truth. Only the V2
  database is parsed (`OpeningBook`); V1 is deliberately unsupported.
* **The opening-book key is doubly perspective-normalized — let
  `OpeningBookKey` do it.** A book entry keys on the position *resulting*
  from the candidate play, seen by the player on roll after it, with the
  away pair stored as (new-on-roll away, mover away) in that frame — while
  XG stores the resulting position in the *mover's* frame, for either
  seat, and the decision's context is mover-framed. Hand-building the key
  invites both a missed flip and a swapped away pair; the `ForMatchPlay` /
  `ForMoneyPlay` factories encapsulate exactly these two traps. The flip is
  the same whoever moved, so the factories take no seat: a seat parameter
  is how the key once flipped player 1's position alone and missed nearly
  every player-2 hit, while the builder wrote player 2's results in player
  1's frame, which let synthesized tests agree with it
  (halheinrich/backgammon#290; `Iterator_EnrichesABookHit_ForEitherMover`
  pins both seats against boards stated by hand). The eval vector, by
  contrast, is from the *mover's* perspective. Measured 2026-10-10 over the
  local corpus's centred-cube V2-book-stamped candidates (391 `.xg`, 193
  `.xgp`): player-1 movers hit 3,856 of 3,860 before the fix and after it,
  3,748 with an entry's evaluation bit-identical to the pane's; player-2
  movers hit 216 of 3,691 before (6 bit-identical) and 3,688 after (3,634).
  Dated evidence, not pins.
* **A book entry's equity slot is cubeful and score-contexted.** The same
  resulting position stores wildly different equities under different away
  scores (+0.377 at (2,4)-away vs −0.38 at (4,2)-away vs +0.01 at (9,9)-away).
  Don't compare equities across score contexts, and don't read the slot as
  cubeless; `OpeningBookEntry.Evaluation`'s doc says how XG's tooltip derives
  its cubeless number.
* **Book enrichment changes depth facts, never emission — and a 998
  stamp is not always rollout-backed.** With and without a book, the same
  decisions and candidates are emitted. On `OpeningBookRealDbTests`' fixture
  (a), two of the five 998-stamped candidates resolve to the book's **Roller++ evaluation baseline**
  entries (Level 1002, zero trials): XG evidently stamps 998 when the book
  supplied the pane numbers, rollout or not. Those hits deliberately state the
  bare book facts, as there is no cached rollout to recover. Do not "fix" that
  degradation, and never read `RolloutMovesLevel` / `Trials` off an entry
  without gating on `IsRollout` (evaluation entries store zeros there — a zero
  moves level would decode as a bogus "1-ply").
* **Cube rows never book-enrich — the keying is unproven, so they degrade
  rather than guess.** Only the checker-play keying is pinned; the
  turned-cube owner-sign convention is unknown and the key factories
  cover centred-cube contexts only. The fixture corpus held no oracle when
  this was built (438 files, 23,736 cube records, 9067925, the command not
  kept): **zero** cube analyses carried a book code (998/999) in `Level` or
  `LevelRequest`, against 3,817 book-stamped checker-play candidates. So a
  book-stamped cube resolves to its bare book facts by design (`BuildCube`
  passes no entry). If a book-stamped cube decision ever surfaces, pin the key
  against it before wiring cube enrichment — `XgDepthFacts.Resolve` would also
  need to select `RolloutCubeLevel` rather than `RolloutMovesLevel` for that
  path.
* **Book selection: deeper rollout levels beat more games — by this
  library's policy, which is not XG's display rule.** One key commonly
  holds several entries (5,113 keys with more than one rollout, 2,099 of
  them at differing moves levels, recorded in c2e7a07 with the
  halheinrich/backgammon#203 work, the command not kept). XG's tooltip agrees
  on one observed key and disagrees on another, so cite both cases from
  `OpeningBook`'s class doc rather than claiming parity anywhere. Two
  residuals are documented there and on `OpeningBookKey`: moves level is
  compared before cube level, a choice the shipped DB offers no discriminating
  case for; and the cube *owner sign* convention (22 turned-cube entries, no
  tooltip oracle) is unverified, which is why the public key factories cover
  centred-cube contexts only.

### Test setup

* **`TestPaths._root` depends on a specific build output depth.** If
  `AppContext.BaseDirectory` moves relative to the repo root (e.g. a csproj
  layout change), the five-`..` walk breaks and every file-touching test
  fails. Fix by adjusting `TestPaths`, not by moving `TestData`.
