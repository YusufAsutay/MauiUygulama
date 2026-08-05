using System.Net.Sockets;
using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Communication;

public sealed class TcpClientService : ITcpClientService
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _receiveLock = new(1, 1);

    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;

    private bool _isConnected;
    private bool _isDisposed;

    public bool IsConnected => _isConnected;

    public event Action<bool>? ConnectionStateChanged;

    public async Task ConnectAsync(
        TcpConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSettings(settings);

        await _connectionLock.WaitAsync(cancellationToken);

        try
        {
            await DisconnectCoreAsync();

            var tcpClient = new TcpClient();

            using var timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            timeoutCancellation.CancelAfter(
                settings.ConnectionTimeoutMilliseconds);

            try
            {
                await tcpClient.ConnectAsync(
                    settings.IpAddress,
                    settings.Port,
                    timeoutCancellation.Token);

                _tcpClient = tcpClient;
                _networkStream = tcpClient.GetStream();

                SetConnectionState(true);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                tcpClient.Dispose();

                throw new TimeoutException(
                    $"{settings.IpAddress}:{settings.Port} adresine " +
                    $"{settings.ConnectionTimeoutMilliseconds} ms içinde " +
                    "bağlantı kurulamadı.");
            }
            catch
            {
                tcpClient.Dispose();
                SetConnectionState(false);
                throw;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();

        await _connectionLock.WaitAsync();

        try
        {
            await DisconnectCoreAsync();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task SendAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (data.IsEmpty)
        {
            throw new ArgumentException(
                "Gönderilecek veri boş olamaz.",
                nameof(data));
        }

        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            NetworkStream stream = GetConnectedStream();
            await stream.WriteAsync(data, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Kullanıcının veya sayfanın isteği iptal etmesi ağ hatası değildir.
            throw;
        }
        catch
        {
            SetConnectionState(false);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<int> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (buffer.IsEmpty)
        {
            throw new ArgumentException(
                "Okuma tamponu boş olamaz.",
                nameof(buffer));
        }

        await _receiveLock.WaitAsync(cancellationToken);

        try
        {
            NetworkStream stream = GetConnectedStream();

            int receivedByteCount =
                await stream.ReadAsync(buffer, cancellationToken);

            // Sıfır byte okunması karşı tarafın bağlantıyı kapattığını gösterir.
            if (receivedByteCount == 0)
            {
                SetConnectionState(false);
            }

            return receivedByteCount;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Master sayfasından çıkılırken receive-loop iptal edilir.
            // Bu normal iptal TCP bağlantısının koptuğu anlamına gelmez.
            throw;
        }
        catch
        {
            // Yalnızca gerçek ağ/stream hataları bağlantıyı kopmuş sayar.
            SetConnectionState(false);
            throw;
        }
        finally
        {
            _receiveLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await _connectionLock.WaitAsync();

        try
        {
            if (_isDisposed)
            {
                return;
            }

            await DisconnectCoreAsync();
            _isDisposed = true;
        }
        finally
        {
            _connectionLock.Release();
        }

        _connectionLock.Dispose();
        _sendLock.Dispose();
        _receiveLock.Dispose();

        GC.SuppressFinalize(this);
    }

    private async Task DisconnectCoreAsync()
    {
        NetworkStream? stream = _networkStream;
        TcpClient? client = _tcpClient;

        _networkStream = null;
        _tcpClient = null;

        if (stream is not null)
        {
            await stream.DisposeAsync();
        }

        client?.Dispose();
        SetConnectionState(false);
    }

    private NetworkStream GetConnectedStream()
    {
        if (!_isConnected ||
            _tcpClient is null ||
            _networkStream is null)
        {
            throw new InvalidOperationException(
                "TCP bağlantısı kurulmamış.");
        }

        return _networkStream;
    }

    private void SetConnectionState(bool isConnected)
    {
        if (_isConnected == isConnected)
        {
            return;
        }

        _isConnected = isConnected;
        ConnectionStateChanged?.Invoke(isConnected);
    }

    private static void ValidateSettings(
        TcpConnectionSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.IpAddress))
        {
            throw new ArgumentException(
                "IP adresi boş olamaz.",
                nameof(settings));
        }

        if (settings.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Port numarası 1 ile 65535 arasında olmalıdır.");
        }

        if (settings.ConnectionTimeoutMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Bağlantı zaman aşımı sıfırdan büyük olmalıdır.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(
                nameof(TcpClientService));
        }
    }
}