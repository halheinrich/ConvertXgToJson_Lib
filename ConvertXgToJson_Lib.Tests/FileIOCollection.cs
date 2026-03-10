namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Forces all test classes that read or write real files to run sequentially,
/// preventing file-locking conflicts when the suite runs in parallel.
/// </summary>
[CollectionDefinition("FileIO", DisableParallelization = true)]
public sealed class FileIOCollection { }