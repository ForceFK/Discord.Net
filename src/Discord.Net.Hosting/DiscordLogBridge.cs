using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Discord.Hosting;

internal sealed class DiscordLogBridge
{
    private readonly ILogger _logger;

    public DiscordLogBridge(ILogger logger) => _logger = logger;

    public Task WriteAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Trace,
            LogSeverity.Debug => LogLevel.Debug,
            _ => LogLevel.None
        };

        if (level != LogLevel.None && _logger.IsEnabled(level))
            _logger.Log(level, message.Exception, "Discord.Net [{Source}] {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }
}
