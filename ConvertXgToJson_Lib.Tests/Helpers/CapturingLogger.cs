using Microsoft.Extensions.Logging;

namespace ConvertXgToJson_Lib.Tests.Helpers;

/// <summary>
/// Minimal <see cref="ILogger"/> that captures the level, the fully formatted
/// message and the structured values of each entry — enough to assert what
/// the iterator logs for a skipped decision, and that it logs nothing for one
/// it passes by silently.
/// </summary>
internal sealed class CapturingLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    /// <summary>The structured values of each entry, by placeholder name, parallel to <see cref="Entries"/>.</summary>
    public List<IReadOnlyDictionary<string, object?>> Values { get; } = [];

    /// <summary>The messages logged at <see cref="LogLevel.Warning"/>, in order.</summary>
    public IReadOnlyList<string> Warnings =>
        [.. Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message)];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
        Values.Add(state is IEnumerable<KeyValuePair<string, object?>> values
            ? values.ToDictionary(v => v.Key, v => v.Value)
            : new Dictionary<string, object?>());
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
