namespace MauiUygulama.Models;

public sealed class ModbusFrameRecord
{
    public Guid ClientId { get; init; }

    public ModbusTcpFrame Frame { get; init; } = new();

    public DateTime ReceivedAt { get; init; }

    public ushort? StartAddress { get; init; }

    public ushort? Quantity { get; init; }
}