using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DonetickCalDav.Testing.Harness;

/// <summary>One log line written by the application under test.</summary>
public sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Exception);

/// <summary>
/// Collects the application's log output, so tests can assert on it, and copies every line to the
/// xUnit test output, so a failing test shows what the bridge did.
/// </summary>
public sealed class LogCollector
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

    public void Add(LogEntry entry) => _entries.Enqueue(entry);

    /// <summary>True when a line at <paramref name="level"/> or higher contains <paramref name="text"/>.</summary>
    public bool Contains(LogLevel level, string text) =>
        _entries.Any(e => e.Level >= level && e.Message.Contains(text, StringComparison.OrdinalIgnoreCase));
}

internal sealed class CollectingLoggerProvider(LogCollector collector, ITestOutputHelper? output) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, collector, output);

    public void Dispose() { }

    private sealed class CollectingLogger(string category, LogCollector collector, ITestOutputHelper? output) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            collector.Add(new LogEntry(logLevel, category, message, exception));

            try
            {
                output?.WriteLine($"[{logLevel,-11}] {ShortCategory(category)}: {message}"
                                  + (exception is null ? "" : $"{Environment.NewLine}{exception}"));
            }
            catch (InvalidOperationException)
            {
                // The test has already finished (for example a background poll during shutdown).
            }
        }

        private static string ShortCategory(string category) =>
            category.StartsWith("DonetickCalDav.", StringComparison.Ordinal) ? category["DonetickCalDav.".Length..] : category;
    }
}
