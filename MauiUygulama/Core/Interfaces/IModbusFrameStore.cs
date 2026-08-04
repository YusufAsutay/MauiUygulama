using MauiUygulama.Models;

namespace MauiUygulama.Core.Interfaces;

public interface IModbusFrameStore
{
    IReadOnlyList<ModbusFrameRecord> Items { get; }

    string? LastError { get; }

    event Action? DataChanged;

    void Clear();
}