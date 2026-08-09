using MauiUygulama.Models;

namespace MauiUygulama.Core.Interfaces;

public interface IModbusPollingService
{
    ModbusPollingStatus Status { get; }

    event Action? StatusChanged;

    Task StartAsync(
        ModbusPollingSettings settings,
        CancellationToken cancellationToken = default);

    Task StopAsync();
}
