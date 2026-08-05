using System.Diagnostics;
using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Modbus;

public sealed class ModbusRequestProcessor :
    IModbusRequestProcessor,
    IDisposable
{
    private const byte ReadHoldingRegistersFunctionCode = 0x03;

    private const byte IllegalFunctionExceptionCode = 0x01;
    private const byte IllegalDataAddressExceptionCode = 0x02;
    private const byte IllegalDataValueExceptionCode = 0x03;
    private const byte ServerDeviceFailureExceptionCode = 0x04;

    private const ushort MinimumReadRegisterQuantity = 1;
    private const ushort MaximumReadRegisterQuantity = 125;

    private readonly IModbusTcpFrameAssembler _frameAssembler;
    private readonly ITcpServerService _tcpServer;
    private readonly IRegisterManager _registerManager;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private bool _isDisposed;

    public ModbusRequestProcessor(
        IModbusTcpFrameAssembler frameAssembler,
        ITcpServerService tcpServer,
        IRegisterManager registerManager)
    {
        _frameAssembler = frameAssembler;
        _tcpServer = tcpServer;
        _registerManager = registerManager;

        _frameAssembler.FrameReceived += OnFrameReceived;
    }

    public async Task ProcessAsync(
        Guid clientId,
        ModbusTcpFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (_isDisposed)
        {
            throw new ObjectDisposedException(
                nameof(ModbusRequestProcessor));
        }

        if (frame.FunctionCode != ReadHoldingRegistersFunctionCode)
        {
            await SendExceptionResponseAsync(
                clientId,
                frame,
                IllegalFunctionExceptionCode,
                cancellationToken);

            return;
        }

        await ProcessReadHoldingRegistersAsync(
            clientId,
            frame,
            cancellationToken);
    }

    private async Task ProcessReadHoldingRegistersAsync(
        Guid clientId,
        ModbusTcpFrame frame,
        CancellationToken cancellationToken)
    {
        if (frame.Data.Length != 4)
        {
            await SendExceptionResponseAsync(
                clientId,
                frame,
                IllegalDataValueExceptionCode,
                cancellationToken);

            return;
        }

        ushort startAddress = ReadUInt16BigEndian(
            frame.Data,
            0);

        ushort quantity = ReadUInt16BigEndian(
            frame.Data,
            2);

        if (quantity < MinimumReadRegisterQuantity ||
            quantity > MaximumReadRegisterQuantity)
        {
            await SendExceptionResponseAsync(
                clientId,
                frame,
                IllegalDataValueExceptionCode,
                cancellationToken);

            return;
        }

        int endAddressExclusive = startAddress + quantity;

        if (endAddressExclusive > _registerManager.RegisterCount)
        {
            await SendExceptionResponseAsync(
                clientId,
                frame,
                IllegalDataAddressExceptionCode,
                cancellationToken);

            return;
        }

        ushort[] registerValues =
            _registerManager.ReadHoldingRegisters(
                startAddress,
                quantity);

        byte[] response = CreateReadHoldingRegistersResponse(
            frame,
            registerValues);

        await SendResponseAsync(
            clientId,
            response,
            cancellationToken);
    }

    private void OnFrameReceived(
        Guid clientId,
        ModbusTcpFrame frame)
    {
        if (_isDisposed)
        {
            return;
        }

        _ = ProcessSafelyAsync(
            clientId,
            frame);
    }

    private async Task ProcessSafelyAsync(
        Guid clientId,
        ModbusTcpFrame frame)
    {
        try
        {
            await ProcessAsync(
                clientId,
                frame);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Modbus isteği işlenemedi: {exception}");

            try
            {
                await SendExceptionResponseAsync(
                    clientId,
                    frame,
                    ServerDeviceFailureExceptionCode,
                    CancellationToken.None);
            }
            catch (Exception sendException)
            {
                Debug.WriteLine(
                    $"Modbus hata cevabı gönderilemedi: {sendException}");
            }
        }
    }

    private async Task SendExceptionResponseAsync(
        Guid clientId,
        ModbusTcpFrame request,
        byte exceptionCode,
        CancellationToken cancellationToken)
    {
        byte[] response = CreateExceptionResponse(
            request,
            exceptionCode);

        await SendResponseAsync(
            clientId,
            response,
            cancellationToken);
    }

    private async Task SendResponseAsync(
        Guid clientId,
        byte[] response,
        CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            await _tcpServer.SendAsync(
                clientId,
                response,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static byte[] CreateReadHoldingRegistersResponse(
        ModbusTcpFrame request,
        IReadOnlyList<ushort> registerValues)
    {
        int registerByteCount = registerValues.Count * 2;
        ushort length = (ushort)(3 + registerByteCount);
        var response = new byte[9 + registerByteCount];

        WriteUInt16BigEndian(response, 0, request.TransactionId);
        WriteUInt16BigEndian(response, 2, request.ProtocolId);
        WriteUInt16BigEndian(response, 4, length);

        response[6] = request.UnitId;
        response[7] = ReadHoldingRegistersFunctionCode;
        response[8] = (byte)registerByteCount;

        for (int index = 0; index < registerValues.Count; index++)
        {
            WriteUInt16BigEndian(
                response,
                9 + (index * 2),
                registerValues[index]);
        }

        return response;
    }

    private static byte[] CreateExceptionResponse(
        ModbusTcpFrame request,
        byte exceptionCode)
    {
        const ushort length = 3;
        var response = new byte[9];

        WriteUInt16BigEndian(response, 0, request.TransactionId);
        WriteUInt16BigEndian(response, 2, request.ProtocolId);
        WriteUInt16BigEndian(response, 4, length);

        response[6] = request.UnitId;
        response[7] = (byte)(request.FunctionCode | 0x80);
        response[8] = exceptionCode;

        return response;
    }

    private static ushort ReadUInt16BigEndian(
        byte[] bytes,
        int index)
    {
        return (ushort)(
            (bytes[index] << 8) |
            bytes[index + 1]);
    }

    private static void WriteUInt16BigEndian(
        byte[] bytes,
        int index,
        ushort value)
    {
        bytes[index] = (byte)(value >> 8);
        bytes[index + 1] = (byte)value;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _frameAssembler.FrameReceived -= OnFrameReceived;
    }
}
