using MauiUygulama.Models;

namespace MauiUygulama.ViewModels;

public sealed class ModeSelectionViewModel : BaseViewModel
{
    private ModbusOperatingMode _selectedMode = ModbusOperatingMode.None;

    public ModbusOperatingMode SelectedMode
    {
        get => _selectedMode;
        private set => SetProperty(ref _selectedMode, value);
    }

    public void SelectMode(ModbusOperatingMode mode)
    {
        SelectedMode = mode;
    }
}