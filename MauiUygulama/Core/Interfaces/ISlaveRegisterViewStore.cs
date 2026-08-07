using MauiUygulama.Models;

namespace MauiUygulama.Core.Interfaces;

public interface ISlaveRegisterViewStore
{
    event Action? Changed;

    IReadOnlyList<SlaveRegisterRow> GetRows();

    void TrackRange(
        RegisterArea area,
        ushort startAddress,
        ushort quantity,
        byte functionCode);

    void TrackAddress(
        RegisterArea area,
        ushort address,
        byte functionCode);

    void Clear();
}
