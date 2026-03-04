namespace ConvertXgToJson_Lib.Tests;

internal static class TestPaths
{
    private static readonly string _root =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\TestData"));

    public static string XgpDir => Path.Combine(_root, "xgp");
    public static string XgDir => Path.Combine(_root, "xg");
    public static string OutputDir => Path.Combine(_root, "Output");

    public static IEnumerable<string> XgpFiles =>
        Directory.EnumerateFiles(XgpDir, "*.xgp");

    public static IEnumerable<string> XgFiles =>
        Directory.EnumerateFiles(XgDir, "*.xg");
    public static string CsvDir => Path.Combine(_root, "Csv");
    public static string outputFilePath = $@"D:\Users\Hal\Documents\Excel\Backgammon";
}
