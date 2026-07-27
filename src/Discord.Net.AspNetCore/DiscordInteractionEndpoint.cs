using Discord.Interactions;
using Discord.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace Discord.AspNetCore;

internal static class DiscordInteractionEndpoint
{
    private const string SignatureHeader = "X-Signature-Ed25519";
    private const string TimestampHeader = "X-Signature-Timestamp";

    public static async Task HandleAsync(HttpContext httpContext, DiscordRestClient discord, InteractionService interactionService, IOptions<DiscordHttpInteractionOptions> optionsAccessor, IDiscordHttpInteractionContextFactory contextFactory, IDiscordHttpInteractionErrorHandler errorHandler, ILoggerFactory loggerFactory)
    {
        var options = optionsAccessor.Value;
        var logger = loggerFactory.CreateLogger("Discord.AspNetCore.Interactions");
        if (!TryGetSingleHeader(httpContext, SignatureHeader, out var signature) ||
            !TryGetSingleHeader(httpContext, TimestampHeader, out var timestamp))
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        byte[] body;
        try
        {
            body = await DiscordInteractionRequestReader.ReadAsync(httpContext.Request,
                options.MaximumRequestBodySize, httpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (BadHttpRequestException exception)
        {
            httpContext.Response.StatusCode = exception.StatusCode;
            return;
        }

        RestInteraction interaction;
        try
        {
            interaction = await discord.ParseHttpInteractionAsync(options.PublicKey, signature, timestamp, body,
                    static _ => false)
                .WaitAsync(httpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (BadSignatureException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        catch (FormatException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        catch (ArgumentException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "A signed Discord interaction payload could not be parsed.");
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var writer = new DiscordInteractionResponseWriter(httpContext, logger);
        var interactionContext = contextFactory.Create(httpContext.RequestServices, discord, interaction, writer.WriteAsync);

        if (interaction is RestPingInteraction ping)
        {
            await writer.WriteAsync(ping.AcknowledgePing()).ConfigureAwait(false);
            return;
        }

        try
        {
            if (SelectedCommandUsesAsyncRunMode(interactionService, interaction))
            {
                var invalidRunMode = new DiscordHttpInteractionFailureResult(InteractionCommandError.Unsuccessful,
                    "HTTP interaction modules must use RunMode.Sync.");
                await errorHandler.HandleAsync(httpContext, interactionContext, invalidRunMode, httpContext.RequestAborted)
                    .ConfigureAwait(false);
                return;
            }

            var result = await interactionService.ExecuteCommandAsync(interactionContext, httpContext.RequestServices)
                .WaitAsync(httpContext.RequestAborted).ConfigureAwait(false);

            if (!result.IsSuccess && !writer.HasResponded)
            {
                await errorHandler.HandleAsync(httpContext, interactionContext, result, httpContext.RequestAborted)
                    .ConfigureAwait(false);
            }
            else if (!writer.HasResponded)
            {
                var noResponse = new DiscordHttpInteractionFailureResult(InteractionCommandError.Unsuccessful,
                    "The interaction command completed without an initial response or defer.");
                await errorHandler.HandleAsync(httpContext, interactionContext, noResponse, httpContext.RequestAborted)
                    .ConfigureAwait(false);
            }
            else if (options.EnableRequestLogging)
            {
                logger.LogInformation("Processed Discord HTTP interaction {InteractionId} ({InteractionType}).",
                    interaction.Id, interaction.Type);
            }
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            var cancelled = new DiscordHttpInteractionFailureResult(InteractionCommandError.Unsuccessful,
                "The interaction request was cancelled.");
            await errorHandler.HandleAsync(httpContext, interactionContext, cancelled, httpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An exception occurred while processing Discord interaction {InteractionId}.", interaction.Id);
            var failure = new DiscordHttpInteractionFailureResult(InteractionCommandError.Exception,
                "An internal command exception occurred.");
            await errorHandler.HandleAsync(httpContext, interactionContext, failure, httpContext.RequestAborted)
                .ConfigureAwait(false);
        }
    }

    private static bool TryGetSingleHeader(HttpContext context, string name, out string value)
    {
        var values = context.Request.Headers[name];
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            value = null;
            return false;
        }
        value = values[0];
        return true;
    }

    private static bool SelectedCommandUsesAsyncRunMode(InteractionService service, RestInteraction interaction)
        => interaction switch
        {
            ISlashCommandInteraction slash => IsAsync(service.SearchSlashCommand(slash)),
            IComponentInteraction component => IsAsync(service.SearchComponentCommand(component)),
            IUserCommandInteraction user => IsAsync(service.SearchUserCommand(user)),
            IMessageCommandInteraction message => IsAsync(service.SearchMessageCommand(message)),
            IAutocompleteInteraction autocomplete => IsAsync(service.SearchAutocompleteCommand(autocomplete)),
            IModalInteraction modal => IsAsync(service.SearchModalCommand(modal)),
            _ => false
        };

    private static bool IsAsync<TCommand>(SearchResult<TCommand> result)
        where TCommand : class, ICommandInfo
        => result.IsSuccess && result.Command.RunMode == RunMode.Async;
}
