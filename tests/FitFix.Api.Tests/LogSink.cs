using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FitFix.Api.Tests;

/// <summary>
/// Collects everything the hosted application logs, so a test can assert what the logs do
/// <em>not</em> contain (AD-9). Exceptions are kept too, because a leak through an exception's
/// text would be a leak all the same — but apart from the formatted message, so a stack
/// trace's own newlines cannot be mistaken for a line a request invented.
/// </summary>
public sealed class LogSink
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public void Add(LogEntry entry) => _entries.Enqueue(entry);

    public void Clear() => _entries.Clear();

    /// <summary>One entry per call to the logger.</summary>
    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    /// <summary>
    /// The formatted messages alone, one per call to the logger: the only place a value taken
    /// from a request can appear, so an embedded newline here is a line something forged.
    /// </summary>
    public IReadOnlyList<string> Messages => [.. _entries.Select(entry => entry.Message)];

    /// <summary>Everything logged so far, as one block of text, formatted as a console would.</summary>
    public string Text => string.Join('\n', _entries.Select(entry => entry.ToString()));
}

/// <summary>One call to the logger.</summary>
/// <param name="Level">Its level, which is how a client's departure is told from the service's own failure.</param>
/// <param name="Category">Its logger's name, which is how this service's lines are told from the framework's.</param>
/// <param name="Message">The formatted message, where any value taken from a request would be.</param>
/// <param name="Exception">What was attached, if anything.</param>
public sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Exception)
{
    public override string ToString() =>
        Exception is null ? $"[{Level}] {Category}: {Message}" : $"[{Level}] {Category}: {Message} {Exception}";
}

/// <summary>Feeds a <see cref="LogSink"/> from the application's logging pipeline.</summary>
public sealed class CollectingLoggerProvider(LogSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, sink);

    public void Dispose()
    {
    }

    private sealed class CollectingLogger(string category, LogSink sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Add(new LogEntry(logLevel, category, formatter(state, exception), exception));
    }
}
