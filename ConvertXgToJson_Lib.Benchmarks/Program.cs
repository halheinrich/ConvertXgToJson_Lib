// Program.cs — the hand-run timing comparisons, moved out of the test suite
// (halheinrich/backgammon#438): each fast path against the full parse over
// every .xg file in one directory, printed with the speed-up and whether the
// expected minimum held. Exit code: 0 every expectation held, 1 one did not,
// 2 too few files to compare.
using System.Diagnostics;
using ConvertXgToJson_Lib;

const int MinFiles = 2;
const double MinSpeedupFactor = 3.0;

// The umbrella's TestData/xg, reached from bin/<config>/<tfm>/ the way the
// test project's TestPaths reaches it; a first argument overrides it.
string xgDir = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\TestData\xg"));

var files = Directory.Exists(xgDir)
    ? Directory.EnumerateFiles(xgDir, "*.xg").ToList()
    : [];

Console.WriteLine($"Directory: {xgDir}");
Console.WriteLine($"Files: {files.Count}");
if (files.Count < MinFiles)
{
    Console.WriteLine($"Fewer than {MinFiles} .xg files; nothing to compare.");
    return 2;
}

bool allHeld = true;
allHeld &= ReadMatchInfoVsReadFile(files);
allHeld &= ReadGameHeadersVsFullIteration(files);
return allHeld ? 0 : 1;

static bool ReadMatchInfoVsReadFile(List<string> files)
{
    // Warm up, so cold start does not skew the first measurement.
    foreach (var path in files.Take(2))
    {
        XgFileReader.ReadFile(path);
        XgFileReader.ReadMatchInfo(path);
    }

    var sw = Stopwatch.StartNew();
    foreach (var path in files)
        _ = XgFileReader.ReadFile(path);
    sw.Stop();
    double fullMs = sw.Elapsed.TotalMilliseconds;

    sw.Restart();
    foreach (var path in files)
        _ = XgFileReader.ReadMatchInfo(path);
    sw.Stop();
    double fastMs = sw.Elapsed.TotalMilliseconds;

    Console.WriteLine();
    Console.WriteLine("ReadMatchInfo vs ReadFile");
    Console.WriteLine($"  ReadFile:          {fullMs,8:F1} ms  ({PerSecond(files.Count, fullMs),6:F1} files/sec)");
    Console.WriteLine($"  ReadMatchInfo:     {fastMs,8:F1} ms  ({PerSecond(files.Count, fastMs),6:F1} files/sec)");
    return Report(fullMs, fastMs);
}

static bool ReadGameHeadersVsFullIteration(List<string> files)
{
    var warmState = new XgIteratorState();
    foreach (var path in files.Take(2))
    {
        XgFileReader.ReadGameHeaders(path, warmState).ToList();
        XgDecisionIterator.Iterate(XgFileReader.ReadFile(path), Path.GetFileName(path)).ToList();
    }

    var sw = Stopwatch.StartNew();
    int decisions = 0;
    foreach (var path in files)
        decisions += XgDecisionIterator.Iterate(XgFileReader.ReadFile(path), Path.GetFileName(path)).Count();
    sw.Stop();
    double fullMs = sw.Elapsed.TotalMilliseconds;

    var state = new XgIteratorState();
    sw.Restart();
    int games = 0;
    foreach (var path in files)
        games += XgFileReader.ReadGameHeaders(path, state).Count();
    sw.Stop();
    double fastMs = sw.Elapsed.TotalMilliseconds;

    Console.WriteLine();
    Console.WriteLine("ReadGameHeaders vs full decision iteration");
    Console.WriteLine($"  Full iteration:    {fullMs,8:F1} ms  ({PerSecond(files.Count, fullMs),6:F1} files/sec)  {decisions} decisions");
    Console.WriteLine($"  ReadGameHeaders:   {fastMs,8:F1} ms  ({PerSecond(files.Count, fastMs),6:F1} files/sec)  {games} games");
    return Report(fullMs, fastMs);
}

static double PerSecond(int count, double ms) => count / (ms / 1000.0);

static bool Report(double fullMs, double fastMs)
{
    double speedup = fullMs / fastMs;
    bool held = speedup > MinSpeedupFactor;
    Console.WriteLine($"  Speed-up:          {speedup:F1}x  (expected > {MinSpeedupFactor:F1}x: {(held ? "held" : "NOT held")})");
    return held;
}
