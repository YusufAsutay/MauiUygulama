using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Modbus;

public sealed class SlaveRegisterViewStore :
    ISlaveRegisterViewStore,
    IDisposable
{
    private const int ModbusAddressCount = ushort.MaxValue + 1;

    private readonly object _syncRoot = new();
    private readonly IRegisterManager _registerManager;
    private readonly Dictionary<RegisterKey, TrackedRegister> _tracked = new();

    private bool _isDisposed;

    public SlaveRegisterViewStore(
        IRegisterManager registerManager)
    {
        _registerManager = registerManager;
        _registerManager.ValueChanged += OnRegisterValueChanged;
    }

    public event Action? Changed;

    public IReadOnlyList<SlaveRegisterRow> GetRows()
    {
        TrackedRegister[] trackedRows;

        lock (_syncRoot)
        {
            trackedRows = _tracked.Values.ToArray();
        }

        return trackedRows
            .Select(item => new SlaveRegisterRow(
                item.Key.Area,
                item.Key.Address,
                ReadCurrentValue(item.Key),
                item.FunctionCode,
                item.LastAccessedUtc))
            .OrderByDescending(row => row.LastAccessedUtc)
            .ThenBy(row => row.Area)
            .ThenBy(row => row.Address)
            .ToArray();
    }

    public void TrackRange(
        RegisterArea area,
        ushort startAddress,
        ushort quantity,
        byte functionCode)
    {
        ThrowIfDisposed();
        ValidateSupportedArea(area);
        ValidateRange(startAddress, quantity);

        DateTime accessedAtUtc = DateTime.UtcNow;

        lock (_syncRoot)
        {
            for (int index = 0; index < quantity; index++)
            {
                ushort address = (ushort)(startAddress + index);
                var key = new RegisterKey(area, address);

                _tracked[key] = new TrackedRegister(
                    key,
                    functionCode,
                    accessedAtUtc);
            }
        }

        Changed?.Invoke();
    }

    public void TrackAddress(
        RegisterArea area,
        ushort address,
        byte functionCode)
    {
        TrackRange(
            area,
            address,
            quantity: 1,
            functionCode);
    }

    public void Clear()
    {
        ThrowIfDisposed();

        bool hadRows;

        lock (_syncRoot)
        {
            hadRows = _tracked.Count > 0;
            _tracked.Clear();
        }

        if (hadRows)
        {
            Changed?.Invoke();
        }
    }

    private ushort ReadCurrentValue(RegisterKey key)
    {
        return key.Area switch
        {
            RegisterArea.Coil =>
                _registerManager.ReadCoils(key.Address, 1)[0]
                    ? (ushort)1
                    : (ushort)0,

            RegisterArea.HoldingRegister =>
                _registerManager.ReadHoldingRegisters(key.Address, 1)[0],

            _ => throw new InvalidOperationException(
                $"{key.Area} alanı Slave görünümünde henüz desteklenmiyor.")
        };
    }

    private void OnRegisterValueChanged(
        RegisterValueChangedEventArgs change)
    {
        bool isTracked;

        lock (_syncRoot)
        {
            isTracked = _tracked.ContainsKey(
                new RegisterKey(change.Area, change.Address));
        }

        if (isTracked)
        {
            Changed?.Invoke();
        }
    }

    private static void ValidateSupportedArea(RegisterArea area)
    {
        if (area is not RegisterArea.Coil and
            not RegisterArea.HoldingRegister)
        {
            throw new ArgumentOutOfRangeException(
                nameof(area),
                "Şu anda yalnızca Coil ve Holding Register görünümü destekleniyor.");
        }
    }

    private static void ValidateRange(
        ushort startAddress,
        ushort quantity)
    {
        if (quantity == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Miktar sıfırdan büyük olmalıdır.");
        }

        int endAddressExclusive = startAddress + quantity;

        if (endAddressExclusive > ModbusAddressCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Başlangıç adresi ve miktar Modbus adres sınırını aşıyor.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _isDisposed,
            this);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _registerManager.ValueChanged -= OnRegisterValueChanged;
    }

    private sealed record TrackedRegister(
        RegisterKey Key,
        byte FunctionCode,
        DateTime LastAccessedUtc);
}
