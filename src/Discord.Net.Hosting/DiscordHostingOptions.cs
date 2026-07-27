using System;

namespace Discord.Hosting;

/// <summary>
/// Configures the lifetime of a Discord.Net gateway client.
/// </summary>
public sealed class DiscordHostingOptions
{
    /// <summary>
    /// Gets or sets the Discord token. It is never logged by this package.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the token type.
    /// </summary>
    public TokenType TokenType { get; set; } = TokenType.Bot;

    /// <summary>
    /// Gets or sets whether host startup waits for gateway readiness.
    /// </summary>
    public bool WaitForReady { get; set; }

    /// <summary>
    /// Gets or sets the maximum readiness wait. A timeout leaves the host running with observable non-ready state.
    /// </summary>
    public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
