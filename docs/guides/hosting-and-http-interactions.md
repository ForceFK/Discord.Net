# Generic Host and ASP.NET Core HTTP interactions

`Discord.Net.Hosting` and `Discord.Net.AspNetCore` are independent integrations. Applications may reference either package without pulling the other integration into their runtime.

## Gateway client in a Generic Host

```csharp
using Discord;
using Discord.Hosting;
using Discord.WebSocket;

builder.Services.AddDiscordNetSocketClient(config =>
{
    config.GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages;
});

builder.Services.AddDiscordNetHosting(options =>
{
    options.Token = builder.Configuration["Discord:Token"]!;
    options.WaitForReady = true;
    options.ReadyTimeout = TimeSpan.FromSeconds(30);
});
```

Use `AddDiscordNetShardedClient` instead of `AddDiscordNetSocketClient` for `DiscordShardedClient`. The client is a singleton. Login/start and stop/logout follow the host lifetime, Discord logs are forwarded to `ILogger`, and `DiscordClientState` exposes `IsStarted`, `IsReady`, `ReadyTimedOut`, `LastError`, and `WaitForReadyAsync`.

Waiting for readiness is opt-in. A readiness timeout is observable and does not fail or indefinitely block host startup. This package never loads interaction modules or registers application commands.

## HTTP interactions without Gateway

```csharp
using Discord.AspNetCore;
using Discord.Interactions;
using Discord.Rest;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDiscordHttpInteractions(options =>
{
    options.PublicKey = builder.Configuration["Discord:PublicKey"]!;
});

var app = builder.Build();

var interactions = app.Services.GetRequiredService<InteractionService>();
await interactions.AddModuleAsync<PingModule>(app.Services);

app.MapDiscordInteractions("/interactions");
await app.RunAsync();

public sealed class PingModule : RestInteractionModuleBase<RestInteractionContext>
{
    [SlashCommand("ping", "Responde com Pong!")]
    public Task PingAsync() => RespondAsync("Pong!");
}
```

To customize the standalone REST client, register it before or after the HTTP interaction services:

```csharp
builder.Services.AddDiscordNetRestClient(config =>
{
    config.LogLevel = LogSeverity.Verbose;
});

builder.Services.AddDiscordHttpInteractions(options =>
{
    options.PublicKey = builder.Configuration["Discord:PublicKey"]!;
});
```

The explicit REST registration wins regardless of registration order. It does not register or require a Gateway client. When omitted, `AddDiscordHttpInteractions` creates the same standalone REST client with default settings.

To preserve a custom REST context, use the generic overload. The context is activated through DI and receives the standard `DiscordRestClient`, `RestInteraction`, and response-callback constructor arguments:

```csharp
builder.Services.AddDiscordHttpInteractions<CustomRestInteractionContext>(
    options =>
    {
        options.PublicKey = builder.Configuration["Discord:PublicKey"]!;
    });
```

When the context needs custom construction, use the factory overload. The runtime context remains the custom type even though the endpoint handles it through `IRestInteractionContext`:

```csharp
builder.Services.AddDiscordHttpInteractions<CustomRestInteractionContext>(
    options => options.PublicKey = builder.Configuration["Discord:PublicKey"]!,
    (services, client, interaction, responseCallback) => new CustomRestInteractionContext(
        client, interaction, responseCallback)
    {
        Gateway = services.GetRequiredService<DiscordSocketClient>()
    });
```

`AddDiscordHttpInteractions` registers a REST client with API lookup during interaction creation disabled, `InteractionService` with `DefaultRunMode = RunMode.Sync`, and `AutoServiceScopes = false`. The latter is recommended because ASP.NET Core already creates a request scope and the endpoint passes `HttpContext.RequestServices` directly to command execution. If supplying a custom `InteractionServiceConfig`, retain those two values.

Configure other interaction-service settings through the overload:

```csharp
builder.Services.AddDiscordHttpInteractions(
    options => options.PublicKey = builder.Configuration["Discord:PublicKey"]!,
    interactions =>
    {
        interactions.LogLevel = LogSeverity.Verbose;
        interactions.UseCompiledLambda = true;
    });
```

The HTTP integration rejects `RunMode` values other than `Sync` and `AutoServiceScopes = true`.

Discord REST and interaction-service logs are forwarded to the `Discord.Net.Rest` and `Discord.Net.Interactions` `ILogger` categories. Gateway logs are forwarded by `Discord.Net.Hosting`. Each bridge checks `ILogger.IsEnabled` before formatting and writing an event, so normal ASP.NET Core logging filters apply without requiring manual event subscriptions.

The endpoint validates the Ed25519 signature over the unmodified body bytes before parsing, limits request size, accepts only POST, suppresses REST lookups during interaction creation even when a shared client enables them, uses a response callback stored on the request's own context, and permits exactly one initial response. It does not auto-defer. Followups and original-response edits/deletes continue through the normal REST APIs on `RestInteraction`.

Replace `IDiscordHttpInteractionErrorHandler` to customize safe command error responses. The default sends a generic ephemeral response and never exposes exception details. `ExposeCommandErrors` only exposes non-exception command errors.

## Options

`DiscordHostingOptions` provides `Token`, `TokenType`, `WaitForReady`, and `ReadyTimeout`.

`DiscordHttpInteractionOptions` provides `PublicKey`, `MaximumRequestBodySize` (1 MiB by default), `ExposeCommandErrors`, and `EnableRequestLogging`. Both option types are validated when the host starts. Tokens, signatures, and public keys are never logged.

## Limitations

- Commands explicitly attributed with `RunMode.Async` are not suitable for HTTP interaction endpoints; use the registered synchronous default or explicitly select `RunMode.Sync`.
- No auto-defer or automatic module/application-command registration is performed.
- HTTP file responses are constrained by ASP.NET Core's normal response and server limits.

## Required REST compatibility changes

The HTTP-only flow required three narrow fixes in `Discord.Net.Rest`: parsing PING payloads that have no user/member, treating an omitted entitlement array as empty, and allowing interaction-token webhook operations (followups and original-response get/edit/delete) without a logged-in bot session. Each webhook operation receives the application ID from its own interaction instead of relying on mutable client-global state. All other REST routes retain their login-state requirement.
