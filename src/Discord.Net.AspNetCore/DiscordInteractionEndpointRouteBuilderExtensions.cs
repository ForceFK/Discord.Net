using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Discord.AspNetCore;

/// <summary>Endpoint routing extensions for Discord HTTP interactions.</summary>
public static class DiscordInteractionEndpointRouteBuilderExtensions
{
    /// <summary>Maps a POST-only Discord interactions endpoint.</summary>
    public static IEndpointConventionBuilder MapDiscordInteractions(this IEndpointRouteBuilder endpoints, string pattern)
        => endpoints.MapPost(pattern, DiscordInteractionEndpoint.HandleAsync);
}
