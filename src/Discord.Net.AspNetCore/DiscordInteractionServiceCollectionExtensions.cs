using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace Discord.AspNetCore;

/// <summary>DI registrations for Discord HTTP interactions.</summary>
public static class DiscordInteractionServiceCollectionExtensions
{
    /// <summary>Registers HTTP interaction services with a DI-activated custom interaction context.</summary>
    public static IServiceCollection AddDiscordHttpInteractions<TContext>(this IServiceCollection services,
        Action<DiscordHttpInteractionOptions> configure)
        where TContext : class, IRestInteractionContext
        => services.AddDiscordHttpInteractions<TContext>(configure, (Action<InteractionServiceConfig>)null);

    /// <summary>Registers HTTP interaction services with a DI-activated custom interaction context and service configuration.</summary>
    public static IServiceCollection AddDiscordHttpInteractions<TContext>(this IServiceCollection services,
        Action<DiscordHttpInteractionOptions> configure,
        Action<InteractionServiceConfig> configureInteractionService)
        where TContext : class, IRestInteractionContext
    {
        services.AddDiscordHttpInteractions(configure, configureInteractionService);
        return services.AddDiscordHttpInteractionContext<TContext>();
    }

    /// <summary>Registers HTTP interaction services with a custom interaction context.</summary>
    public static IServiceCollection AddDiscordHttpInteractions<TContext>(this IServiceCollection services,
        Action<DiscordHttpInteractionOptions> configure,
        Func<IServiceProvider, DiscordRestClient, RestInteraction, Func<string, Task>, TContext> contextFactory)
        where TContext : class, IRestInteractionContext
    {
        services.AddDiscordHttpInteractions(configure, null);
        return services.AddDiscordHttpInteractionContext(contextFactory);
    }

    /// <summary>Registers HTTP interaction services with a custom interaction context and service configuration.</summary>
    public static IServiceCollection AddDiscordHttpInteractions<TContext>(this IServiceCollection services,
        Action<DiscordHttpInteractionOptions> configure,
        Action<InteractionServiceConfig> configureInteractionService,
        Func<IServiceProvider, DiscordRestClient, RestInteraction, Func<string, Task>, TContext> contextFactory)
        where TContext : class, IRestInteractionContext
    {
        services.AddDiscordHttpInteractions(configure, configureInteractionService);
        return services.AddDiscordHttpInteractionContext(contextFactory);
    }

    /// <summary>Registers a custom context factory for HTTP interaction execution.</summary>
    public static IServiceCollection AddDiscordHttpInteractionContext<TContext>(this IServiceCollection services)
        where TContext : class, IRestInteractionContext
    {
        services.Replace(ServiceDescriptor.Singleton<IDiscordHttpInteractionContextFactory,
            ActivatingDiscordHttpInteractionContextFactory<TContext>>());
        return services;
    }

    /// <summary>Registers a custom context factory for HTTP interaction execution.</summary>
    public static IServiceCollection AddDiscordHttpInteractionContext<TContext>(this IServiceCollection services,
        Func<IServiceProvider, DiscordRestClient, RestInteraction, Func<string, Task>, TContext> factory)
        where TContext : class, IRestInteractionContext
    {
        ArgumentNullException.ThrowIfNull(factory);
        services.Replace(ServiceDescriptor.Singleton<IDiscordHttpInteractionContextFactory>(
            _ => new DelegateDiscordHttpInteractionContextFactory<TContext>(factory)));
        return services;
    }

    /// <summary>Registers a standalone REST client configured for HTTP interactions.</summary>
    public static IServiceCollection AddDiscordNetRestClient(this IServiceCollection services,
        Action<DiscordRestConfig> configure = null)
    {
        services.Replace(ServiceDescriptor.Singleton(_ => CreateRestClient(configure)));
        return services;
    }

    /// <summary>Registers HTTP interaction verification and execution services without a Gateway dependency.</summary>
    public static IServiceCollection AddDiscordHttpInteractions(this IServiceCollection services,
        Action<DiscordHttpInteractionOptions> configure)
        => services.AddDiscordHttpInteractions(configure, null);

    /// <summary>Registers HTTP interaction verification, execution, and interaction service configuration.</summary>
    public static IServiceCollection AddDiscordHttpInteractions(this IServiceCollection services,
        Action<DiscordHttpInteractionOptions> configure,
        Action<InteractionServiceConfig> configureInteractionService)
    {
        services.AddOptions<DiscordHttpInteractionOptions>().Configure(configure).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DiscordHttpInteractionOptions>, DiscordHttpInteractionOptionsValidator>());
        services.TryAddSingleton(_ => CreateRestClient(null));
        services.TryAddSingleton(_ => CreateInteractionServiceConfig(configureInteractionService));
        services.TryAddSingleton<InteractionService>();
        services.TryAddSingleton<IDiscordHttpInteractionContextFactory, DiscordHttpInteractionContextFactory>();
        services.TryAddSingleton<IDiscordHttpInteractionErrorHandler, DiscordInteractionErrorHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DiscordHttpInteractionLogHostedService>());
        return services;
    }

    private static InteractionServiceConfig CreateInteractionServiceConfig(
        Action<InteractionServiceConfig> configureInteractionService)
    {
        var config = new InteractionServiceConfig
        {
            DefaultRunMode = RunMode.Sync,
            AutoServiceScopes = false
        };
        configureInteractionService?.Invoke(config);

        if (config.DefaultRunMode != RunMode.Sync)
            throw new InvalidOperationException("HTTP interactions require InteractionServiceConfig.DefaultRunMode to be RunMode.Sync.");
        if (config.AutoServiceScopes)
            throw new InvalidOperationException("HTTP interactions require InteractionServiceConfig.AutoServiceScopes to be false.");

        return config;
    }

    private static DiscordRestClient CreateRestClient(Action<DiscordRestConfig> configure)
    {
        var config = new DiscordRestConfig
        {
            APIOnRestInteractionCreation = false
        };
        configure?.Invoke(config);
        return new DiscordRestClient(config);
    }
}
