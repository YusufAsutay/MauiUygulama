namespace MauiUygulama.Models;

public sealed class ModbusMasterResponseRecord
{
    public ushort TransactionId { get; init; }

    public byte UnitId { get; init; }

    public ushort? StartAddress { get; init; }

    public ushort[] RegisterValues { get; init; } =
        Array.Empty<ushort>();

    public DateTime ReceivedAt { get; init; }

    public string Hex { get; init; } = string.Empty;
}