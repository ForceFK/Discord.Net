using Discord.Hosting;
using Discord.Logging;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Discord.Net.Hosting.Tests;

public sealed class DiscordClientHostedServiceTests
{
    [Fact]
    public async Task StartsAndStopsGracefully()
    {
        var client = new FakeGatewayClient();
        var state = new DiscordClientState();
        var service = CreateService(client, state);

        await service.StartAsync(default);
        await service.StopAsync(default);

        Assert.Equal(1, client.LoginCount);
        Assert.Equal(1, client.StartCount);
        Assert.Equal(1, client.StopCount);
        Assert.Equal(1, client.LogoutCount);
        Assert.False(state.IsStarted);
        Assert.Equal(ConnectionState.Disconnected, state.ConnectionState);
    }

    [Fact]
    public async Task MultipleStartAndStopCallsAreIdempotent()
    {
        var client = new FakeGatewayClient();
        var service = CreateService(client, new DiscordClientState());

        await service.StartAsync(default);
        await service.StartAsync(default);
        await service.StopAsync(default);
        await service.StopAsync(default);

        Assert.Equal(1, client.LoginCount);
        Assert.Equal(1, client.StartCount);
        Assert.Equal(1, client.StopCount);
        Assert.Equal(1, client.LogoutCount);
    }

    [Fact]
    public async Task CancellationDuringStartupIsPropagatedAndCleanedUp()
    {
        var login = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeGatewayClient { LoginTask = login.Task };
        var state = new DiscordClientState();
        var service = CreateService(client, state);
        using var cancellation = new CancellationTokenSource();

        var starting = service.StartAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        Assert.Equal(1, client.StopCount);
        Assert.Equal(1, client.LogoutCount);
        Assert.False(state.IsStarted);
    }

    [Fact]
    public async Task AuthenticationFailureIsObservableAndRethrown()
    {
        var failure = new InvalidOperationException("authentication failed");
        var client = new FakeGatewayClient { LoginTask = Task.FromException(failure) };
        var state = new DiscordClientState();
        var service = CreateService(client, state);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(default));

        Assert.Same(failure, actual);
        Assert.Same(failure, state.LastError);
    }

    [Fact]
    public async Task WaitForReadyObservesReadyEvent()
    {
        var client = new FakeGatewayClient { RaiseReadyOnStart = true };
        var state = new DiscordClientState();
        var service = CreateService(client, state, waitForReady: true);

        await service.StartAsync(default);

        Assert.True(state.IsReady);
        Assert.False(state.ReadyTimedOut);
    }

    [Fact]
    public async Task ReadyWaitTimesOutWithoutBlockingHostIndefinitely()
    {
        var state = new DiscordClientState();
        var service = CreateService(new FakeGatewayClient(), state, waitForReady: true,
            readyTimeout: TimeSpan.FromMilliseconds(20));

        await service.StartAsync(default);

        Assert.True(state.IsStarted);
        Assert.False(state.IsReady);
        Assert.True(state.ReadyTimedOut);
    }

    [Fact]
    public async Task DiscordLogsAreForwardedToMicrosoftLogger()
    {
        var logger = new RecordingLogger();
        var client = new FakeGatewayClient();
        var service = CreateService(client, new DiscordClientState(), logger: logger);
        await service.StartAsync(default);

        await client.EmitLogAsync(new LogMessage(LogSeverity.Warning, "Gateway", "warning"));

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("warning"));
    }

    [Fact]
    public async Task DisabledDiscordLogLevelIsNotWritten()
    {
        var logger = new RecordingLogger { Enabled = false };
        var client = new FakeGatewayClient();
        var service = CreateService(client, new DiscordClientState(), logger: logger);
        await service.StartAsync(default);

        await client.EmitLogAsync(new LogMessage(LogSeverity.Warning, "Gateway", "ignored"));

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void RegistersNormalAndShardedClientsIndependently()
    {
        var socketServices = new ServiceCollection();
        socketServices.AddLogging();
        socketServices.AddDiscordNetHosting(options => options.Token = "token");
        socketServices.AddDiscordNetSocketClient();
        using var socketProvider = socketServices.BuildServiceProvider();
        Assert.NotNull(socketProvider.GetRequiredService<DiscordSocketClient>());
        Assert.Contains(socketProvider.GetServices<IHostedService>(), service => service is DiscordSocketClientHostedService);

        var shardedServices = new ServiceCollection();
        shardedServices.AddLogging();
        shardedServices.AddDiscordNetHosting(options => options.Token = "token");
        shardedServices.AddDiscordNetShardedClient(config => config.TotalShards = 1);
        using var shardedProvider = shardedServices.BuildServiceProvider();
        Assert.NotNull(shardedProvider.GetRequiredService<DiscordShardedClient>());
        Assert.Contains(shardedProvider.GetServices<IHostedService>(), service => service is DiscordShardedClientHostedService);
    }

    [Fact]
    public void InvalidHostingOptionsAreRejected()
    {
        var validator = new DiscordHostingOptionsValidator();
        Assert.True(validator.Validate(null, new DiscordHostingOptions()).Failed);
        Assert.True(validator.Validate(null, new DiscordHostingOptions
        {
            Token = "token", ReadyTimeout = TimeSpan.Zero
        }).Failed);
    }

    private static DiscordClientHostedService CreateService(FakeGatewayClient client, DiscordClientState state,
        bool waitForReady = false, TimeSpan? readyTimeout = null, ILogger logger = null)
        => new(client, Options.Create(new DiscordHostingOptions
        {
            Token = "not-a-real-token",
            WaitForReady = waitForReady,
            ReadyTimeout = readyTimeout ?? TimeSpan.FromSeconds(1)
        }), state, logger ?? new RecordingLogger());

    private sealed class FakeGatewayClient : IDiscordGatewayClient
    {
        public ConnectionState ConnectionState { get; set; } = ConnectionState.Connected;
        public event Func<LogMessage, Task> Log;
        public event Func<Task> Ready;
        public Task LoginTask { get; set; } = Task.CompletedTask;
        public bool RaiseReadyOnStart { get; set; }
        public int LoginCount { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int LogoutCount { get; private set; }

        public Task LoginAsync(TokenType tokenType, string token) { LoginCount++; return LoginTask; }
        public async Task StartAsync()
        {
            StartCount++;
            if (RaiseReadyOnStart && Ready is not null)
                await Ready.Invoke();
        }
        public Task StopAsync() { StopCount++; return Task.CompletedTask; }
        public Task LogoutAsync() { LogoutCount++; return Task.CompletedTask; }
        public Task EmitLogAsync(LogMessage message) => Log?.Invoke(message) ?? Task.CompletedTask;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public bool Enabled { get; set; } = true;
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => Enabled;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }
}
