using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Modbus;

public sealed class RegisterManager : IRegisterManager
{
    // Modbus adresleri ushort olduğundan her veri alanı 0-65535
    // arasındaki toplam 65.536 adresi destekler.
    private const int ModbusAddressCount = ushort.MaxValue + 1;

    private readonly object _syncRoot = new();
    private readonly bool[] _coils = new bool[ModbusAddressCount];
    private readonly bool[] _discreteInputs = new bool[ModbusAddressCount];
    private readonly ushort[] _holdingRegisters = new ushort[ModbusAddressCount];

    public int RegisterCount => ModbusAddressCount;

    public event Action<RegisterValueChangedEventArgs>? ValueChanged;

    public bool[] ReadCoils(
        ushort startAddress,
        ushort quantity)
    {
        return ReadRange(
            _coils,
            startAddress,
            quantity);
    }

    public bool[] ReadDiscreteInputs(
        ushort startAddress,
        ushort quantity)
    {
        return ReadRange(
            _discreteInputs,
            startAddress,
            quantity);
    }

    public ushort[] ReadHoldingRegisters(
        ushort startAddress,
        ushort quantity)
    {
        return ReadRange(
            _holdingRegisters,
            startAddress,
            quantity);
    }

    public void WriteSingleCoil(
        ushort address,
        bool value)
    {
        WriteSingleValue(
            _coils,
            address,
            value,
            RegisterArea.Coil,
            value ? (ushort)1 : (ushort)0);
    }

    public void WriteMultipleCoils(
        ushort startAddress,
        IReadOnlyList<bool> values)
    {
        WriteRange(
            _coils,
            startAddress,
            values,
            RegisterArea.Coil,
            static value => value ? (ushort)1 : (ushort)0);
    }

    public void WriteSingleHoldingRegister(
        ushort address,
        ushort value)
    {
        WriteSingleValue(
            _holdingRegisters,
            address,
            value,
            RegisterArea.HoldingRegister,
            value);
    }

    public void WriteMultipleHoldingRegisters(
        ushort startAddress,
        IReadOnlyList<ushort> values)
    {
        WriteRange(
            _holdingRegisters,
            startAddress,
            values,
            RegisterArea.HoldingRegister,
            static value => value);
    }

    public void SetDiscreteInput(
        ushort address,
        bool value)
    {
        WriteSingleValue(
            _discreteInputs,
            address,
            value,
            RegisterArea.DiscreteInput,
            value ? (ushort)1 : (ushort)0);
    }

    private T[] ReadRange<T>(
        T[] source,
        ushort startAddress,
        ushort quantity)
    {
        ValidateRange(
            startAddress,
            quantity,
            nameof(quantity));

        var result = new T[quantity];

        lock (_syncRoot)
        {
            Array.Copy(
                source,
                startAddress,
                result,
                0,
                quantity);
        }

        return result;
    }

    private void WriteSingleValue<T>(
        T[] target,
        ushort address,
        T value,
        RegisterArea area,
        ushort eventValue)
    {
        bool changed;

        lock (_syncRoot)
        {
            changed = !EqualityComparer<T>.Default.Equals(
                target[address],
                value);

            target[address] = value;
        }

        if (changed)
        {
            ValueChanged?.Invoke(
                new RegisterValueChangedEventArgs(
                    area,
                    address,
                    eventValue));
        }
    }

    private void WriteRange<T>(
        T[] target,
        ushort startAddress,
        IReadOnlyList<T> values,
        RegisterArea area,
        Func<T, ushort> eventValueSelector)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            throw new ArgumentException(
                "En az bir değer yazılmalıdır.",
                nameof(values));
        }

        ValidateRange(
            startAddress,
            values.Count,
            nameof(values));

        var changes = new List<RegisterValueChangedEventArgs>();

        lock (_syncRoot)
        {
            for (int index = 0; index < values.Count; index++)
            {
                int targetAddress = startAddress + index;
                T newValue = values[index];

                if (EqualityComparer<T>.Default.Equals(
                        target[targetAddress],
                        newValue))
                {
                    continue;
                }

                target[targetAddress] = newValue;

                changes.Add(
                    new RegisterValueChangedEventArgs(
                        area,
                        (ushort)targetAddress,
                        eventValueSelector(newValue)));
            }
        }

        foreach (RegisterValueChangedEventArgs change in changes)
        {
            ValueChanged?.Invoke(change);
        }
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

        int endAddressExclusive = startAddress + quantity;

        if (endAddressExclusive > ModbusAddressCount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Başlangıç adresi ve miktar Modbus adres sınırını aşıyor.");
        }
    }

}
