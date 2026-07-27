using Discord.Interactions;

namespace Discord.AspNetCore;

internal sealed class DiscordHttpInteractionFailureResult : IResult
{
    public DiscordHttpInteractionFailureResult(InteractionCommandError error, string reason)
    {
        Error = error;
        ErrorReason = reason;
    }

    public InteractionCommandError? Error { get; }
    public string ErrorReason { get; }
    public bool IsSuccess => false;
}
