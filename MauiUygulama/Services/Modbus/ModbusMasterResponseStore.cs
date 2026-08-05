using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Modbus;

public sealed class ModbusMasterResponseStore : IModbusMasterResponseStore
{
    private const int MaximumStoredResponseCount = 100;

    private readonly object _syncRoot = new();
    private readonly List<ModbusMasterResponseRecord> _items = new();

    public IReadOnlyList<ModbusMasterResponseRecord> Items
    {
        get
        {
            lock (_syncRoot)
            {
                return _items.ToArray();
            }
        }
    }

    public event Action? DataChanged;

    public void Add(ModbusMasterResponseRecord response)
    {
        ArgumentNullException.ThrowIfNull(response);

        lock (_syncRoot)
        {
            _items.Insert(0, response);

            if (_items.Count > MaximumStoredResponseCount)
            {
                _items.RemoveAt(_items.Count - 1);
            }
        }

        DataChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            if (_items.Count == 0)
            {
                return;
            }

            _items.Clear();
        }

        DataChanged?.Invoke();
    }
}
