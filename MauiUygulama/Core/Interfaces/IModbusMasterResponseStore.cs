using MauiUygulama.Models;

namespace MauiUygulama.Core.Interfaces;

public interface IModbusMasterResponseStore
{
    IReadOnlyList<ModbusMasterResponseRecord> Items { get; }

    event Action? DataChanged;

    void Add(ModbusMasterResponseRecord response);

    void Clear();
}