// ReadMatchInfoBenchmarkTests.cs
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using System.Diagnostics;
using Xunit.Abstractions;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Compares the throughput of ReadFile (full parse) vs ReadMatchInfo (fast path).
/// These are not pass/fail tests — they print timing to test output and assert
/// only that ReadMatchInfo is meaningfully faster than ReadFile.
/// </summary>
[Collection("FileIO")]
public class ReadMatchInfoBenchmarkTests(ITestOutputHelper output)
{
    private const int MinFiles = 2;
    private const double MinSpeedupFactor = 3.0; // ReadMatchInfo must be at least 3x faster

    [Fact]
    public void ReadMatchInfo_IsFasterThanReadFile()
    {
        var files = TestPaths.XgFiles.ToList();
        if (files.Count < MinFiles)
        {
            output.WriteLine($"Skipping — fewer than {MinFiles} .xg files available.");
            return;
        }

        // Warm up — avoid cold-start skewing the first measurement
        foreach (var path in files.Take(2))
        {
            XgFileReader.ReadFile(path);
            XgFileReader.ReadMatchInfo(path);
        }

        // --- Full parse ---
        var sw = Stopwatch.StartNew();
        int fullCount = 0;
        foreach (var path in files)
        {
            _ = XgFileReader.ReadFile(path);
            fullCount++;
        }
        sw.Stop();
        double fullMs = sw.Elapsed.TotalMilliseconds;
        double fullPerSec = fullCount / (fullMs / 1000.0);

        // --- Fast path ---
        sw.Restart();
        int fastCount = 0;
        foreach (var path in files)
        {
            _ = XgFileReader.ReadMatchInfo(path);
            fastCount++;
        }
        sw.Stop();
        double fastMs = sw.Elapsed.TotalMilliseconds;
        double fastPerSec = fastCount / (fastMs / 1000.0);

        double speedup = fastPerSec / fullPerSec;

        output.WriteLine($"Files: {files.Count}");
        output.WriteLine($"ReadFile:      {fullMs,8:F1} ms  ({fullPerSec,6:F1} files/sec)");
        output.WriteLine($"ReadMatchInfo: {fastMs,8:F1} ms  ({fastPerSec,6:F1} files/sec)");
        output.WriteLine($"Speedup:       {speedup:F1}x");

        speedup.Should().BeGreaterThan(MinSpeedupFactor,
            $"ReadMatchInfo should be at least {MinSpeedupFactor}x faster than ReadFile");
    }

    [Fact]
    public void ReadMatchInfo_ReturnsCorrectData()
    {
        var files = TestPaths.XgFiles.ToList();
        if (files.Count < MinFiles)
            return;

        foreach (var path in files)
        {
            var fast = XgFileReader.ReadMatchInfo(path);
            var full = XgDecisionIterator.ExtractMatchInfo(XgFileReader.ReadFile(path));

            fast.Should().NotBeNull(
                $"file should have a readable match header: {Path.GetFileName(path)}");
            full.Should().NotBeNull(
                $"full parse should produce a match header: {Path.GetFileName(path)}");

            fast!.Player1.Should().Be(full!.Player1,
                $"Player1 mismatch in {Path.GetFileName(path)}");
            fast!.Player2.Should().Be(full!.Player2,
                $"Player2 mismatch in {Path.GetFileName(path)}");
            fast!.MatchLength.Should().Be(full!.MatchLength,
                $"MatchLength mismatch in {Path.GetFileName(path)}");
        }
    }
    /// <summary>
    /// Compares throughput of full decision iteration vs. ReadGameHeaders streaming fast path.
    /// </summary>
    [Fact]
    public void ReadGameHeaders_IsFasterThanFullIteration()
    {
        var files = TestPaths.XgFiles.ToList();
        if (files.Count < MinFiles)
        {
            output.WriteLine($"Skipping — fewer than {MinFiles} .xg files available.");
            return;
        }

        // Warm up
        var warmState = new XgIteratorState();
        foreach (var path in files.Take(2))
        {
            XgFileReader.ReadGameHeaders(path, warmState).ToList();
            XgDecisionIterator.Iterate(XgFileReader.ReadFile(path),
                Path.GetFileName(path)).ToList();
        }

        // --- Full decision iteration ---
        var sw = Stopwatch.StartNew();
        int fullDecisions = 0;
        foreach (var path in files)
        {
            var file = XgFileReader.ReadFile(path);
            fullDecisions += XgDecisionIterator
                .Iterate(file, Path.GetFileName(path))
                .Count();
        }
        sw.Stop();
        double fullMs = sw.Elapsed.TotalMilliseconds;
        double fullPerSec = files.Count / (fullMs / 1000.0);

        // --- ReadGameHeaders streaming fast path ---
        var state = new XgIteratorState();
        sw.Restart();
        int totalGames = 0;
        foreach (var path in files)
        {
            foreach (var game in XgFileReader.ReadGameHeaders(path, state))
                totalGames++;
        }
        sw.Stop();
        double fastMs = sw.Elapsed.TotalMilliseconds;
        double fastPerSec = files.Count / (fastMs / 1000.0);

        double speedup = fastPerSec / fullPerSec;

        output.WriteLine($"Files: {files.Count}");
        output.WriteLine($"Full iteration:    {fullMs,8:F1} ms  ({fullPerSec,6:F1} files/sec)  {fullDecisions} decisions");
        output.WriteLine($"ReadGameHeaders:   {fastMs,8:F1} ms  ({fastPerSec,6:F1} files/sec)  {totalGames} games");
        output.WriteLine($"Speedup:           {speedup:F1}x");

        speedup.Should().BeGreaterThan(MinSpeedupFactor,
            $"ReadGameHeaders should be at least {MinSpeedupFactor}x faster than full iteration");
    }
    /// <summary>
    /// Verifies ReadGameHeaders streaming overload returns correct away scores,
    /// IsCrawfordGame, and IsStandardStart against the full-parse path.
    /// </summary>
    [Fact]
    public void ReadGameHeaders_ReturnsCorrectData()
    {
        var files = TestPaths.XgFiles.ToList();
        if (files.Count < MinFiles)
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
                fastInfos[i].Away1.Should().Be(fullInfos[i].Away1,
                    $"Away1 mismatch game {i + 1} in {Path.GetFileName(path)}");
                fastInfos[i].Away2.Should().Be(fullInfos[i].Away2,
                    $"Away2 mismatch game {i + 1} in {Path.GetFileName(path)}");
                fastInfos[i].IsCrawfordGame.Should().Be(fullInfos[i].IsCrawfordGame,
                    $"IsCrawfordGame mismatch game {i + 1} in {Path.GetFileName(path)}");
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
    /// <summary>
    /// Compares throughput of iterating decisions from .xg vs pre-parsed .json files.
    /// </summary>
    [Fact]
    public void JsonIteration_VsXgIteration()
    {
        var xgFiles = TestPaths.XgFiles.ToList();
        if (xgFiles.Count < MinFiles)
        {
            output.WriteLine($"Skipping — fewer than {MinFiles} .xg files available.");
            return;
        }

        // Write JSONs to a fresh temp dir so stale files from prior runs can't inflate the count.
        string tempJsonDir = Path.Combine(Path.GetTempPath(), $"xg_bench_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempJsonDir);
        try
        {
            foreach (var path in xgFiles)
            {
                string jsonPath = Path.Combine(tempJsonDir,
                    Path.GetFileNameWithoutExtension(path) + ".json");
                var file = XgFileReader.ReadFile(path);
                File.WriteAllText(jsonPath, XgFileReader.ToJson(file));
            }

            // Warm up
            XgDecisionIterator.IterateXgDirectory(TestPaths.XgDir).ToList();
            XgDecisionIterator.IterateJsonDirectory(tempJsonDir).ToList();

            // --- XG iteration ---
            var sw = Stopwatch.StartNew();
            int xgDecisions = XgDecisionIterator.IterateXgDirectory(TestPaths.XgDir).Count();
            sw.Stop();
            double xgMs = sw.Elapsed.TotalMilliseconds;
            double xgPerSec = xgFiles.Count / (xgMs / 1000.0);

            // --- JSON iteration ---
            sw.Restart();
            int jsonDecisions = XgDecisionIterator.IterateJsonDirectory(tempJsonDir).Count();
            sw.Stop();
            double jsonMs = sw.Elapsed.TotalMilliseconds;
            double jsonPerSec = xgFiles.Count / (jsonMs / 1000.0);

            double speedup = jsonPerSec / xgPerSec;

            output.WriteLine($"Files: {xgFiles.Count}");
            output.WriteLine($"XG iteration:   {xgMs,8:F1} ms  ({xgPerSec,6:F1} files/sec)  {xgDecisions} decisions");
            output.WriteLine($"JSON iteration: {jsonMs,8:F1} ms  ({jsonPerSec,6:F1} files/sec)  {jsonDecisions} decisions");
            output.WriteLine($"Speedup:        {speedup:F1}x");

            jsonDecisions.Should().Be(xgDecisions,
                "JSON and XG iteration should yield identical decision counts");
        }
        finally
        {
            Directory.Delete(tempJsonDir, recursive: true);
        }
    }
}