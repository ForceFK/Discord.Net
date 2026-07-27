using Discord.Rest;
using System;
using System.Threading.Tasks;

namespace Discord.AspNetCore;

/// <summary>Creates the interaction context used to execute an HTTP interaction.</summary>
public interface IDiscordHttpInteractionContextFactory
{
    /// <summary>Creates an interaction context for the current HTTP request.</summary>
    IRestInteractionContext Create(IServiceProvider services, DiscordRestClient client,
        RestInteraction interaction, Func<string, Task> interactionResponseCallback);
}
