using System;
using System.Threading.Tasks;

namespace Discord.Hosting;

internal interface IDiscordGatewayClient
{
    ConnectionState ConnectionState { get; }
    event Func<LogMessage, Task> Log;
    event Func<Task> Ready;
    Task LoginAsync(TokenType tokenType, string token);
    Task StartAsync();
    Task StopAsync();
    Task LogoutAsync();
}
