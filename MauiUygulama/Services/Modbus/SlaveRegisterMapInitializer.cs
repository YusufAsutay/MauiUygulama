using MauiUygulama.Core.Interfaces;

namespace MauiUygulama.Services.Modbus;

/// <summary>
/// Slave üzerinde kullanılacak başlangıç register
/// haritasını bir defa oluşturur.
/// </summary>
public sealed class SlaveRegisterMapInitializer
{
    private readonly object _syncRoot = new();
    private readonly IRegisterManager _registerManager;

    private bool _isInitialized;

    public SlaveRegisterMapInitializer(
        IRegisterManager registerManager)
    {
        _registerManager = registerManager;
    }

    public void Initialize()
    {
        lock (_syncRoot)
        {
            if (_isInitialized)
            {
                return;
            }

            DefineCoils();
            DefineHoldingRegisters();

            _isInitialized = true;
        }
    }

    private void DefineCoils()
    {
        _registerManager.DefineCoil(
            address: 20,
            initialValue: false);

        _registerManager.DefineCoil(
            address: 21,
            initialValue: true);

        _registerManager.DefineCoil(
            address: 22,
            initialValue: false);

        _registerManager.DefineCoil(
            address: 23,
            initialValue: true);
    }

    private void DefineHoldingRegisters()
    {
        _registerManager.DefineHoldingRegister(
            address: 100,
            initialValue: 10);

        _registerManager.DefineHoldingRegister(
            address: 101,
            initialValue: 100);

        _registerManager.DefineHoldingRegister(
            address: 102,
            initialValue: 500);
    }
}