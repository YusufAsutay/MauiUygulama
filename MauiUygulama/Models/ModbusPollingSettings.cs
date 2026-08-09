namespace MauiUygulama.Models;

public sealed class ModbusPollingSettings
{
    public byte FunctionCode { get; set; } = 0x03;

    public byte UnitId { get; set; } = 1;

    public ushort StartingAddress { get; set; }

    public ushort Quantity { get; set; } = 1;

    public int IntervalMilliseconds { get; set; } = 1000;

    public int RequestTimeoutMilliseconds { get; set; } =
        ModbusRequestOptions.DefaultTimeoutMilliseconds;

    public ModbusPollingSettings Copy()
    {
        return new ModbusPollingSettings
        {
            FunctionCode = FunctionCode,
            UnitId = UnitId,
            StartingAddress = StartingAddress,
            Quantity = Quantity,
            IntervalMilliseconds = IntervalMilliseconds,
            RequestTimeoutMilliseconds = RequestTimeoutMilliseconds
        };
    }
}
