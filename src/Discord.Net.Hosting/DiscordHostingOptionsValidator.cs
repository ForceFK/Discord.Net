using Microsoft.Extensions.Options;
using System;

namespace Discord.Hosting;

internal sealed class DiscordHostingOptionsValidator : IValidateOptions<DiscordHostingOptions>
{
    public ValidateOptionsResult Validate(string name, DiscordHostingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Token))
            return ValidateOptionsResult.Fail("Discord hosting token is required.");
        if (options.ReadyTimeout <= TimeSpan.Zero)
            return ValidateOptionsResult.Fail("ReadyTimeout must be greater than zero.");
        return ValidateOptionsResult.Success;
    }
}
