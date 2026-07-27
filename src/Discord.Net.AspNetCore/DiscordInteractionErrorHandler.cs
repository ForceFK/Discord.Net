using Discord.Interactions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace Discord.AspNetCore;

/// <summary>Safe default HTTP interaction error handler.</summary>
public sealed class DiscordInteractionErrorHandler : IDiscordHttpInteractionErrorHandler
{
    private readonly DiscordHttpInteractionOptions _options;
    private readonly ILogger<DiscordInteractionErrorHandler> _logger;

    public DiscordInteractionErrorHandler(IOptions<DiscordHttpInteractionOptions> options,
        ILogger<DiscordInteractionErrorHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext httpContext, IRestInteractionContext interactionContext, Discord.Interactions.IResult result,
        CancellationToken cancellationToken)
    {
        if (result is ExecuteResult { Exception: not null } execution)
        {
            _logger.LogError(execution.Exception,
                "Discord HTTP interaction {InteractionId} failed during command execution.",
                interactionContext.Interaction.Id);
        }
        else
        {
            _logger.LogWarning("Discord HTTP interaction {InteractionId} failed with {Error}: {Reason}",
                interactionContext.Interaction.Id, result.Error, result.ErrorReason);
        }

        if (httpContext.Response.HasStarted || cancellationToken.IsCancellationRequested)
            return;

        var detail = _options.ExposeCommandErrors && result.Error is not InteractionCommandError.Exception
            ? result.ErrorReason
            : "The interaction could not be processed.";
        await interactionContext.Interaction.RespondAsync(detail, ephemeral: true)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
