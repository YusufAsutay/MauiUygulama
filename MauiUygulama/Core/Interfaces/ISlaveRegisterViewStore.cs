using MauiUygulama.Models;

namespace MauiUygulama.Core.Interfaces;

public interface ISlaveRegisterViewStore
{
    event Action? Changed;

    IReadOnlyList<SlaveRegisterRow> GetRows();

    // Yeni isimlendirme:
    // Yalnızca başarıyla erişilmiş, önceden tanımlı adresleri kaydeder.
    void RecordSuccessfulRange(
        RegisterArea area,
        ushort startAddress,
        ushort quantity,
        byte functionCode);

    void RecordSuccessfulAddress(
        RegisterArea area,
        ushort address,
        byte functionCode);

    void ClearSuccessfulAccesses();

    /*
     * Eski çağrı noktalarının hemen bozulmaması için
     * geçici olarak korunuyor.
     */
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