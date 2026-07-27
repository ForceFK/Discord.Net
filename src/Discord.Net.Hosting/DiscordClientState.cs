using System;
using System.Threading;
using System.Threading.Tasks;

namespace Discord.Hosting;

/// <summary>Observable lifecycle and readiness state for the hosted Discord client.</summary>
public sealed class DiscordClientState
{
    private TaskCompletionSource _ready = NewCompletionSource();
    private Exception _lastError;
    private int _started;
    private int _readyFlag;
    private int _readyTimedOut;
    private int _connectionState = (int)ConnectionState.Disconnected;

    /// <summary>Gets whether client startup completed.</summary>
    public bool IsStarted => Volatile.Read(ref _started) != 0;

    /// <summary>Gets whether the gateway emitted readiness.</summary>
    public bool IsReady => Volatile.Read(ref _readyFlag) != 0;

    /// <summary>Gets whether the configured readiness wait timed out.</summary>
    public bool ReadyTimedOut => Volatile.Read(ref _readyTimedOut) != 0;

    /// <summary>Gets the most recent startup or shutdown error.</summary>
    public Exception LastError => Volatile.Read(ref _lastError);

    /// <summary>Gets the last observed gateway connection state.</summary>
    public ConnectionState ConnectionState => (ConnectionState)Volatile.Read(ref _connectionState);

    /// <summary>Waits until readiness is observed.</summary>
    public Task WaitForReadyAsync(CancellationToken cancellationToken = default)
        => _ready.Task.WaitAsync(cancellationToken);

    internal void Starting()
    {
        Volatile.Write(ref _lastError, null);
        Volatile.Write(ref _started, 0);
        Volatile.Write(ref _readyFlag, 0);
        Volatile.Write(ref _readyTimedOut, 0);
        Volatile.Write(ref _connectionState, (int)ConnectionState.Connecting);
        if (_ready.Task.IsCompleted)
            Interlocked.Exchange(ref _ready, NewCompletionSource());
    }

    internal void Started(ConnectionState connectionState)
    {
        Volatile.Write(ref _started, 1);
        Volatile.Write(ref _connectionState, (int)connectionState);
    }

    internal void Ready()
    {
        Volatile.Write(ref _readyFlag, 1);
        Volatile.Write(ref _connectionState, (int)ConnectionState.Connected);
        _ready.TrySetResult();
    }

    internal void TimedOut() => Volatile.Write(ref _readyTimedOut, 1);

    internal void Failed(Exception exception) => Volatile.Write(ref _lastError, exception);

    internal void Stopped()
    {
        Volatile.Write(ref _started, 0);
        Volatile.Write(ref _readyFlag, 0);
        Volatile.Write(ref _connectionState, (int)ConnectionState.Disconnected);
    }

    private static TaskCompletionSource NewCompletionSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
