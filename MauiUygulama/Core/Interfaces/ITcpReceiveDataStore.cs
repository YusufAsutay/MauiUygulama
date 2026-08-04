using MauiUygulama.Models;

namespace MauiUygulama.Core.Interfaces;

public interface ITcpReceivedDataStore : IDisposable
{
    IReadOnlyList<ReceivedTcpDataItem> Items { get; }

    event Action? DataChanged;

    void Clear();
}