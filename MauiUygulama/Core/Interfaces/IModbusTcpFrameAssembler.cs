using MauiUygulama.Models;

namespace MauiUygulama.Core.Interfaces;

public interface IModbusTcpFrameAssembler : IDisposable
{
    event Action<Guid, ModbusTcpFrame>? FrameReceived;

    event Action<Guid, string>? FrameError;
}