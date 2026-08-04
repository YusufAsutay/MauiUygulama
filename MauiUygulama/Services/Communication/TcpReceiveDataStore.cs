using System.Text;
using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Communication;

public sealed class TcpReceivedDataStore :
    ITcpReceivedDataStore
{
    private readonly ITcpServerService _tcpServer;
    private readonly object _itemsLock = new();
    private readonly List<ReceivedTcpDataItem> _items = new();

    private bool _isDisposed;

    public TcpReceivedDataStore(
        ITcpServerService tcpServer)
    {
        _tcpServer = tcpServer;
        _tcpServer.DataReceived += OnDataReceived;
    }

    public IReadOnlyList<ReceivedTcpDataItem> Items
    {
        get
        {
            lock (_itemsLock)
            {
                return _items.ToArray();
            }
        }
    }

    public event Action? DataChanged;

    public void Clear()
    {
        lock (_itemsLock)
        {
            _items.Clear();
        }

        DataChanged?.Invoke();
    }

    private void OnDataReceived(
        Guid clientId,
        ReadOnlyMemory<byte> data)
    {
        byte[] receivedBytes = data.ToArray();

        var item = new ReceivedTcpDataItem
        {
            ClientId = clientId,
            ClientName = clientId.ToString("N")[..8],
            ReceivedAt = DateTime.Now,
            Text = Encoding.UTF8.GetString(receivedBytes),
            Hex = BitConverter
                .ToString(receivedBytes)
                .Replace("-", " "),
            ByteCount = receivedBytes.Length
        };

        lock (_itemsLock)
        {
            _items.Insert(0, item);

            // Yalnızca son 100 veri kaydı tutulur.
            if (_items.Count > 100)
            {
                _items.RemoveAt(_items.Count - 1);
            }
        }

        DataChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _tcpServer.DataReceived -= OnDataReceived;
        _isDisposed = true;
    }
}