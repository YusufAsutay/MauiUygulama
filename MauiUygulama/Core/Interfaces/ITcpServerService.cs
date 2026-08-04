using MauiUygulama.Models;

namespace MauiUygulama.Core.Interfaces;

public interface ITcpServerService : IAsyncDisposable
{
    bool IsRunning { get; }

    int ConnectedClientCount { get; }

    event Action<bool>? ServerStateChanged;

    event Action<Guid, string>? ClientConnected;

    event Action<Guid, string>? ClientDisconnected;

    event Action<Guid, ReadOnlyMemory<byte>>? DataReceived;

    event Action<Exception>? ServerError;

    Task StartAsync(
        TcpServerSettings settings,
        CancellationToken cancellationToken = default);

    Task StopAsync();

    Task SendAsync(
        Guid clientId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);
}