using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Discord.AspNetCore;

internal sealed class DiscordHttpInteractionContextFactory : IDiscordHttpInteractionContextFactory
{
    public IRestInteractionContext Create(IServiceProvider services, DiscordRestClient client,
        RestInteraction interaction, Func<string, Task> interactionResponseCallback)
        => new RestInteractionContext(client, interaction, interactionResponseCallback);
}

internal sealed class DelegateDiscordHttpInteractionContextFactory<TContext> : IDiscordHttpInteractionContextFactory
    where TContext : class, IRestInteractionContext
{
    private readonly Func<IServiceProvider, DiscordRestClient, RestInteraction, Func<string, Task>, TContext> _factory;

    public DelegateDiscordHttpInteractionContextFactory(
        Func<IServiceProvider, DiscordRestClient, RestInteraction, Func<string, Task>, TContext> factory)
        => _factory = factory;

    public IRestInteractionContext Create(IServiceProvider services, DiscordRestClient client,
        RestInteraction interaction, Func<string, Task> interactionResponseCallback)
        => _factory(services, client, interaction, interactionResponseCallback);
}

internal sealed class ActivatingDiscordHttpInteractionContextFactory<TContext> : IDiscordHttpInteractionContextFactory
    where TContext : class, IRestInteractionContext
{
    public IRestInteractionContext Create(IServiceProvider services, DiscordRestClient client,
        RestInteraction interaction, Func<string, Task> interactionResponseCallback)
        => ActivatorUtilities.CreateInstance<TContext>(services, client, interaction, interactionResponseCallback);
}
