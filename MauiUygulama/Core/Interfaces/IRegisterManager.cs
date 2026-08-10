using MauiUygulama.Models;

namespace MauiUygulama.Core.Interfaces;

public interface IRegisterManager
{
    /// <summary>
    /// Slave üzerinde gerçekten tanımlanmış toplam adres sayısıdır.
    /// </summary>
    int RegisterCount { get; }

    event Action<RegisterValueChangedEventArgs>? ValueChanged;

    // Slave veri haritasını görüntülemek için kullanılır.
    IReadOnlyDictionary<ushort, bool> GetDefinedCoils();

    IReadOnlyDictionary<ushort, bool> GetDefinedDiscreteInputs();

    IReadOnlyDictionary<ushort, ushort> GetDefinedHoldingRegisters();

    // Bu metotlar yalnızca Slave yapılandırması tarafından çağrılmalıdır.
    // Adres daha önce tanımlanmışsa false döner.
    bool DefineCoil(
        ushort address,
        bool initialValue);

    bool DefineDiscreteInput(
        ushort address,
        bool initialValue);

    bool DefineHoldingRegister(
        ushort address,
        ushort initialValue);

    // Master isteği sırasında kullanılan güvenli metotlar.
    // Adres/aralık tanımlı değilse false döner ve adres oluşturmaz.
    bool TryReadCoils(
        ushort startAddress,
        ushort quantity,
        out bool[] values);

    bool TryReadDiscreteInputs(
        ushort startAddress,
        ushort quantity,
        out bool[] values);

    bool TryReadHoldingRegisters(
        ushort startAddress,
        ushort quantity,
        out ushort[] values);

    bool TryWriteSingleCoil(
        ushort address,
        bool value);

    bool TryWriteSingleHoldingRegister(
        ushort address,
        ushort value);

    bool TrySetDiscreteInput(
        ushort address,
        bool value);

    // Eski çağrı noktalarının şimdilik bozulmaması için korunuyor.
    bool[] ReadCoils(
        ushort startAddress,
        ushort quantity);

    bool[] ReadDiscreteInputs(
        ushort startAddress,
        ushort quantity);

    ushort[] ReadHoldingRegisters(
        ushort startAddress,
        ushort quantity);

    void WriteSingleCoil(
        ushort address,
        bool value);

    void WriteMultipleCoils(
        ushort startAddress,
        IReadOnlyList<bool> values);

    void WriteSingleHoldingRegister(
        ushort address,
        ushort value);

    void WriteMultipleHoldingRegisters(
        ushort startAddress,
        IReadOnlyList<ushort> values);

    void SetDiscreteInput(
        ushort address,
        bool value);
}