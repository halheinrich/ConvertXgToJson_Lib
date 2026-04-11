# ConvertXgToJson_Lib — Project Instructions

Part of the Backgammon tools ecosystem: https://github.com/halheinrich/backgammon
**After committing here, return to the Backgammon Umbrella project to update hashes and instructions doc.**

## Repo

https://github.com/halheinrich/ConvertXgToJson_Lib
**Branch:** main
**Current commit:** `d61da8b`

## Stack

C# / .NET 10 / Class Library / Visual Studio 2026 / Windows

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\ConvertXgToJson_Lib\ConvertXgToJson_Lib.slnx`

## Purpose

Reads .xg and .xgp files; produces DecisionRow records consumed by XgFilter_Lib and ExtractFromXgToCsv.

## Repo directory tree
ConvertXgToJson_Lib/
ConvertXgToJson_Lib/
Json/
XgJsonOptions.cs
Models/
DecisionRow.cs
Models.cs
Parsing/
CommentParser.cs
PascalBinaryReader.cs
RichGameHeaderParser.cs
RolloutContextParser.cs
SaveRecordParser.cs
XgDecompressor.cs
BackgammonConstants.cs
ConvertXgToJson_Lib.csproj
MatchContext.cs
XgDecisionIterator.cs
XgFileReader.cs
XgGameInfo.cs
XgidEncoder.cs
XgIteratorState.cs
XgMatchInfo.cs
ConvertXgToJson_Lib.Tests/
BoardTests.cs
DecisionCsvTests.cs
DiagramRequestIteratorTests.cs
FileIOCollection.cs
GlobalUsings.cs
ReadMatchInfoBenchmarkTests.cs
RealFileTests.cs
TestPaths.cs
XgDecisionIteratorTests.cs
ConvertXgToJson_Lib.Tests.csproj
ConvertXgToJson_Lib.slnx
INSTRUCTIONS.md

## Key files

* ConvertXgToJson_Lib.csproj: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/ConvertXgToJson_Lib.csproj
* Models/DecisionRow.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/Models/DecisionRow.cs
* Models/Models.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/Models/Models.cs
* XgDecisionIterator.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/XgDecisionIterator.cs
* XgIteratorState.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/XgIteratorState.cs
* XgMatchInfo.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/XgMatchInfo.cs
* XgGameInfo.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/XgGameInfo.cs
* XgFileReader.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/XgFileReader.cs
* BackgammonConstants.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/BackgammonConstants.cs
* XgidEncoder.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/XgidEncoder.cs
* MatchContext.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/MatchContext.cs
* Json/XgJsonOptions.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/Json/XgJsonOptions.cs
* Parsing/SaveRecordParser.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/Parsing/SaveRecordParser.cs
* Parsing/PascalBinaryReader.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/Parsing/PascalBinaryReader.cs
* Parsing/RichGameHeaderParser.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/Parsing/RichGameHeaderParser.cs
* Parsing/RolloutContextParser.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/Parsing/RolloutContextParser.cs
* Parsing/XgDecompressor.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/Parsing/XgDecompressor.cs
* Parsing/CommentParser.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib/Parsing/CommentParser.cs
* Tests.csproj: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib.Tests/ConvertXgToJson_Lib.Tests.csproj
* Tests/GlobalUsings.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib.Tests/GlobalUsings.cs
* Tests/TestPaths.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib.Tests/TestPaths.cs
* Tests/BoardTests.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib.Tests/BoardTests.cs
* Tests/RealFileTests.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib.Tests/RealFileTests.cs
* Tests/DecisionCsvTests.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib.Tests/DecisionCsvTests.cs
* Tests/XgDecisionIteratorTests.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib.Tests/XgDecisionIteratorTests.cs
* Tests/FileIOCollection.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib.Tests/FileIOCollection.cs
* Tests/ReadMatchInfoBenchmarkTests.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib.Tests/ReadMatchInfoBenchmarkTests.cs
* Tests/DiagramRequestIteratorTests.cs: https://raw.githack.com/halheinrich/ConvertXgToJson_Lib/d61da8b/ConvertXgToJson_Lib.Tests/DiagramRequestIteratorTests.cs

## Dependency files

BgDataTypes_Lib @ b94f762:
- https://raw.githack.com/halheinrich/BgDataTypes_Lib/b94f762/BgDataTypes_Lib/BgDecisionData.cs
- https://raw.githack.com/halheinrich/BgDataTypes_Lib/b94f762/BgDataTypes_Lib/PositionData.cs
- https://raw.githack.com/halheinrich/BgDataTypes_Lib/b94f762/BgDataTypes_Lib/DecisionData.cs
- https://raw.githack.com/halheinrich/BgDataTypes_Lib/b94f762/BgDataTypes_Lib/DescriptiveData.cs
- https://raw.githack.com/halheinrich/BgDataTypes_Lib/b94f762/BgDataTypes_Lib/PlayCandidate.cs
- https://raw.githack.com/halheinrich/BgDataTypes_Lib/b94f762/BgDataTypes_Lib/AnalysisDepthEntry.cs
- https://raw.githack.com/halheinrich/BgDataTypes_Lib/b94f762/BgDataTypes_Lib/CubeOwner.cs

## Architecture

### DecisionRow.Board

* `int[]` 26 elements
* `board[0]` = opponent bar (never positive)
* `board[1-24]` = points 1-24 from player on roll's perspective
* `board[25]` = player bar (never negative)
* Positive = player on roll; negative = opponent
* Board is never exposed in CSV output

### XgDecisionIterator

* `ToBoard` — converts position to board array normalized to player-on-roll perspective
* `FlipPosition` — flips a PositionEngine from top-player to bottom-player perspective for XGID encoding
* `ExtractMatchInfo` — public helper; accepts XgFile; scans records for MatchHeaderRecord and returns XgMatchInfo
* `IsStandardOpeningPosition` — moved to BackgammonConstants
* `.xgp` files have no play record — `MoveError` and `ErrorCube` are -1000 (sentinel)
* `IsAnalysed` gates on analysis level only; `Error` field uses `> -999.0` to return 0.0 for sentinels
* `IterateDiagramRequests` yields one `BgDecisionData` per decision (cube decisions yield one, not two)
* `BuildMoveDiagramRequest` returns null if dice == 0 (consistent with DecisionRow.IsCube filter)

### BackgammonConstants

* `StandardOpeningPosition` — internal static readonly sbyte[26] defining the standard backgammon starting position
* `IsStandardOpeningPosition` — internal static helper; compares PositionEngine against StandardOpeningPosition

### XgFileReader

* `ReadFile` — fully parses a .xg file into XgFile
* `ReadMatchInfo` — fast path; decompresses only the first zlib stream and parses only the MatchHeaderRecord
* `ReadGameHeaders` — fast path; decompresses only the first zlib stream and scans only GameHeaderRecord entries

### XgIteratorState

* `AdvanceNextGame` — set by caller to skip remaining decisions in current game
* `AdvanceNextMatch` — set by caller to skip remaining decisions in current match
* `MatchInfo` — populated by iterator before first row of each file
* `GameInfo` — populated by iterator before first row of each game
* All flags reset at file boundaries

### XgMatchInfo / XgGameInfo

* `XgMatchInfo`: `Player1`, `Player2`, `MatchLength` from MatchHeaderRecord
* `XgGameInfo`: `Away1`, `Away2`, `IsCrawfordGame`, `IsStandardStart` from GameHeaderRecord

### MatchContext

* Extracted from `XgDecisionIterator` to its own file; `internal sealed class`
* Tracks match/game state as records are scanned: scores, cube value/position, game number, move number
* Shared by `XgDecisionIterator` and available for future iterators

### TestData

* Shared at solution root: `D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\TestData`
* `TestPaths._root` resolves via 5 x `..` from `AppContext.BaseDirectory`
* All file-touching test classes use `[Collection("FileIO")]`

## Current status

Complete. All tests pass (184).
IterateDiagramRequests populates UserPlayError, UserDoubleError, UserTakeError.
PlayCandidate populated with win/gammon/bg probabilities from EvalResult.
BgDecisionData_WriteSampleJson writes sample JSON to TestData/BgDecisionData/.

## Deferred

* `ExtractFromXgToCsv` gets 0 rows after XGID fix — to be diagnosed from that project
* `SyncJsonDir` — sync XG to JSON cache by timestamp; under consideration

## Key decisions

* Board encoding is player-on-roll perspective throughout
* XGID is always bottom-player perspective
* All file-touching test classes share `[Collection("FileIO")]`
* TestData lives at solution root (`backgammon\TestData`)
* Cube decisions use `MoveNumber + 1` in BuildCubeRows
* Taker cube row Board is always doubler's POV (same as doubler row) — FlipBoard removed
* IterateDiagramRequests yields one BgDecisionData per decision (cube decisions yield one, not two)
* CubeValueActual is internal static on XgDecisionIterator — called from MatchContext.cs
* Taker DecisionRow uses MatchScoreFor(cube.ActivePlayer) — same as doubler row (XGID perspective)
* MatchContext is a separate file; MatchScoreFor(int activePlayer) replaces the old MatchScore property
* Dependency on BackgammonDiagram_Lib replaced with BgDataTypes_Lib; IterateDiagramRequests returns IEnumerable<BgDecisionData>
* DiagramRequestIteratorTests accesses nested BgDecisionData fields (req.Position.X, req.Decision.X, req.Descriptive.X)

## Shared rules

See `AGENTS.md` in the umbrella repo — applies to all sub-projects.
get hash via `git log --oneline -3 -- AGENTS.md` from umbrella root, then:
      `https://raw.githack.com/halheinrich/backgammon/{hash}/AGENTS.md`

## Session handoff

After committing:

1. `git rev-parse HEAD` in this subproject dir — note the short hash
2. Update commit hash in this doc and all raw URLs
3. Add URLs for any new files created
4. Update In progress / Deferred / Key decisions
5. Return to Backgammon Umbrella project — update umbrella instructions doc