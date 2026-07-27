using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Discord.Hosting;

internal sealed class DiscordSocketClientHostedService : DiscordClientHostedService
{
    public DiscordSocketClientHostedService(DiscordSocketClient client, IOptions<DiscordHostingOptions> options,
        DiscordClientState state, ILogger<DiscordSocketClientHostedService> logger)
        : base(new DiscordSocketClientAdapter(client), options, state, logger) { }
}
