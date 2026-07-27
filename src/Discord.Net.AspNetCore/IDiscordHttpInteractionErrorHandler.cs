using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Discord.AspNetCore;

/// <summary>Handles command and endpoint errors after a Discord interaction has been created.</summary>
public interface IDiscordHttpInteractionErrorHandler
{
    /// <summary>Handles an unsuccessful interaction execution.</summary>
    Task HandleAsync(HttpContext httpContext, IRestInteractionContext interactionContext, Interactions.IResult result,
        CancellationToken cancellationToken);
}
