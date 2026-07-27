using Discord.AspNetCore;
using Discord.Interactions;
using Discord.Rest;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSec.Cryptography;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using RestResponse = global::Discord.Net.Rest.RestResponse;

namespace Discord.Net.AspNetCore.Tests;

public sealed class DiscordHttpInteractionEndpointTests : IAsyncLifetime
{
    private readonly SignatureAlgorithm _algorithm = SignatureAlgorithm.Ed25519;
    private readonly Key _key;
    private readonly string _publicKey;
    private IHost _host;
    private HttpClient _client;
    private readonly FakeRestClient _restClient = new();
    private readonly DiscordRestClient _discordClient;
    private readonly ConcurrentBag<Discord.Interactions.IResult> _results = new();

    public DiscordHttpInteractionEndpointTests()
    {
        _key = Key.Create(_algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        _publicKey = Convert.ToHexString(_key.PublicKey.Export(KeyBlobFormat.RawPublicKey)).ToLowerInvariant();
        _discordClient = new DiscordRestClient(new DiscordRestConfig
        {
            // The HTTP endpoint must suppress creation lookups even when a shared client enables them.
            APIOnRestInteractionCreation = true,
            RestClientProvider = _ => _restClient
        });
    }

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddScoped<ScopedMarker>();
                    services.AddHttpContextAccessor();
                    services.AddSingleton(_discordClient);
                    services.AddDiscordHttpInteractions<TestRestInteractionContext>(options =>
                        {
                            options.PublicKey = _publicKey;
                            options.MaximumRequestBodySize = 4096;
                        });
                })
                .Configure(app => app.UseRouting().UseEndpoints(endpoints => endpoints.MapDiscordInteractions("/interactions"))))
            .StartAsync();
        _client = _host.GetTestClient();
        Assert.Same(_discordClient, _host.Services.GetRequiredService<DiscordRestClient>());
        var service = _host.Services.GetRequiredService<InteractionService>();
        service.InteractionExecuted += (_, _, result) => { _results.Add(result); return Task.CompletedTask; };
        await service.AddModuleAsync<TestModule>(_host.Services);
        await service.AddModuleAsync<CustomContextModule>(_host.Services);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        _key.Dispose();
    }

    [Fact]
    public async Task ValidSignatureAndPingReturnPong()
    {
        var response = await PostSignedAsync(PingPayload());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("type").GetInt32());
    }

    [Fact]
    public async Task InvalidSignatureReturnsUnauthorized()
    {
        var request = CreateSignedRequest(PingPayload());
        request.Headers.Remove("X-Signature-Ed25519");
        request.Headers.Add("X-Signature-Ed25519", new string('0', 128));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task MissingSignatureReturnsUnauthorized()
    {
        var request = CreateSignedRequest(PingPayload());
        request.Headers.Remove("X-Signature-Ed25519");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task MissingTimestampReturnsUnauthorized()
    {
        var request = CreateSignedRequest(PingPayload());
        request.Headers.Remove("X-Signature-Timestamp");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task GetIsRejectedByPostOnlyRoute()
        => Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _client.GetAsync("/interactions")).StatusCode);

    [Fact]
    public async Task OversizedBodyReturnsPayloadTooLarge()
    {
        var response = await PostSignedAsync(PingPayload() + new string(' ', 5000));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task SlashCommandExecutesAndWritesInitialResponse()
    {
        var response = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "ping", type = 1 }));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        Assert.Contains("Pong!", body);
    }

    [Fact]
    public async Task CustomRestInteractionContextIsPreservedDuringExecution()
    {
        var response = await PostSignedAsync(CommandPayload(2,
            new { id = "3", name = "custom-context", type = 1 }));

        Assert.Contains("custom", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ButtonAndSelectCommandsExecute()
    {
        var button = await PostSignedAsync(CommandPayload(3, new { custom_id = "button", component_type = 2 }));
        Assert.Contains("button", await button.Content.ReadAsStringAsync());

        var select = await PostSignedAsync(CommandPayload(3, new { custom_id = "select", component_type = 3, values = new[] { "one" } }));
        Assert.Contains("one", await select.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AutocompleteExecutesAndReturnsChoices()
    {
        var data = new
        {
            id = "3", name = "search", type = 1,
            options = new[] { new { name = "value", type = 3, value = "a", focused = true } }
        };
        var response = await PostSignedAsync(CommandPayload(4, data));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("answer", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UserCommandUsesResolvedUser()
    {
        var data = new
        {
            id = "3", name = "inspect-user", type = 2, target_id = "6",
            resolved = new
            {
                users = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["6"] = new { id = "6", username = "target", discriminator = "0001", avatar = (string)null }
                }
            }
        };
        var response = await PostSignedAsync(CommandPayload(2, data));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("target", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MessageCommandUsesResolvedMessage()
    {
        var data = new
        {
            id = "3", name = "inspect-message", type = 3, target_id = "7",
            resolved = new
            {
                messages = new Dictionary<string, object>
                {
                    ["7"] = new
                    {
                        id = "7", type = 0, channel_id = "4",
                        author = new { id = "6", username = "author", discriminator = "0001", avatar = (string)null },
                        content = "resolved message", timestamp = DateTimeOffset.UtcNow, edited_timestamp = (DateTimeOffset?)null,
                        tts = false, mention_everyone = false, mentions = Array.Empty<object>(), mention_roles = Array.Empty<object>(),
                        attachments = Array.Empty<object>(), embeds = Array.Empty<object>(), pinned = false, components = Array.Empty<object>()
                    }
                }
            }
        };
        var response = await PostSignedAsync(CommandPayload(2, data));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        Assert.Contains("resolved message", body);
    }

    [Fact]
    public async Task FollowupAfterDeferUsesNormalRestApi()
    {
        var response = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "followup", type = 1 }));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, JsonDocument.Parse(body).RootElement.GetProperty("type").GetInt32());
        var failure = _results.OfType<ExecuteResult>().FirstOrDefault(result => !result.IsSuccess).Exception;
        Assert.True(_restClient.Endpoints.Any(endpoint => endpoint.Contains("webhooks/2/interaction-token?wait=true")), failure?.ToString());
    }

    [Fact]
    public async Task ComponentsV2FlagIsAddedToInitialResponsesAndFollowups()
    {
        var initial = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "components-v2", type = 1 }));
        var initialJson = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
        var initialFlags = (MessageFlags)initialJson.RootElement.GetProperty("data").GetProperty("flags").GetInt32();
        Assert.True(initialFlags.HasFlag(MessageFlags.ComponentsV2));

        var followup = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "components-v2-followup", type = 1 }));
        Assert.Equal(HttpStatusCode.OK, followup.StatusCode);
        var followupPayload = _restClient.JsonPayloads.Last(payload => payload.Contains("components-v2-followup"));
        var followupFlags = (MessageFlags)JsonDocument.Parse(followupPayload).RootElement.GetProperty("flags").GetInt32();
        Assert.True(followupFlags.HasFlag(MessageFlags.ComponentsV2));
    }

    [Fact]
    public async Task ConcurrentFollowupsUseApplicationIdFromEachInteraction()
    {
        var first = PostSignedAsync(CommandPayload(2, new { id = "3", name = "followup", type = 1 }, "20"));
        var second = PostSignedAsync(CommandPayload(2, new { id = "3", name = "followup", type = 1 }, "21"));

        await Task.WhenAll(first, second);

        Assert.Contains(_restClient.Endpoints, endpoint => endpoint.Contains("webhooks/20/interaction-token?wait=true"));
        Assert.Contains(_restClient.Endpoints, endpoint => endpoint.Contains("webhooks/21/interaction-token?wait=true"));
    }

    [Fact]
    public async Task RequestCancellationIsPropagatedToCommand()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _client.SendAsync(CreateSignedRequest(CommandPayload(2, new { id = "3", name = "wait", type = 1 })), cancellation.Token));
    }

    [Fact]
    public async Task ExplicitAsyncRunModeIsRejectedBeforeBackgroundExecution()
    {
        AsyncRunModeModule.ExecutionCount = 0;
        await _host.Services.GetRequiredService<InteractionService>().AddModuleAsync<AsyncRunModeModule>(_host.Services);

        var synchronous = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "ping", type = 1 }));
        Assert.Contains("Pong!", await synchronous.Content.ReadAsStringAsync());

        var response = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "background", type = 1 }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Task.Delay(50);
        Assert.Equal(0, AsyncRunModeModule.ExecutionCount);
    }

    [Fact]
    public async Task ModalCanBeOpenedAndSubmitted()
    {
        var open = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "modal", type = 1 }));
        Assert.Contains("test-modal", await open.Content.ReadAsStringAsync());

        var data = new
        {
            custom_id = "test-modal",
            components = new[] { new { type = 1, components = new[] { new { type = 4, custom_id = "value", value = "submitted" } } } }
        };
        var submit = await PostSignedAsync(CommandPayload(5, data));
        Assert.Contains("submitted", await submit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PreconditionsAreApplied()
    {
        var accepted = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "allowed", type = 1 }));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Contains("allowed", await accepted.Content.ReadAsStringAsync());

        var rejected = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "denied", type = 1 }));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.DoesNotContain("secret rejection detail", await rejected.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ScopedServiceComesFromHttpRequestScope()
    {
        var first = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "scope", type = 1 }));
        var second = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "scope", type = 1 }));
        Assert.NotEqual(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SimultaneousRequestsKeepCallbacksIsolated()
    {
        var requests = Enumerable.Range(0, 8).Select(index => PostSignedAsync(
            CommandPayload(2, new { id = "3", name = "echo", type = 1,
                options = new[] { new { name = "value", type = 3, value = index.ToString() } } })));
        var responses = await Task.WhenAll(requests);
        var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()));
        for (var index = 0; index < bodies.Length; index++)
            Assert.Contains(index.ToString(), bodies[index]);
    }

    [Fact]
    public async Task DuplicateInitialResponseIsRejectedWithoutReplacingFirstResponse()
    {
        var response = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "double", type = 1 }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("first", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CommandExceptionIsGenericAndCommandWithoutResponseIsDetected()
    {
        var exception = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "throws", type = 1 }));
        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.DoesNotContain("private exception", await exception.Content.ReadAsStringAsync());

        var noResponse = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "silent", type = 1 }));
        Assert.Equal(HttpStatusCode.OK, noResponse.StatusCode);
    }

    [Fact]
    public async Task OversizedInitialResponseIsReplacedWithAValidErrorResponse()
    {
        var response = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "oversized", type = 1 }));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(4, JsonDocument.Parse(body).RootElement.GetProperty("type").GetInt32());
        Assert.DoesNotContain(new string('x', DiscordConfig.MaxMessageSize + 1), body);
    }

    [Fact]
    public async Task UnknownCommandAndInvalidConversionUseSafeErrorHandler()
    {
        var unknown = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "missing", type = 1 }));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.DoesNotContain("No command found", await unknown.Content.ReadAsStringAsync());

        var invalid = await PostSignedAsync(CommandPayload(2, new
        {
            id = "3", name = "number", type = 1,
            options = new[] { new { name = "value", type = 3, value = "not-a-number" } }
        }));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.DoesNotContain("not-a-number", await invalid.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownMessageCommandWithoutRegisteredMessageModulesReturnsAnInteractionResponse()
    {
        using var isolatedHost = await new HostBuilder()
            .ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddSingleton(_discordClient);
                    services.AddDiscordHttpInteractions(options => options.PublicKey = _publicKey);
                })
                .Configure(app => app.UseRouting()
                    .UseEndpoints(endpoints => endpoints.MapDiscordInteractions("/interactions"))))
            .StartAsync();

        using var client = isolatedHost.GetTestClient();
        var interactionService = isolatedHost.Services.GetRequiredService<InteractionService>();
        var eventInvoked = false;
        interactionService.ContextCommandExecuted += (_, context, result) =>
        {
            eventInvoked = true;
            return result.Error == InteractionCommandError.UnknownCommand
                ? context.Interaction.RespondAsync("handled unknown context command", ephemeral: true)
                : Task.CompletedTask;
        };

        var data = new
        {
            id = "3", name = "Message", type = 3, target_id = "7",
            resolved = new
            {
                messages = new Dictionary<string, object>
                {
                    ["7"] = new
                    {
                        id = "7", type = 0, channel_id = "4",
                        author = new { id = "6", username = "author", discriminator = "0001", avatar = (string)null },
                        content = "message", timestamp = DateTimeOffset.UtcNow, edited_timestamp = (DateTimeOffset?)null,
                        tts = false, mention_everyone = false, mentions = Array.Empty<object>(), mention_roles = Array.Empty<object>(),
                        attachments = Array.Empty<object>(), embeds = Array.Empty<object>(), pinned = false, components = Array.Empty<object>()
                    }
                }
            }
        };

        var response = await client.SendAsync(CreateSignedRequest(CommandPayload(2, data)));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(4, JsonDocument.Parse(body).RootElement.GetProperty("type").GetInt32());
        Assert.True(eventInvoked);
        Assert.Contains("handled unknown context command", body);
        await isolatedHost.StopAsync();
    }

    [Fact]
    public async Task InteractionExecutedHandlerCanRespondThroughIDiscordInteraction()
    {
        var service = _host.Services.GetRequiredService<InteractionService>();

        Task HandleAsync(SlashCommandInfo _, IInteractionContext context, Discord.Interactions.IResult result)
            => result.Error == InteractionCommandError.UnknownCommand
                ? context.Interaction.RespondAsync("handled by event", ephemeral: true)
                : Task.CompletedTask;

        service.SlashCommandExecuted += HandleAsync;
        try
        {
            var response = await PostSignedAsync(CommandPayload(2, new { id = "3", name = "missing", type = 1 }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("handled by event", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            service.SlashCommandExecuted -= HandleAsync;
        }
    }

    [Fact]
    public void InvalidOptionsAreRejected()
    {
        var validator = new DiscordHttpInteractionOptionsValidator();
        Assert.True(validator.Validate(null, new DiscordHttpInteractionOptions { PublicKey = "invalid" }).Failed);
        Assert.True(validator.Validate(null, new DiscordHttpInteractionOptions
        {
            PublicKey = new string('a', 64), MaximumRequestBodySize = 0
        }).Failed);
    }

    [Fact]
    public void InteractionServiceConfigurationIsAppliedAndHttpInvariantsAreEnforced()
    {
        var services = new ServiceCollection();
        var configured = false;
        services.AddDiscordHttpInteractions(options => options.PublicKey = new string('a', 64), interactionService =>
        {
            configured = true;
            interactionService.LogLevel = LogSeverity.Verbose;
            interactionService.UseCompiledLambda = true;
        });

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<InteractionService>();
        Assert.True(configured);

        var invalidServices = new ServiceCollection();
        invalidServices.AddDiscordHttpInteractions(options => options.PublicKey = new string('a', 64), interactionService =>
            interactionService.AutoServiceScopes = true);
        using var invalidProvider = invalidServices.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => invalidProvider.GetRequiredService<InteractionService>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitRestClientRegistrationWinsRegardlessOfRegistrationOrder(bool restClientFirst)
    {
        var services = new ServiceCollection();
        var configured = false;

        void AddRestClient() => services.AddDiscordNetRestClient(config =>
        {
            configured = true;
            config.LogLevel = LogSeverity.Verbose;
        });

        void AddHttpInteractions() => services.AddDiscordHttpInteractions(options =>
            options.PublicKey = new string('a', 64));

        if (restClientFirst)
        {
            AddRestClient();
            AddHttpInteractions();
        }
        else
        {
            AddHttpInteractions();
            AddRestClient();
        }

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<DiscordRestClient>();
        var second = provider.GetRequiredService<DiscordRestClient>();

        Assert.True(configured);
        Assert.Same(first, second);
    }

    private Task<HttpResponseMessage> PostSignedAsync(string json) => _client.SendAsync(CreateSignedRequest(json));

    private HttpRequestMessage CreateSignedRequest(string json)
    {
        const string timestamp = "1750000000";
        var body = Encoding.UTF8.GetBytes(json);
        var message = Encoding.UTF8.GetBytes(timestamp).Concat(body).ToArray();
        var signature = Convert.ToHexString(_algorithm.Sign(_key, message)).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, "/interactions")
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Signature-Ed25519", signature);
        request.Headers.Add("X-Signature-Timestamp", timestamp);
        return request;
    }

    private static string PingPayload() => JsonSerializer.Serialize(new
    {
        id = CurrentSnowflake(), application_id = "2", type = 1, token = "interaction-token", version = 1,
        entitlements = Array.Empty<object>(), authorizing_integration_owners = new { }, app_permissions = "0",
        attachment_size_limit = 10485760
    });

    private static string CommandPayload(int type, object data, string applicationId = "2") => JsonSerializer.Serialize(new
    {
        id = CurrentSnowflake(), application_id = applicationId, type, data, channel_id = "4", token = "interaction-token", version = 1,
        locale = "en-US",
        user = new { id = "5", username = "tester", discriminator = "0001", avatar = (string)null },
        entitlements = Array.Empty<object>(), authorizing_integration_owners = new { }, app_permissions = "0",
        attachment_size_limit = 10485760
    });

    private static string CurrentSnowflake() => SnowflakeUtils.ToSnowflake(DateTimeOffset.UtcNow).ToString();

    public sealed class ScopedMarker { public Guid Id { get; } = Guid.NewGuid(); }

    public sealed class TestModule : RestInteractionModuleBase<IRestInteractionContext>
    {
        private readonly ScopedMarker _marker;
        private readonly IHttpContextAccessor _httpContextAccessor;
        public TestModule(ScopedMarker marker, IHttpContextAccessor httpContextAccessor)
        {
            _marker = marker;
            _httpContextAccessor = httpContextAccessor;
        }

        [SlashCommand("ping", "ping")]
        public Task PingAsync() => RespondAsync("Pong!");

        [SlashCommand("echo", "echo")]
        public Task EchoAsync(string value) => RespondAsync(value);

        [SlashCommand("number", "number")]
        public Task NumberAsync(int value) => RespondAsync(value.ToString());

        [SlashCommand("search", "search")]
        public Task SearchAsync(string value) => RespondAsync(value);

        [AutocompleteCommand("value", "search")]
        public Task AutocompleteAsync()
        {
            var interaction = (RestAutocompleteInteraction)Context.Interaction;
            var payload = interaction.Respond(new[] { new AutocompleteResult("answer", "answer") });
            return Context.InteractionResponseCallback(payload);
        }

        [UserCommand("inspect-user")]
        public Task InspectUserAsync(IUser user) => RespondAsync(user.Username);

        [MessageCommand("inspect-message")]
        public Task InspectMessageAsync(IMessage message) => RespondAsync(message.Content);

        [SlashCommand("followup", "followup")]
        public async Task SendFollowupAsync()
        {
            await DeferAsync();
            await FollowupAsync("later");
        }

        [SlashCommand("components-v2", "components-v2")]
        public Task ComponentsV2Async()
        {
            var components = new ComponentBuilderV2([new ContainerBuilder().WithTextDisplay("components-v2")]);
            return RespondAsync(components: components.Build());
        }

        [SlashCommand("components-v2-followup", "components-v2-followup")]
        public async Task ComponentsV2FollowupAsync()
        {
            await DeferAsync();
            var components = new ComponentBuilderV2([new ContainerBuilder().WithTextDisplay("components-v2-followup")]);
            await FollowupAsync(components: components.Build());
        }

        [SlashCommand("wait", "wait")]
        public Task WaitAsync() => Task.Delay(Timeout.Infinite, _httpContextAccessor.HttpContext.RequestAborted);

        [SlashCommand("scope", "scope")]
        public Task ScopeAsync() => RespondAsync(_marker.Id.ToString());

        [SlashCommand("modal", "modal")]
        public Task ModalAsync() => RespondWithModalAsync<TestModal>("test-modal");

        [ModalInteraction("test-modal")]
        public Task ModalSubmittedAsync(TestModal modal) => RespondAsync(modal.Value);

        [ComponentInteraction("button")]
        public Task ButtonAsync() => RespondAsync("button");

        [ComponentInteraction("select")]
        public Task SelectAsync(string[] values) => RespondAsync(values[0]);

        [SlashCommand("allowed", "allowed")]
        [TestPrecondition(true)]
        public Task AllowedAsync() => RespondAsync("allowed");

        [SlashCommand("denied", "denied")]
        [TestPrecondition(false)]
        public Task DeniedAsync() => RespondAsync("denied");

        [SlashCommand("double", "double")]
        public async Task DoubleAsync()
        {
            await RespondAsync("first");
            await RespondAsync("second");
        }

        [SlashCommand("throws", "throws")]
        public Task ThrowsAsync() => throw new InvalidOperationException("private exception");

        [SlashCommand("silent", "silent")]
        public Task SilentAsync() => Task.CompletedTask;

        [SlashCommand("oversized", "oversized")]
        public Task OversizedAsync() => RespondAsync(new string('x', DiscordConfig.MaxMessageSize + 1));
    }

    public sealed class TestModal : IModal
    {
        public string Title => "Test";
        [InputLabel("Value")]
        [ModalTextInput("value")]
        public string Value { get; set; }
    }

    public sealed class AsyncRunModeModule : RestInteractionModuleBase<IRestInteractionContext>
    {
        public static int ExecutionCount;

        [SlashCommand("background", "background", runMode: RunMode.Async)]
        public Task BackgroundAsync()
        {
            Interlocked.Increment(ref ExecutionCount);
            return RespondAsync("background");
        }
    }

    public sealed class CustomContextModule : RestInteractionModuleBase<TestRestInteractionContext>
    {
        [SlashCommand("custom-context", "custom-context")]
        public Task CustomContextAsync() => RespondAsync(Context.Marker);
    }

    public sealed class TestRestInteractionContext : RestInteractionContext<RestInteraction>
    {
        public string Marker => "custom";

        public TestRestInteractionContext(DiscordRestClient client, RestInteraction interaction,
            Func<string, Task> interactionResponseCallback)
            : base(client, interaction, interactionResponseCallback) { }
    }

    private sealed class TestPreconditionAttribute : PreconditionAttribute
    {
        private readonly bool _allowed;
        public TestPreconditionAttribute(bool allowed) => _allowed = allowed;
        public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context,
            ICommandInfo commandInfo, IServiceProvider services)
            => Task.FromResult(_allowed ? PreconditionResult.FromSuccess() :
                PreconditionResult.FromError("secret rejection detail"));
    }

    private sealed class FakeRestClient : global::Discord.Net.Rest.IRestClient
    {
        public ConcurrentBag<string> Endpoints { get; } = new();
        public ConcurrentBag<string> JsonPayloads { get; } = new();
        public void Dispose() { }
        public void SetHeader(string key, string value) { }
        public void SetCancelToken(CancellationToken cancelToken) { }

        public Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken cancelToken,
            bool headerOnly = false, string reason = null,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders = null)
            => RespondAsync(endpoint);

        public Task<RestResponse> SendAsync(string method, string endpoint, string json, CancellationToken cancelToken,
            bool headerOnly = false, string reason = null,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders = null)
        {
            JsonPayloads.Add(json);
            return RespondAsync(endpoint);
        }

        public Task<RestResponse> SendAsync(string method, string endpoint, IReadOnlyDictionary<string, object> multipartParams,
            CancellationToken cancelToken, bool headerOnly = false, string reason = null,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders = null)
            => RespondAsync(endpoint);

        private Task<RestResponse> RespondAsync(string endpoint)
        {
            Endpoints.Add(endpoint);
            var json = JsonSerializer.Serialize(new
            {
                id = "8", type = 0, channel_id = "4",
                author = new { id = "2", username = "bot", discriminator = "0001", avatar = (string)null },
                content = "later", timestamp = DateTimeOffset.UtcNow, edited_timestamp = (DateTimeOffset?)null,
                tts = false, mention_everyone = false, mentions = Array.Empty<object>(), mention_roles = Array.Empty<object>(),
                attachments = Array.Empty<object>(), embeds = Array.Empty<object>(), pinned = false, components = Array.Empty<object>()
            });
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return Task.FromResult(new RestResponse(HttpStatusCode.OK, new Dictionary<string, string>(), stream));
        }
    }

}
