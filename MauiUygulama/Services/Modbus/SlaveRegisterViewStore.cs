using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Modbus;

public sealed class SlaveRegisterViewStore :
    ISlaveRegisterViewStore,
    IDisposable
{
    private const int ModbusAddressCount =
        ushort.MaxValue + 1;

    private readonly object _syncRoot = new();
    private readonly IRegisterManager _registerManager;

    private readonly Dictionary<RegisterKey, TrackedRegister>
        _tracked = new();

    private bool _isDisposed;

    public SlaveRegisterViewStore(
        IRegisterManager registerManager)
    {
        _registerManager = registerManager;
        _registerManager.ValueChanged +=
            OnRegisterValueChanged;
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
            .OrderByDescending(
                row => row.LastAccessedUtc)
            .ThenBy(row => row.Area)
            .ThenBy(row => row.Address)
            .ToArray();
    }

    public void RecordSuccessfulRange(
        RegisterArea area,
        ushort startAddress,
        ushort quantity,
        byte functionCode)
    {
        ThrowIfDisposed();
        ValidateSupportedArea(area);
        ValidateRange(startAddress, quantity);

        /*
         * Görünüm listesine eklemeden önce adreslerin
         * gerçekten Slave üzerinde tanımlı olduğu doğrulanır.
         */
        if (!IsEntireRangeDefined(
                area,
                startAddress,
                quantity))
        {
            throw new InvalidOperationException(
                $"{area} alanındaki {startAddress} başlangıç " +
                $"adresli {quantity} kayıtlık aralık Slave " +
                "üzerinde tanımlı değil. Başarılı erişim " +
                "olarak kaydedilemez.");
        }

        DateTime accessedAtUtc = DateTime.UtcNow;

        lock (_syncRoot)
        {
            for (int index = 0; index < quantity; index++)
            {
                ushort address =
                    (ushort)(startAddress + index);

                var key =
                    new RegisterKey(area, address);

                _tracked[key] =
                    new TrackedRegister(
                        key,
                        functionCode,
                        accessedAtUtc);
            }
        }

        Changed?.Invoke();
    }

    public void RecordSuccessfulAddress(
        RegisterArea area,
        ushort address,
        byte functionCode)
    {
        RecordSuccessfulRange(
            area,
            address,
            quantity: 1,
            functionCode);
    }

    public void ClearSuccessfulAccesses()
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

    /*
     * Bu üç metot eski kodun derlenmeye devam etmesi için
     * geçici yönlendirme görevi görüyor.
     */

    public void TrackRange(
        RegisterArea area,
        ushort startAddress,
        ushort quantity,
        byte functionCode)
    {
        RecordSuccessfulRange(
            area,
            startAddress,
            quantity,
            functionCode);
    }

    public void TrackAddress(
        RegisterArea area,
        ushort address,
        byte functionCode)
    {
        RecordSuccessfulAddress(
            area,
            address,
            functionCode);
    }

    public void Clear()
    {
        ClearSuccessfulAccesses();
    }

    private bool IsEntireRangeDefined(
        RegisterArea area,
        ushort startAddress,
        ushort quantity)
    {
        return area switch
        {
            RegisterArea.Coil =>
                _registerManager.TryReadCoils(
                    startAddress,
                    quantity,
                    out _),

            RegisterArea.HoldingRegister =>
                _registerManager.TryReadHoldingRegisters(
                    startAddress,
                    quantity,
                    out _),

            _ => false
        };
    }

    private ushort ReadCurrentValue(
        RegisterKey key)
    {
        switch (key.Area)
        {
            case RegisterArea.Coil:
                {
                    if (_registerManager.TryReadCoils(
                            key.Address,
                            quantity: 1,
                            out bool[] values))
                    {
                        return values[0]
                            ? (ushort)1
                            : (ushort)0;
                    }

                    break;
                }

            case RegisterArea.HoldingRegister:
                {
                    if (_registerManager.TryReadHoldingRegisters(
                            key.Address,
                            quantity: 1,
                            out ushort[] values))
                    {
                        return values[0];
                    }

                    break;
                }
        }

        throw new InvalidOperationException(
            $"{key.Area} alanındaki {key.Address} adresi " +
            "Slave üzerinde artık tanımlı değil.");
    }

    private void OnRegisterValueChanged(
        RegisterValueChangedEventArgs change)
    {
        bool isTracked;

        lock (_syncRoot)
        {
            isTracked = _tracked.ContainsKey(
                new RegisterKey(
                    change.Area,
                    change.Address));
        }

        if (isTracked)
        {
            Changed?.Invoke();
        }
    }

    private static void ValidateSupportedArea(
        RegisterArea area)
    {
        if (area is not RegisterArea.Coil and
            not RegisterArea.HoldingRegister)
        {
            throw new ArgumentOutOfRangeException(
                nameof(area),
                "Şu anda yalnızca Coil ve Holding Register " +
                "görünümü destekleniyor.");
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

        int endAddressExclusive =
            startAddress + quantity;

        if (endAddressExclusive > ModbusAddressCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Başlangıç adresi ve miktar Modbus adres " +
                "sınırını aşıyor.");
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

        _registerManager.ValueChanged -=
            OnRegisterValueChanged;
    }

    private sealed record TrackedRegister(
        RegisterKey Key,
        byte FunctionCode,
        DateTime LastAccessedUtc);
}