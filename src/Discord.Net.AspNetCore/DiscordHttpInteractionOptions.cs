namespace Discord.AspNetCore;

/// <summary>
/// Configures the Discord HTTP interactions endpoint.
/// </summary>
public sealed class DiscordHttpInteractionOptions
{
    /// <summary>
    /// Gets or sets the application's Ed25519 public key.
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum raw request body size.
    /// </summary>
    public long MaximumRequestBodySize { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets or sets whether safe command error details are included in public responses.
    /// </summary>
    public bool ExposeCommandErrors { get; set; }

    /// <summary>
    /// Gets or sets whether request outcome logging is enabled.
    /// </summary>
    public bool EnableRequestLogging { get; set; } = true;
}
