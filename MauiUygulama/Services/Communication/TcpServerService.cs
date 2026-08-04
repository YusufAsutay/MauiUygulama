using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Communication;

public sealed class TcpServerService : ITcpServerService
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private readonly ConcurrentDictionary<Guid, ClientSession> _clients = new();

    private readonly ConcurrentDictionary<Guid, Task> _clientTasks = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _serverCancellation;
    private Task? _acceptLoopTask;

    private int _receiveBufferSize = 4096;
    private bool _isRunning;
    private bool _isDisposed;

    public bool IsRunning => _isRunning;

    public int ConnectedClientCount => _clients.Count;

    public event Action<bool>? ServerStateChanged;

    public event Action<Guid, string>? ClientConnected;

    public event Action<Guid, string>? ClientDisconnected;

    public event Action<Guid, ReadOnlyMemory<byte>>? DataReceived;

    public event Action<Exception>? ServerError;

    public async Task StartAsync(
        TcpServerSettings settings,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);

        ValidateSettings(settings);

        await _lifecycleLock.WaitAsync(cancellationToken);

        try
        {
            if (_isRunning)
            {
                throw new InvalidOperationException(
                    "TCP sunucusu zaten çalışıyor.");
            }

            if (!IPAddress.TryParse(
                    settings.IpAddress,
                    out IPAddress? listenAddress))
            {
                throw new ArgumentException(
                    "Geçerli bir dinleme IP adresi girilmelidir.",
                    nameof(settings));
            }

            var listener = new TcpListener(
                listenAddress,
                settings.Port);

            try
            {
                listener.Start(settings.Backlog);

                var serverCancellation =
                    new CancellationTokenSource();

                _listener = listener;
                _serverCancellation = serverCancellation;
                _receiveBufferSize = settings.ReceiveBufferSize;

                SetServerState(true);

                _acceptLoopTask = AcceptClientsAsync(
                    listener,
                    serverCancellation.Token);
            }
            catch
            {
                listener.Stop();

                _listener = null;
                _serverCancellation = null;
                _acceptLoopTask = null;

                SetServerState(false);

                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();

        try
        {
            if (!_isRunning)
            {
                return;
            }

            _serverCancellation?.Cancel();
            _listener?.Stop();

            if (_acceptLoopTask is not null)
            {
                try
                {
                    await _acceptLoopTask;
                }
                catch (OperationCanceledException)
                {
                    // Sunucu durdurulurken beklenen iptal.
                }
            }

            foreach (ClientSession session in _clients.Values)
            {
                session.Client.Dispose();
            }

            Task[] clientTasks = _clientTasks.Values.ToArray();

            if (clientTasks.Length > 0)
            {
                try
                {
                    await Task.WhenAll(clientTasks);
                }
                catch (Exception exception)
                {
                    NotifyServerError(exception);
                }
            }

            _clients.Clear();
            _clientTasks.Clear();

            _serverCancellation?.Dispose();

            _listener = null;
            _serverCancellation = null;
            _acceptLoopTask = null;

            SetServerState(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task SendAsync(
        Guid clientId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_isRunning)
        {
            throw new InvalidOperationException(
                "TCP sunucusu çalışmıyor.");
        }

        if (data.IsEmpty)
        {
            throw new ArgumentException(
                "Gönderilecek veri boş olamaz.",
                nameof(data));
        }

        if (!_clients.TryGetValue(
                clientId,
                out ClientSession? session))
        {
            throw new InvalidOperationException(
                "Belirtilen istemci bağlı değil.");
        }

        await session.SendLock.WaitAsync(cancellationToken);

        try
        {
            NetworkStream stream =
                session.Client.GetStream();

            await stream.WriteAsync(
                data,
                cancellationToken);
        }
        finally
        {
            session.SendLock.Release();
        }
    }

    private async Task AcceptClientsAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                TcpClient tcpClient =
                    await listener.AcceptTcpClientAsync(
                        cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    tcpClient.Dispose();
                    break;
                }

                tcpClient.NoDelay = true;

                Guid clientId = Guid.NewGuid();

                var session = new ClientSession(
                    clientId,
                    tcpClient);

                if (!_clients.TryAdd(clientId, session))
                {
                    tcpClient.Dispose();
                    continue;
                }

                ClientConnected?.Invoke(
                    clientId,
                    session.RemoteEndPoint);

                Task clientTask = HandleClientAsync(
                    session,
                    cancellationToken);

                _clientTasks[clientId] = clientTask;

                _ = RemoveCompletedClientTaskAsync(
                    clientId,
                    clientTask);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                NotifyServerError(exception);
            }
        }
    }

    private async Task HandleClientAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[_receiveBufferSize];

        try
        {
            NetworkStream stream =
                session.Client.GetStream();

            while (!cancellationToken.IsCancellationRequested)
            {
                int receivedByteCount =
                    await stream.ReadAsync(
                        buffer.AsMemory(),
                        cancellationToken);

                if (receivedByteCount == 0)
                {
                    break;
                }

                byte[] receivedData =
                    buffer.AsSpan(
                        0,
                        receivedByteCount)
                    .ToArray();

                DataReceived?.Invoke(
                    session.ClientId,
                    receivedData);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Sunucu durdurulurken beklenen iptal.
        }
        catch (ObjectDisposedException)
            when (cancellationToken.IsCancellationRequested)
        {
            // İstemci sunucu durdurulurken kapatıldı.
        }
        catch (Exception exception)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                NotifyServerError(exception);
            }
        }
        finally
        {
            if (_clients.TryRemove(
                    session.ClientId,
                    out ClientSession? removedSession))
            {
                removedSession.Client.Dispose();

                ClientDisconnected?.Invoke(
                    removedSession.ClientId,
                    removedSession.RemoteEndPoint);
            }
        }
    }

    private async Task RemoveCompletedClientTaskAsync(
        Guid clientId,
        Task clientTask)
    {
        try
        {
            await clientTask;
        }
        finally
        {
            _clientTasks.TryRemove(
                clientId,
                out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await StopAsync();

        _isDisposed = true;
        _lifecycleLock.Dispose();

        GC.SuppressFinalize(this);
    }

    private void SetServerState(bool isRunning)
    {
        if (_isRunning == isRunning)
        {
            return;
        }

        _isRunning = isRunning;
        ServerStateChanged?.Invoke(isRunning);
    }

    private void NotifyServerError(Exception exception)
    {
        ServerError?.Invoke(exception);
    }

    private static void ValidateSettings(
        TcpServerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.IpAddress))
        {
            throw new ArgumentException(
                "Dinleme IP adresi boş olamaz.",
                nameof(settings));
        }

        if (settings.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Port numarası 1 ile 65535 arasında olmalıdır.");
        }

        if (settings.Backlog <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Backlog sıfırdan büyük olmalıdır.");
        }

        if (settings.ReceiveBufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Alma tamponu boyutu sıfırdan büyük olmalıdır.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(
                nameof(TcpServerService));
        }
    }

    private sealed class ClientSession
    {
        public ClientSession(
            Guid clientId,
            TcpClient client)
        {
            ClientId = clientId;
            Client = client;

            RemoteEndPoint =
                client.Client.RemoteEndPoint?.ToString()
                ?? "Bilinmeyen istemci";
        }

        public Guid ClientId { get; }

        public TcpClient Client { get; }

        public string RemoteEndPoint { get; }

        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }
}  