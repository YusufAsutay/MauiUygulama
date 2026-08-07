namespace MauiUygulama.Models;

public sealed record SlaveRegisterRow(
    RegisterArea Area,
    ushort Address,
    ushort Value,
    byte FunctionCode,
    DateTime LastAccessedUtc)
{
    public bool BoolValue => Value != 0;

    public string DecimalText => Value.ToString();

    public string HexText => $"0x{Value:X4}";

    public string BinaryText =>
        Convert.ToString(Value, 2).PadLeft(16, '0');
}
