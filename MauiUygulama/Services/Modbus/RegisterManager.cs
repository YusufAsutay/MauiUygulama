using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Modbus;

public sealed class RegisterManager : IRegisterManager
{
    private const int ModbusAddressCount = ushort.MaxValue + 1;

    private readonly object _syncRoot = new();

    // Yalnızca Slave tarafından gerçekten tanımlanmış adresler tutulur.
    private readonly Dictionary<ushort, bool> _coils = new();
    private readonly Dictionary<ushort, bool> _discreteInputs = new();
    private readonly Dictionary<ushort, ushort> _holdingRegisters = new();

    public int RegisterCount
    {
        get
        {
            lock (_syncRoot)
            {
                return
                    _coils.Count +
                    _discreteInputs.Count +
                    _holdingRegisters.Count;
            }
        }
    }

    public event Action<RegisterValueChangedEventArgs>? ValueChanged;

    public IReadOnlyDictionary<ushort, bool> GetDefinedCoils()
    {
        lock (_syncRoot)
        {
            return new Dictionary<ushort, bool>(_coils);
        }
    }

    public IReadOnlyDictionary<ushort, bool> GetDefinedDiscreteInputs()
    {
        lock (_syncRoot)
        {
            return new Dictionary<ushort, bool>(_discreteInputs);
        }
    }

    public IReadOnlyDictionary<ushort, ushort>
        GetDefinedHoldingRegisters()
    {
        lock (_syncRoot)
        {
            return new Dictionary<ushort, ushort>(
                _holdingRegisters);
        }
    }

    public bool DefineCoil(
        ushort address,
        bool initialValue)
    {
        return DefineValue(
            _coils,
            address,
            initialValue,
            RegisterArea.Coil,
            initialValue ? (ushort)1 : (ushort)0);
    }

    public bool DefineDiscreteInput(
        ushort address,
        bool initialValue)
    {
        return DefineValue(
            _discreteInputs,
            address,
            initialValue,
            RegisterArea.DiscreteInput,
            initialValue ? (ushort)1 : (ushort)0);
    }

    public bool DefineHoldingRegister(
        ushort address,
        ushort initialValue)
    {
        return DefineValue(
            _holdingRegisters,
            address,
            initialValue,
            RegisterArea.HoldingRegister,
            initialValue);
    }

    public bool TryReadCoils(
        ushort startAddress,
        ushort quantity,
        out bool[] values)
    {
        return TryReadRange(
            _coils,
            startAddress,
            quantity,
            out values);
    }

    public bool TryReadDiscreteInputs(
        ushort startAddress,
        ushort quantity,
        out bool[] values)
    {
        return TryReadRange(
            _discreteInputs,
            startAddress,
            quantity,
            out values);
    }

    public bool TryReadHoldingRegisters(
        ushort startAddress,
        ushort quantity,
        out ushort[] values)
    {
        return TryReadRange(
            _holdingRegisters,
            startAddress,
            quantity,
            out values);
    }

    public bool TryWriteSingleCoil(
        ushort address,
        bool value)
    {
        return TryWriteSingleValue(
            _coils,
            address,
            value,
            RegisterArea.Coil,
            value ? (ushort)1 : (ushort)0);
    }

    public bool TryWriteSingleHoldingRegister(
        ushort address,
        ushort value)
    {
        return TryWriteSingleValue(
            _holdingRegisters,
            address,
            value,
            RegisterArea.HoldingRegister,
            value);
    }

    public bool TrySetDiscreteInput(
        ushort address,
        bool value)
    {
        return TryWriteSingleValue(
            _discreteInputs,
            address,
            value,
            RegisterArea.DiscreteInput,
            value ? (ushort)1 : (ushort)0);
    }

    /*
     * Aşağıdaki metotlar eski çağrı noktalarının bu aşamada
     * bozulmaması için geçici olarak korunuyor.
     */

    public bool[] ReadCoils(
        ushort startAddress,
        ushort quantity)
    {
        ValidateRange(
            startAddress,
            quantity,
            nameof(quantity));

        if (TryReadCoils(
                startAddress,
                quantity,
                out bool[] values))
        {
            return values;
        }

        throw CreateUndefinedAddressException(
            RegisterArea.Coil,
            startAddress,
            quantity);
    }

    public bool[] ReadDiscreteInputs(
        ushort startAddress,
        ushort quantity)
    {
        ValidateRange(
            startAddress,
            quantity,
            nameof(quantity));

        if (TryReadDiscreteInputs(
                startAddress,
                quantity,
                out bool[] values))
        {
            return values;
        }

        throw CreateUndefinedAddressException(
            RegisterArea.DiscreteInput,
            startAddress,
            quantity);
    }

    public ushort[] ReadHoldingRegisters(
        ushort startAddress,
        ushort quantity)
    {
        ValidateRange(
            startAddress,
            quantity,
            nameof(quantity));

        if (TryReadHoldingRegisters(
                startAddress,
                quantity,
                out ushort[] values))
        {
            return values;
        }

        throw CreateUndefinedAddressException(
            RegisterArea.HoldingRegister,
            startAddress,
            quantity);
    }

    public void WriteSingleCoil(
        ushort address,
        bool value)
    {
        if (!TryWriteSingleCoil(address, value))
        {
            throw CreateUndefinedAddressException(
                RegisterArea.Coil,
                address,
                quantity: 1);
        }
    }

    public void WriteMultipleCoils(
        ushort startAddress,
        IReadOnlyList<bool> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        ValidateRange(
            startAddress,
            values.Count,
            nameof(values));

        if (!TryWriteRange(
                _coils,
                startAddress,
                values,
                RegisterArea.Coil,
                static value =>
                    value ? (ushort)1 : (ushort)0))
        {
            throw CreateUndefinedAddressException(
                RegisterArea.Coil,
                startAddress,
                values.Count);
        }
    }

    public void WriteSingleHoldingRegister(
        ushort address,
        ushort value)
    {
        if (!TryWriteSingleHoldingRegister(address, value))
        {
            throw CreateUndefinedAddressException(
                RegisterArea.HoldingRegister,
                address,
                quantity: 1);
        }
    }

    public void WriteMultipleHoldingRegisters(
        ushort startAddress,
        IReadOnlyList<ushort> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        ValidateRange(
            startAddress,
            values.Count,
            nameof(values));

        if (!TryWriteRange(
                _holdingRegisters,
                startAddress,
                values,
                RegisterArea.HoldingRegister,
                static value => value))
        {
            throw CreateUndefinedAddressException(
                RegisterArea.HoldingRegister,
                startAddress,
                values.Count);
        }
    }

    public void SetDiscreteInput(
        ushort address,
        bool value)
    {
        if (!TrySetDiscreteInput(address, value))
        {
            throw CreateUndefinedAddressException(
                RegisterArea.DiscreteInput,
                address,
                quantity: 1);
        }
    }

    private bool DefineValue<T>(
        Dictionary<ushort, T> target,
        ushort address,
        T initialValue,
        RegisterArea area,
        ushort eventValue)
    {
        bool added;

        lock (_syncRoot)
        {
            if (target.ContainsKey(address))
            {
                return false;
            }

            target.Add(address, initialValue);
            added = true;
        }

        if (added)
        {
            ValueChanged?.Invoke(
                new RegisterValueChangedEventArgs(
                    area,
                    address,
                    eventValue));
        }

        return true;
    }

    private bool TryReadRange<T>(
        Dictionary<ushort, T> source,
        ushort startAddress,
        ushort quantity,
        out T[] values)
    {
        values = Array.Empty<T>();

        if (!IsValidRange(startAddress, quantity))
        {
            return false;
        }

        var result = new T[quantity];

        lock (_syncRoot)
        {
            for (int index = 0; index < quantity; index++)
            {
                ushort address =
                    (ushort)(startAddress + index);

                if (!source.TryGetValue(
                        address,
                        out T? currentValue))
                {
                    return false;
                }

                result[index] = currentValue;
            }
        }

        values = result;
        return true;
    }

    private bool TryWriteSingleValue<T>(
        Dictionary<ushort, T> target,
        ushort address,
        T value,
        RegisterArea area,
        ushort eventValue)
    {
        bool changed;

        lock (_syncRoot)
        {
            if (!target.TryGetValue(
                    address,
                    out T? currentValue))
            {
                return false;
            }

            changed =
                !EqualityComparer<T>.Default.Equals(
                    currentValue,
                    value);

            if (changed)
            {
                target[address] = value;
            }
        }

        if (changed)
        {
            ValueChanged?.Invoke(
                new RegisterValueChangedEventArgs(
                    area,
                    address,
                    eventValue));
        }

        return true;
    }

    private bool TryWriteRange<T>(
        Dictionary<ushort, T> target,
        ushort startAddress,
        IReadOnlyList<T> values,
        RegisterArea area,
        Func<T, ushort> eventValueSelector)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (!IsValidRange(startAddress, values.Count))
        {
            return false;
        }

        var changes =
            new List<RegisterValueChangedEventArgs>();

        lock (_syncRoot)
        {
            // Önce bütün adresler kontrol edilir.
            // Tek bir adres bile yoksa hiçbir değer değiştirilmez.
            for (int index = 0; index < values.Count; index++)
            {
                ushort address =
                    (ushort)(startAddress + index);

                if (!target.ContainsKey(address))
                {
                    return false;
                }
            }

            for (int index = 0; index < values.Count; index++)
            {
                ushort address =
                    (ushort)(startAddress + index);

                T newValue = values[index];
                T currentValue = target[address];

                if (EqualityComparer<T>.Default.Equals(
                        currentValue,
                        newValue))
                {
                    continue;
                }

                target[address] = newValue;

                changes.Add(
                    new RegisterValueChangedEventArgs(
                        area,
                        address,
                        eventValueSelector(newValue)));
            }
        }

        foreach (RegisterValueChangedEventArgs change in changes)
        {
            ValueChanged?.Invoke(change);
        }

        return true;
    }

    private static bool IsValidRange(
        ushort startAddress,
        int quantity)
    {
        if (quantity <= 0)
        {
            return false;
        }

        int endAddressExclusive =
            startAddress + quantity;

        return endAddressExclusive <= ModbusAddressCount;
    }

    private static void ValidateRange(
        ushort startAddress,
        int quantity,
        string parameterName)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Miktar sıfırdan büyük olmalıdır.");
        }

        int endAddressExclusive =
            startAddress + quantity;

        if (endAddressExclusive > ModbusAddressCount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Başlangıç adresi ve miktar Modbus adres sınırını aşıyor.");
        }
    }

    private static KeyNotFoundException
        CreateUndefinedAddressException(
            RegisterArea area,
            ushort startAddress,
            int quantity)
    {
        int endAddress =
            startAddress + quantity - 1;

        return new KeyNotFoundException(
            $"{area} alanındaki {startAddress}-{endAddress} " +
            "adres aralığının tamamı Slave üzerinde tanımlı değil.");
    }
}