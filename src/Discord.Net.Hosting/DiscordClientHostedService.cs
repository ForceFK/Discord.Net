using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Discord.Hosting;

internal class DiscordClientHostedService : IHostedService
{
    private readonly IDiscordGatewayClient _client;
    private readonly DiscordHostingOptions _options;
    private readonly DiscordClientState _state;
    private readonly DiscordLogBridge _logBridge;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private bool _running;

    internal DiscordClientHostedService(IDiscordGatewayClient client, IOptions<DiscordHostingOptions> options, DiscordClientState state, ILogger logger)
    {
        _client = client;
        _options = options.Value;
        _state = state;
        _logBridge = new DiscordLogBridge(logger);
    }

    private Task OnReadyAsync()
    {
        _state.Ready();
        return Task.CompletedTask;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_running)
                return;

            _state.Starting();
            _client.Log += _logBridge.WriteAsync;
            _client.Ready += OnReadyAsync;
            try
            {
                await _client.LoginAsync(_options.TokenType, _options.Token).WaitAsync(cancellationToken).ConfigureAwait(false);
                await _client.StartAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                _running = true;
                _state.Started(_client.ConnectionState);

                if (_options.WaitForReady)
                {
                    using var timeout = new CancellationTokenSource(_options.ReadyTimeout);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                    try
                    {
                        await _state.WaitForReadyAsync(linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        _state.TimedOut();
                    }
                }
            }
            catch (Exception exception)
            {
                _state.Failed(exception);
                await CleanupAfterFailedStartAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_running)
                return;

            try
            {
                await _client.StopAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                await _client.LogoutAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _state.Failed(exception);
                throw;
            }
            finally
            {
                _running = false;
                _state.Stopped();
                Unsubscribe();
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task CleanupAfterFailedStartAsync()
    {
        try { await _client.StopAsync().ConfigureAwait(false); } catch { }
        try { await _client.LogoutAsync().ConfigureAwait(false); } catch { }
        _running = false;
        _state.Stopped();
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        _client.Ready -= OnReadyAsync;
        _client.Log -= _logBridge.WriteAsync;
    }
}
