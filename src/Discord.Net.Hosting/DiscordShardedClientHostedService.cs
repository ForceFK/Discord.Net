using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Discord.Hosting;

internal sealed class DiscordShardedClientHostedService : DiscordClientHostedService
{
    public DiscordShardedClientHostedService(DiscordShardedClient client, IOptions<DiscordHostingOptions> options, DiscordClientState state, ILogger<DiscordShardedClientHostedService> logger) : base(new DiscordShardedClientAdapter(client), options, state, logger)
    {
    }
}
