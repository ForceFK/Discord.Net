using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace Discord.AspNetCore;

internal sealed class DiscordHttpInteractionLogHostedService : IHostedService
{
    private readonly DiscordRestClient _restClient;
    private readonly InteractionService _interactionService;
    private readonly DiscordLogBridge _restLogBridge;
    private readonly DiscordLogBridge _interactionLogBridge;
    private bool _subscribed;

    public DiscordHttpInteractionLogHostedService(DiscordRestClient restClient, InteractionService interactionService,
        ILoggerFactory loggerFactory)
    {
        _restClient = restClient;
        _interactionService = interactionService;
        _restLogBridge = new DiscordLogBridge(loggerFactory.CreateLogger("Discord.Net.Rest"));
        _interactionLogBridge = new DiscordLogBridge(loggerFactory.CreateLogger("Discord.Net.Interactions"));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_subscribed)
            return Task.CompletedTask;

        _restClient.Log += _restLogBridge.WriteAsync;
        _interactionService.Log += _interactionLogBridge.WriteAsync;
        _subscribed = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_subscribed)
            return Task.CompletedTask;

        _restClient.Log -= _restLogBridge.WriteAsync;
        _interactionService.Log -= _interactionLogBridge.WriteAsync;
        _subscribed = false;
        return Task.CompletedTask;
    }
}
