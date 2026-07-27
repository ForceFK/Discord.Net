using Microsoft.Extensions.Options;
using System;

namespace Discord.AspNetCore;

internal sealed class DiscordHttpInteractionOptionsValidator : IValidateOptions<DiscordHttpInteractionOptions>
{
    public ValidateOptionsResult Validate(string name, DiscordHttpInteractionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PublicKey))
            return ValidateOptionsResult.Fail("Discord HTTP interactions public key is required.");
        if (options.PublicKey.Length != 64 || !IsHex(options.PublicKey))
            return ValidateOptionsResult.Fail("Discord HTTP interactions public key must contain 64 hexadecimal characters.");
        if (options.MaximumRequestBodySize <= 0 || options.MaximumRequestBodySize > int.MaxValue)
            return ValidateOptionsResult.Fail($"MaximumRequestBodySize must be between 1 and {int.MaxValue} bytes.");
        return ValidateOptionsResult.Success;
    }

    private static bool IsHex(string value)
    {
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }
        return true;
    }
}
