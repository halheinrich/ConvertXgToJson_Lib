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

            fast.Player1.Should().Be(full.Player1,
                $"Player1 mismatch in {Path.GetFileName(path)}");
            fast.Player2.Should().Be(full.Player2,
                $"Player2 mismatch in {Path.GetFileName(path)}");
            fast.MatchLength.Should().Be(full.MatchLength,
                $"MatchLength mismatch in {Path.GetFileName(path)}");
        }
    }
    /// <summary>
    /// Compares throughput of full decision iteration vs. game-header-only scan.
    /// Game-header scan decompresses the xg stream but skips move/cube parsing.
    /// </summary>
    [Fact]
    public void GameHeaderScan_IsFasterThanFullIteration()
    {
        var files = TestPaths.XgFiles.ToList();
        if (files.Count < MinFiles)
        {
            output.WriteLine($"Skipping — fewer than {MinFiles} .xg files available.");
            return;
        }

        // Warm up
        foreach (var path in files.Take(2))
        {
            XgDecisionIterator.Iterate(XgFileReader.ReadFile(path),
                Path.GetFileNameWithoutExtension(path)).ToList();
            XgFileReader.ReadFile(path); // decompress only
        }

        // --- Full decision iteration ---
        var sw = Stopwatch.StartNew();
        int fullDecisions = 0;
        int fullFiles = 0;
        foreach (var path in files)
        {
            var file = XgFileReader.ReadFile(path);
            fullDecisions += XgDecisionIterator
                .Iterate(file, Path.GetFileNameWithoutExtension(path))
                .Count();
            fullFiles++;
        }
        sw.Stop();
        double fullMs = sw.Elapsed.TotalMilliseconds;
        double fullPerSec = fullFiles / (fullMs / 1000.0);

        // --- Game-header-only scan ---
        // ReadFile still decompresses everything, but we only look at GameHeaderRecords.
        // This isolates the cost of move/cube parsing vs. header-only inspection.
        sw.Restart();
        int headerGames = 0;
        int headerFiles = 0;
        foreach (var path in files)
        {
            var file = XgFileReader.ReadFile(path);
            headerGames += file.Records.OfType<GameHeaderRecord>().Count();
            headerFiles++;
        }
        sw.Stop();
        double headerMs = sw.Elapsed.TotalMilliseconds;
        double headerPerSec = headerFiles / (headerMs / 1000.0);

        double speedup = headerPerSec / fullPerSec;

        output.WriteLine($"Files: {files.Count}");
        output.WriteLine($"Full iteration:      {fullMs,8:F1} ms  ({fullPerSec,6:F1} files/sec)  {fullDecisions} decisions");
        output.WriteLine($"Game-header scan:    {headerMs,8:F1} ms  ({headerPerSec,6:F1} files/sec)  {headerGames} games");
        output.WriteLine($"Speedup:             {speedup:F1}x");

        // No strict assertion — this is diagnostic. Just confirm it's not slower.
        speedup.Should().BeGreaterThan(0.5,
            "game-header scan should not be significantly slower than full iteration");
    }
    /// <summary>
    /// Compares throughput of full decision iteration vs. ReadGameHeaders fast path.
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
        foreach (var path in files.Take(2))
        {
            XgFileReader.ReadGameHeaders(path);
            XgDecisionIterator.Iterate(XgFileReader.ReadFile(path),
                Path.GetFileNameWithoutExtension(path)).ToList();
        }

        // --- Full decision iteration ---
        var sw = Stopwatch.StartNew();
        int fullDecisions = 0;
        foreach (var path in files)
        {
            var file = XgFileReader.ReadFile(path);
            fullDecisions += XgDecisionIterator
                .Iterate(file, Path.GetFileNameWithoutExtension(path))
                .Count();
        }
        sw.Stop();
        double fullMs = sw.Elapsed.TotalMilliseconds;
        double fullPerSec = files.Count / (fullMs / 1000.0);

        // --- ReadGameHeaders fast path ---
        sw.Restart();
        int totalGames = 0;
        foreach (var path in files)
        {
            var headers = XgFileReader.ReadGameHeaders(path);
            totalGames += headers.Count;
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
    /// Verifies ReadGameHeaders returns correct away scores and IsStandardStart
    /// against the full-parse path.
    /// </summary>
    [Fact]
    public void ReadGameHeaders_ReturnsCorrectData()
    {
        var files = TestPaths.XgFiles.ToList();
        if (files.Count < MinFiles)
            return;

        foreach (var path in files)
        {
            var fast = XgFileReader.ReadGameHeaders(path);
            var file = XgFileReader.ReadFile(path);

            var state = new XgIteratorState();
            var fullInfos = new List<XgGameInfo>();
            int? lastGame = null;

            foreach (var row in XgDecisionIterator.Iterate(
                file, Path.GetFileNameWithoutExtension(path), state))
            {
                if (row.Game != lastGame)
                {
                    fullInfos.Add(state.GameInfo!);
                    lastGame = row.Game;
                }
            }

            fast.Count.Should().Be(fullInfos.Count,
                $"game count mismatch in {Path.GetFileName(path)}");

            for (int i = 0; i < fast.Count; i++)
            {
                fast[i].Away1.Should().Be(fullInfos[i].Away1,
                    $"Away1 mismatch game {i + 1} in {Path.GetFileName(path)}");
                fast[i].Away2.Should().Be(fullInfos[i].Away2,
                    $"Away2 mismatch game {i + 1} in {Path.GetFileName(path)}");
                fast[i].IsCrawfordGame.Should().Be(fullInfos[i].IsCrawfordGame,
                    $"IsCrawfordGame mismatch game {i + 1} in {Path.GetFileName(path)}");
                fast[i].IsStandardStart.Should().Be(fullInfos[i].IsStandardStart,
                    $"IsStandardStart mismatch game {i + 1} in {Path.GetFileName(path)}");
            }
        }
    }
}