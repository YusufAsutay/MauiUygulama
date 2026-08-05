namespace MauiUygulama.Core.Interfaces;

public interface IRegisterManager
{
    int RegisterCount { get; }

    bool[] ReadCoils(
        ushort startAddress,
        ushort quantity);

    bool[] ReadDiscreteInputs(
        ushort startAddress,
        ushort quantity);

    ushort[] ReadInputRegisters(
        ushort startAddress,
        ushort quantity);

    ushort[] ReadHoldingRegisters(
        ushort startAddress,
        ushort quantity);

    void WriteSingleCoil(
        ushort address,
        bool value);

    void WriteMultipleCoils(
        ushort startAddress,
        IReadOnlyList<bool> values);

    void WriteSingleHoldingRegister(
        ushort address,
        ushort value);

    void WriteMultipleHoldingRegisters(
        ushort startAddress,
        IReadOnlyList<ushort> values);

    void SetDiscreteInput(
        ushort address,
        bool value);

    void SetInputRegister(
        ushort address,
        ushort value);
}