using MauiUygulama.Models;

namespace MauiUygulama.Core.Interfaces;

public interface IModbusMasterService
{
    bool IsConnected { get; }

    event Action<bool>? ConnectionStateChanged;

    Task ConnectAsync(
        TcpConnectionSettings settings,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    Task<ModbusMasterOperationResult> ReadCoilsAsync(
        byte unitId,
        ushort startingAddress,
        ushort quantity,
        CancellationToken cancellationToken = default);

    Task<ModbusMasterOperationResult> ReadHoldingRegistersAsync(
        byte unitId,
        ushort startingAddress,
        ushort quantity,
        CancellationToken cancellationToken = default);

    Task<ModbusMasterOperationResult> WriteSingleHoldingRegisterAsync(
        byte unitId,
        ushort address,
        ushort value,
        CancellationToken cancellationToken = default);
}
