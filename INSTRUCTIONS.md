# ConvertXgToJson_Lib — Project Instructions

Part of the Backgammon tools ecosystem: https://github.com/halheinrich/backgammon
**After committing here, return to the Backgammon Umbrella project to update hashes and instructions doc.**

## Repo

https://github.com/halheinrich/ConvertXgToJson_Lib
**Branch:** main
**Current commit:** `f25850d`

## Stack

C# / .NET 10 / Class Library / Visual Studio 2026 / Windows

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\ConvertXgToJson_Lib\ConvertXgToJson_Lib.slnx`

## Purpose

Reads .xg and .xgp files; produces DecisionRow records consumed by XgFilter_Lib and ExtractFromXgToCsv.

## Key files (commit f25850d)

* Models.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/Models/Models.cs
* DecisionRow.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/Models/DecisionRow.cs
* XgDecisionIterator.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/XgDecisionIterator.cs
* XgIteratorState.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/XgIteratorState.cs
* XgMatchInfo.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/XgMatchInfo.cs
* XgGameInfo.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/XgGameInfo.cs
* XgFileReader.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/XgFileReader.cs
* BackgammonConstants.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/BackgammonConstants.cs
* XgidEncoder.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/XgidEncoder.cs
* XgJsonOptions.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/Json/XgJsonOptions.cs
* SaveRecordParser.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/Parsing/SaveRecordParser.cs
* PascalBinaryReader.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/Parsing/PascalBinaryReader.cs
* RichGameHeaderParser.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/Parsing/RichGameHeaderParser.cs
* RolloutContextParser.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/Parsing/RolloutContextParser.cs
* XgDecompressor.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/Parsing/XgDecompressor.cs
* CommentParser.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/Parsing/CommentParser.cs
* ConvertXgToJson_Lib.csproj: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib/ConvertXgToJson_Lib.csproj
* Tests.csproj: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib.Tests/ConvertXgToJson_Lib.Tests.csproj
* GlobalUsings.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib.Tests/GlobalUsings.cs
* TestPaths.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib.Tests/TestPaths.cs
* BoardTests.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib.Tests/BoardTests.cs
* RealFileTests.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib.Tests/RealFileTests.cs
* DecisionCsvTests.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib.Tests/DecisionCsvTests.cs
* XgDecisionIteratorTests.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib.Tests/XgDecisionIteratorTests.cs
* FileIOCollection.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib.Tests/FileIOCollection.cs
* ReadMatchInfoBenchmarkTests.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/f25850d/ConvertXgToJson_Lib.Tests/ReadMatchInfoBenchmarkTests.cs

## Architecture

### DecisionRow.Board

* `int[]` 26 elements
* `board[0]` = opponent bar (never positive)
* `board[1-24]` = points 1-24 from player on roll's perspective
* `board[25]` = player bar (never negative)
* Positive = player on roll; negative = opponent
* Board is never exposed in CSV output

### XgDecisionIterator

* `ToBoard` - converts position to board array normalized to player-on-roll perspective
* `FlipBoard` - flips board perspective (used for cube take/drop row)
* `FlipPosition` - flips a PositionEngine from top-player to bottom-player perspective for XGID encoding
* `ExtractMatchInfo` - public helper; accepts XgFile; scans records for MatchHeaderRecord and returns XgMatchInfo
* `IsStandardOpeningPosition` - moved to BackgammonConstants

### BackgammonConstants

* `StandardOpeningPosition` - internal static readonly sbyte[26] defining the standard backgammon starting position
* `IsStandardOpeningPosition` - internal static helper; compares PositionEngine against StandardOpeningPosition

### XgFileReader

* `ReadFile` - fully parses a .xg file into XgFile
* `ReadMatchInfo` - fast path; decompresses only the first zlib stream and parses only the MatchHeaderRecord
* `ReadGameHeaders` - fast path; decompresses only the first zlib stream and scans only GameHeaderRecord entries

### XgIteratorState

* `AdvanceNextGame` - set by caller to skip remaining decisions in current game
* `AdvanceNextMatch` - set by caller to skip remaining decisions in current match
* `MatchInfo` - populated by iterator before first row of each file
* `GameInfo` - populated by iterator before first row of each game
* All flags reset at file boundaries

### XgMatchInfo / XgGameInfo

* `XgMatchInfo`: `Player1`, `Player2`, `MatchLength` from MatchHeaderRecord
* `XgGameInfo`: `Away1`, `Away2`, `IsCrawfordGame`, `IsStandardStart` from GameHeaderRecord

### TestData

* Shared at solution root: `D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\TestData`
* `TestPaths._root` resolves via 5 x `..` from `AppContext.BaseDirectory`
* All file-touching test classes use `[Collection("FileIO")]`

## Current status

Complete. All tests pass.
Benchmark: ReadMatchInfo is 90x faster than ReadFile.
Benchmark: JSON iteration is 1.5x faster than XG iteration.

## Deferred

* `ExtractFromXgToCsv` gets 0 rows after XGID fix - to be diagnosed from that project.
* `SyncJsonDir` - sync XG to JSON cache by timestamp; under consideration.

## Key decisions

* Board encoding is player-on-roll perspective throughout
* FlipBoard kept for cube rows (responder perspective)
* XGID is always bottom-player perspective
* All file-touching test classes share `[Collection("FileIO")]`
* TestData lives at solution root (`backgammon\TestData`)
* Cube decisions use `MoveNumber + 1` in BuildCubeRows

## Shared rules

See `AGENTS.md` in the umbrella repo — applies to all sub-projects.
`https://raw.githubusercontent.com/halheinrich/backgammon/main/AGENTS.md`

## Session handoff

After committing:

1. `git rev-parse HEAD` in this subproject dir - note the short hash
2. Update commit hash in this doc and all raw URLs
3. Add URLs for any new files created
4. Update In progress / Deferred / Key decisions
5. Return to Backgammon Umbrella project - update umbrella instructions doc
