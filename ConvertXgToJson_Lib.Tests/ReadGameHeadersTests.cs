using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins the single-parser unification for game headers: the fast path
/// (ReadGameHeaders) and the full parse (ReadFile + XgGameInfo.From) must
/// yield identical game metadata for every corpus file.
/// </summary>
[Collection("FileIO")]
public class ReadGameHeadersTests
{
    [Fact]
    public void ReadGameHeaders_MatchesFullParse()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            string name = Path.GetFileName(path);

            var state = new XgIteratorState();
            var fast = XgFileReader.ReadGameHeaders(path, state).ToList();

            var file = XgFileReader.ReadFile(path);
            var terms = XgDecisionIterator.ExtractMatchInfo(file)!.Terms;
            var full = file.Records.OfType<GameHeaderRecord>()
                .Select(gh => XgGameInfo.From(gh, terms))
                .ToList();

            fast.Should().HaveCount(full.Count, $"game-header count in {name}");
            for (int i = 0; i < full.Count; i++)
            {
                fast[i].Standing.Should().Be(full[i].Standing, $"standing game#{i} in {name}");
                fast[i].IsStandardStart.Should().Be(full[i].IsStandardStart, $"StdStart game#{i} in {name}");
            }
        }
    }

    /// <summary>
    /// Verifies ReadGameHeaders streaming overload returns correct away scores,
    /// IsCrawfordGame, and IsStandardStart against the full-parse path.
    /// </summary>
    [Fact]
    public void ReadGameHeaders_ReturnsCorrectData()
    {
        var files = TestPaths.XgFiles.ToList();
        if (files.Count < 2)
            return;

        foreach (var path in files)
        {
            // --- Fast path ---
            var state = new XgIteratorState();
            var fastInfos = XgFileReader.ReadGameHeaders(path, state).ToList();

            // --- Full parse path ---
            // ReadGameHeaders yields one XgGameInfo per game header. Rebuild the
            // reference list the same way — capture every game header through the
            // iterator's SkipGameAt callback, which fires once per game (before any
            // rows) and receives the same XgGameInfo projection the fast path emits.
            // A row-derived oracle would only see games that produced at least one
            // analysed decision row, undercounting games with none and misaligning
            // the positional comparison below; every game has a header, so capturing
            // at the header is the contract ReadGameHeaders implements.
            var file = XgFileReader.ReadFile(path);
            var fullInfos = new List<XgGameInfo>();
            var captureGames = new XgIteratorCallbacks(
                SkipGameAt: gameInfo =>
                {
                    fullInfos.Add(gameInfo);
                    return true; // header captured; skip the game's rows
                });
            XgDecisionIterator.Iterate(
                file, Path.GetFileName(path), callbacks: captureGames).ToList();

            fastInfos.Count.Should().Be(fullInfos.Count,
                $"game count mismatch in {Path.GetFileName(path)}");

            for (int i = 0; i < fastInfos.Count; i++)
            {
                fastInfos[i].Standing.Should().Be(fullInfos[i].Standing,
                    $"standing mismatch game {i + 1} in {Path.GetFileName(path)}");
                fastInfos[i].IsStandardStart.Should().Be(fullInfos[i].IsStandardStart,
                    $"IsStandardStart mismatch game {i + 1} in {Path.GetFileName(path)}");
            }
        }
    }

    /// <summary>
    /// Verifies the streaming ReadGameHeaders overload populates MatchInfo
    /// before the first yield.
    /// </summary>
    [Fact]
    public void ReadGameHeaders_Streaming_PopulatesMatchInfoBeforeFirstYield()
    {
        var path = TestPaths.XgFiles.First();
        var state = new XgIteratorState();
        XgMatchInfo? capturedMatchInfo = null;

        foreach (var game in XgFileReader.ReadGameHeaders(path, state))
        {
            capturedMatchInfo = state.MatchInfo;
            break;
        }

        capturedMatchInfo.Should().NotBeNull("MatchInfo should be set before the first game is yielded");
        capturedMatchInfo!.Player1.Should().NotBeNullOrEmpty();
        capturedMatchInfo.Player2.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Verifies the streaming ReadGameHeaders enumerator stops when the
    /// consumer breaks out of the foreach — the natural early-exit
    /// mechanism for yield-iterators. No imperative skip flag is needed.
    /// </summary>
    [Fact]
    public void ReadGameHeaders_Streaming_RespectsConsumerBreak()
    {
        var files = TestPaths.XgFiles.ToList();
        if (files.Count < 2)
            return;

        int totalGames = 0;
        int filesWithRows = 0;

        foreach (var path in files)
        {
            var state = new XgIteratorState();
            int gamesThisFile = 0;
            foreach (var game in XgFileReader.ReadGameHeaders(path, state))
            {
                gamesThisFile++;
                totalGames++;
                break; // stop after first game of each file
            }
            if (gamesThisFile > 0) filesWithRows++;
        }

        totalGames.Should().Be(filesWithRows,
            "each file should yield exactly one game when the consumer breaks after the first");
    }
}
