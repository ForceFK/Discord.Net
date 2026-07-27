using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;

namespace Discord.Hosting;

/// <summary>
/// DI registrations for Discord.Net Generic Host integration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers shared Discord hosting options and state.
    /// </summary>
    public static IServiceCollection AddDiscordNetHosting(this IServiceCollection services, Action<DiscordHostingOptions> configure)
    {
        services.AddOptions<DiscordHostingOptions>().Configure(configure).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DiscordHostingOptions>, DiscordHostingOptionsValidator>());
        services.TryAddSingleton<DiscordClientState>();
        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="DiscordSocketClient"/> and its hosted lifecycle.
    /// </summary>
    public static IServiceCollection AddDiscordNetSocketClient(this IServiceCollection services, Action<DiscordSocketConfig> configure = null)
    {
        services.TryAddSingleton(_ =>
        {
            var config = new DiscordSocketConfig();
            configure?.Invoke(config);
            return new DiscordSocketClient(config);
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DiscordSocketClientHostedService>());
        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="DiscordShardedClient"/> and its hosted lifecycle.
    /// </summary>
    public static IServiceCollection AddDiscordNetShardedClient(this IServiceCollection services, Action<DiscordSocketConfig> configure = null)
    {
        services.TryAddSingleton(_ =>
        {
            var config = new DiscordSocketConfig();
            configure?.Invoke(config);
            return new DiscordShardedClient(config);
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DiscordShardedClientHostedService>());
        return services;
    }
}
