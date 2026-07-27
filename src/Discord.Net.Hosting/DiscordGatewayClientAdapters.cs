using Discord.Logging;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Discord.Hosting;

internal sealed class DiscordSocketClientAdapter : IDiscordGatewayClient
{
    private readonly DiscordSocketClient _client;
    public DiscordSocketClientAdapter(DiscordSocketClient client) => _client = client;
    public ConnectionState ConnectionState => _client.ConnectionState;
    public event Func<LogMessage, Task> Log { add => _client.Log += value; remove => _client.Log -= value; }
    public event Func<Task> Ready { add => _client.Ready += value; remove => _client.Ready -= value; }
    public Task LoginAsync(TokenType tokenType, string token) => _client.LoginAsync(tokenType, token);
    public Task StartAsync() => _client.StartAsync();
    public Task StopAsync() => _client.StopAsync();
    public Task LogoutAsync() => _client.LogoutAsync();
}

internal sealed class DiscordShardedClientAdapter : IDiscordGatewayClient
{
    private readonly DiscordShardedClient _client;
    private readonly ConcurrentDictionary<int, byte> _readyShards = new();
    private event Func<Task> ReadyHandlers;

    public DiscordShardedClientAdapter(DiscordShardedClient client)
    {
        _client = client;
        _client.ShardReady += OnShardReadyAsync;
    }

    public ConnectionState ConnectionState => _client.ConnectionState;

    public event Func<LogMessage, Task> Log { add => _client.Log += value; remove => _client.Log -= value; }
    public event Func<Task> Ready
    {
        add
        {
            if (ReadyHandlers is null)
                _readyShards.Clear();
            ReadyHandlers += value;
        }
        remove => ReadyHandlers -= value;
    }
    public Task LoginAsync(TokenType tokenType, string token) => _client.LoginAsync(tokenType, token);
    public Task StartAsync() => _client.StartAsync();
    public Task StopAsync() => _client.StopAsync();
    public Task LogoutAsync() => _client.LogoutAsync();

    private Task OnShardReadyAsync(DiscordSocketClient shard)
    {
        _readyShards.TryAdd(shard.ShardId, 0);
        return _readyShards.Count >= _client.Shards.Count && ReadyHandlers is not null
            ? ReadyHandlers.Invoke()
            : Task.CompletedTask;
    }
}
