using MauiUygulama.Models;

namespace MauiUygulama.Core.Interfaces;

public interface ITcpClientService : IAsyncDisposable
{
    bool IsConnected { get; }

    event Action<bool>? ConnectionStateChanged;

    Task ConnectAsync(
        TcpConnectionSettings settings,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    Task SendAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    Task<int> ReceiveAsync(
        Memory<byte> buffer,  //byte verisi için Memory<byte> kullanıldı 
        CancellationToken cancellationToken = default);
}