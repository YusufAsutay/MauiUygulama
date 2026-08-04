using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Modbus;

public sealed class ModbusFrameStore : IModbusFrameStore, IDisposable
{
    private const int MaximumStoredFrameCount = 100;

    private readonly IModbusTcpFrameAssembler _frameAssembler;
    private readonly object _syncRoot = new();
    private readonly List<ModbusFrameRecord> _items = new();

    private string? _lastError;
    private bool _isDisposed;

    public ModbusFrameStore(
        IModbusTcpFrameAssembler frameAssembler)
    {
        _frameAssembler = frameAssembler;

        _frameAssembler.FrameReceived += OnFrameReceived;
        _frameAssembler.FrameError += OnFrameError;
    }

    public IReadOnlyList<ModbusFrameRecord> Items
    {
        get
        {
            lock (_syncRoot)
            {
                return _items.ToArray();
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastError;
            }
        }
    }

    public event Action? DataChanged;

    public void Clear()
    {
        lock (_syncRoot)
        {
            _items.Clear();
            _lastError = null;
        }

        DataChanged?.Invoke();
    }

    private void OnFrameReceived(
        Guid clientId,
        ModbusTcpFrame frame)
    {
        ushort? startAddress = null;
        ushort? quantity = null;

        if (frame.FunctionCode == 0x03 && frame.Data.Length >= 4)
        {
            startAddress = ReadUInt16BigEndian(frame.Data, 0);
            quantity = ReadUInt16BigEndian(frame.Data, 2);
        }

        var record = new ModbusFrameRecord
        {
            ClientId = clientId,
            Frame = frame,
            ReceivedAt = DateTime.Now,
            StartAddress = startAddress,
            Quantity = quantity
        };

        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _items.Insert(0, record);

            if (_items.Count > MaximumStoredFrameCount)
            {
                _items.RemoveAt(_items.Count - 1);
            }

            _lastError = null;
        }

        DataChanged?.Invoke();
    }

    private void OnFrameError(
        Guid clientId,
        string errorMessage)
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _lastError =
                $"Master {clientId.ToString()[..8]}: {errorMessage}";
        }

        DataChanged?.Invoke();
    }

    private static ushort ReadUInt16BigEndian(
        byte[] bytes,
        int index)
    {
        return (ushort)(
            (bytes[index] << 8) |
            bytes[index + 1]);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        _frameAssembler.FrameReceived -= OnFrameReceived;
        _frameAssembler.FrameError -= OnFrameError;
    }
}