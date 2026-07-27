using Discord.AspNetCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Discord.Net.AspNetCore.Tests;

public sealed class DiscordLogBridgeTests
{
    [Fact]
    public async Task MapsDiscordSeverityAndPreservesException()
    {
        var logger = new RecordingLogger(true);
        var bridge = new DiscordLogBridge(logger);
        var exception = new InvalidOperationException("failure");

        await bridge.WriteAsync(new LogMessage(LogSeverity.Warning, "Rest", "warning", exception));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(exception, entry.Exception);
        Assert.Contains("Rest", entry.Message);
        Assert.Contains("warning", entry.Message);
    }

    [Fact]
    public async Task DisabledLevelDoesNotWriteToLogger()
    {
        var logger = new RecordingLogger(false);
        var bridge = new DiscordLogBridge(logger);

        await bridge.WriteAsync(new LogMessage(LogSeverity.Info, "Rest", "ignored"));

        Assert.Empty(logger.Entries);
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly bool _enabled;
        public List<(LogLevel Level, Exception Exception, string Message)> Entries { get; } = new();

        public RecordingLogger(bool enabled) => _enabled = enabled;
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => _enabled;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
            => Entries.Add((logLevel, exception, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }
}
