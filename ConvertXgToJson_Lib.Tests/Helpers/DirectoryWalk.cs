using BgDataTypes_Lib;

namespace ConvertXgToJson_Lib.Tests.Helpers;

/// <summary>
/// The directory walk a caller composes from the public pieces, which this
/// library does not own (halheinrich/backgammon#359): the files listed,
/// each read through <see cref="XgSourceReader"/>, each decoded file
/// iterated under its file name. A file that fails to read fails the test,
/// naming the file and its exception — a test's walk never drops one.
/// </summary>
internal static class DirectoryWalk
{
    /// <summary>
    /// Every decision of every XG-format file in <paramref name="directory"/>,
    /// in <see cref="XgFileReader.EnumerateXgFormatFiles(string)"/>'s order,
    /// <paramref name="state"/> and <paramref name="callbacks"/> handed to
    /// each file's iteration.
    /// </summary>
    public static IEnumerable<DecisionRow> XgRows(
        string directory, XgIteratorState? state = null, XgIteratorCallbacks? callbacks = null) =>
        Rows(XgFileReader.EnumerateXgFormatFiles(directory), XgSourceFormat.Xg, state, callbacks);

    /// <summary>Every decision of every <c>*.json</c> export in <paramref name="directory"/>.</summary>
    public static IEnumerable<DecisionRow> JsonRows(string directory) =>
        Rows(Directory.EnumerateFiles(directory, "*.json"), XgSourceFormat.Json, state: null, callbacks: null);

    private static IEnumerable<DecisionRow> Rows(
        IEnumerable<string> paths, XgSourceFormat format, XgIteratorState? state, XgIteratorCallbacks? callbacks)
    {
        foreach (string path in paths)
        {
            var file = XgSourceReader.ReadFile(path, format).Match(
                decoded => decoded.File,
                failed => throw new Xunit.Sdk.XunitException(
                    $"{failed.SourceName} did not read: {failed.Exception}"));

            foreach (var row in XgDecisionIterator.Iterate(file, Path.GetFileName(path), state, callbacks))
                yield return row;
        }
    }
}
