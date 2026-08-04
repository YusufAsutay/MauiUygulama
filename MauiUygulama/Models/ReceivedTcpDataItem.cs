namespace MauiUygulama.Models;

public sealed class ReceivedTcpDataItem
{
    public Guid ClientId { get; init; }

    public string ClientName { get; init; } = string.Empty;

    public DateTime ReceivedAt { get; init; }

    public string Text { get; init; } = string.Empty;

    public string Hex { get; init; } = string.Empty;

    public int ByteCount { get; init; }
}