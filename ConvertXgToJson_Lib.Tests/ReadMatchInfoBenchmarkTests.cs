// ReadMatchInfoBenchmarkTests.cs
using ConvertXgToJson_Lib;
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
}