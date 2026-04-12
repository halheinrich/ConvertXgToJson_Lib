# ConvertXgToJson_Lib — Project Instructions

Part of the Backgammon tools ecosystem: https://github.com/halheinrich/backgammon

## Repo

https://github.com/halheinrich/ConvertXgToJson_Lib
**Branch:** main

## Stack

C# / .NET 10 / Class Library / Visual Studio 2026 / Windows

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\ConvertXgToJson_Lib\ConvertXgToJson_Lib.slnx`

## Purpose

Reads .xg and .xgp files; produces DecisionRow and BgDecisionData records.

## Depends on

* **BgDataTypes_Lib** — DecisionRow, BgDecisionData, PositionData, DecisionData, DescriptiveData, PlayCandidate, AnalysisDepthEntry, CubeOwner

## Dependency files

### BgDataTypes_Lib
* BgDataTypes_Lib/BgDecisionData.cs
* BgDataTypes_Lib/PositionData.cs
* BgDataTypes_Lib/DecisionData.cs
* BgDataTypes_Lib/DescriptiveData.cs
* BgDataTypes_Lib/DecisionRow.cs
* BgDataTypes_Lib/PlayCandidate.cs
* BgDataTypes_Lib/AnalysisDepthEntry.cs
* BgDataTypes_Lib/CubeOwner.cs

## Directory tree

```
ConvertXgToJson_Lib/
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
  ConvertXgToJson_Lib.slnx
```

## Architecture

### XgDecisionIterator

* `Iterate` yields `DecisionRow` records
* `IterateDiagramRequests` yields `BgDecisionData` records (one per decision; cube decisions yield one, not two)
* `ToBoard` — converts position to board array, player-on-roll perspective
* `FlipPosition` — flips PositionEngine for XGID encoding (bottom-player perspective)
* `ExtractMatchInfo` — public helper; scans for MatchHeaderRecord, returns XgMatchInfo
* `CubeValueActual` — internal static helper, called from MatchContext
* `BuildMoveDiagramRequest` returns null if dice == 0

### XgFileReader

* `ReadFile` — full parse of .xg file
* `ReadMatchInfo` — fast path; first zlib stream, MatchHeaderRecord only
* `ReadGameHeaders` — fast path; first zlib stream, GameHeaderRecord entries only

### XgIteratorState

* `AdvanceNextGame` / `AdvanceNextMatch` — caller-set flags for early-exit
* `MatchInfo` / `GameInfo` — populated by iterator before first row
* Flags reset at file boundaries

### MatchContext

* Internal class tracking match/game state during iteration
* `MatchScoreFor(int activePlayer)` — perspective-correct match score

### BackgammonConstants

* `StandardOpeningPosition` — internal static readonly sbyte[26]
* `IsStandardOpeningPosition` — comparison helper

### TestData

* Shared at `backgammon\TestData`; `TestPaths._root` resolves via 5 × `..` from `AppContext.BaseDirectory`
* All file-touching tests use `[Collection("FileIO")]`

## Current status

✅ Complete — all 184 tests pass

## Deferred

* 0-rows bug in ExtractFromXgToCsv after XGID fix — to be diagnosed from that project
* `SyncJsonDir` — sync XG to JSON cache by timestamp; under consideration

## Key decisions

* Board encoding is player-on-roll perspective throughout
* XGID is always bottom-player perspective
* Taker cube row board is always doubler's POV — FlipBoard removed
* Taker DecisionRow uses MatchScoreFor(cube.ActivePlayer)
* IterateDiagramRequests yields one BgDecisionData per decision
* Dependency on BackgammonDiagram_Lib replaced with BgDataTypes_Lib
* `.xgp` files: MoveError and ErrorCube are -1000 (sentinel); `IsAnalysed` gates on analysis level; `Error` uses `> -999.0`
* UserPlayError/UserDoubleError/UserTakeError populated from sentinel-guarded raw XG fields
* PlayCandidate win/gammon/bg probabilities populated from EvalResult