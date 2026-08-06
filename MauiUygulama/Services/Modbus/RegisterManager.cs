using MauiUygulama.Core.Interfaces;

namespace MauiUygulama.Services.Modbus;

public sealed class RegisterManager : IRegisterManager
{
    private const int ModbusAddressCount = ushort.MaxValue + 1;

    private readonly object _syncRoot = new();
    private readonly bool[] _coils = new bool[ModbusAddressCount];
    private readonly bool[] _discreteInputs = new bool[ModbusAddressCount];
    private readonly ushort[] _holdingRegisters = new ushort[ModbusAddressCount];

    public RegisterManager()
    {
        LoadInitialTestValues();
    }

    public int RegisterCount => ModbusAddressCount;

    public event Action? RegistersChanged;

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
            value);
    }

    public void WriteMultipleCoils(
        ushort startAddress,
        IReadOnlyList<bool> values)
    {
        WriteRange(
            _coils,
            startAddress,
            values);
    }

    public void WriteSingleHoldingRegister(
        ushort address,
        ushort value)
    {
        WriteSingleValue(
            _holdingRegisters,
            address,
            value);
    }

    public void WriteMultipleHoldingRegisters(
        ushort startAddress,
        IReadOnlyList<ushort> values)
    {
        WriteRange(
            _holdingRegisters,
            startAddress,
            values);
    }

    public void SetDiscreteInput(
        ushort address,
        bool value)
    {
        WriteSingleValue(
            _discreteInputs,
            address,
            value);
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
        T value)
    {
        lock (_syncRoot)
        {
            target[address] = value;
        }

        RegistersChanged?.Invoke();
    }

    private void WriteRange<T>(
        T[] target,
        ushort startAddress,
        IReadOnlyList<T> values)
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

        lock (_syncRoot)
        {
            for (int index = 0; index < values.Count; index++)
            {
                target[startAddress + index] = values[index];
            }
        }

        RegistersChanged?.Invoke();
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

    private void LoadInitialTestValues()
    {
        _holdingRegisters[0] = 100;
        _holdingRegisters[1] = 250;
        _holdingRegisters[2] = 500;
        _holdingRegisters[3] = 1000;

        _coils[0] = true;
        _discreteInputs[0] = true;
    }
}
