using MauiUygulama.Models;

namespace MauiUygulama.Core.Interfaces;

public interface IModbusRequestProcessor
{
    Task ProcessAsync(
        Guid clientId,
        ModbusTcpFrame frame,
        CancellationToken cancellationToken = default);
}