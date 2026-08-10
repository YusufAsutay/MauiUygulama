using System.Diagnostics;
using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Modbus;

public sealed class ModbusRequestProcessor :
    IModbusRequestProcessor,
    IDisposable
{
    private const byte ReadCoilsFunctionCode = 0x01;
    private const byte ReadHoldingRegistersFunctionCode = 0x03;
    private const byte WriteSingleRegisterFunctionCode = 0x06;

    private const ushort MinimumReadQuantity = 1;
    private const ushort MaximumReadCoilQuantity = 2000;
    private const ushort MaximumReadRegisterQuantity = 125;

    private readonly IModbusTcpFrameAssembler _frameAssembler;
    private readonly ITcpServerService _tcpServer;
    private readonly IRegisterManager _registerManager;
    private readonly ISlaveRegisterViewStore
        _slaveRegisterViewStore;

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private bool _isDisposed;

    public ModbusRequestProcessor(
        IModbusTcpFrameAssembler frameAssembler,
        ITcpServerService tcpServer,
        IRegisterManager registerManager,
        ISlaveRegisterViewStore slaveRegisterViewStore)
    {
        _frameAssembler = frameAssembler;
        _tcpServer = tcpServer;
        _registerManager = registerManager;
        _slaveRegisterViewStore = slaveRegisterViewStore;

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

        switch (frame.FunctionCode)
        {
            case ReadCoilsFunctionCode:
                await ProcessReadCoilsAsync(
                    clientId,
                    frame,
                    cancellationToken);
                break;

            case ReadHoldingRegistersFunctionCode:
                await ProcessReadHoldingRegistersAsync(
                    clientId,
                    frame,
                    cancellationToken);
                break;

            case WriteSingleRegisterFunctionCode:
                await ProcessWriteSingleRegisterAsync(
                    clientId,
                    frame,
                    cancellationToken);
                break;

            default:
                await SendExceptionResponseAsync(
                    clientId,
                    frame,
                    ModbusExceptionCode.IllegalFunction,
                    cancellationToken);
                break;
        }
    }

    private async Task ProcessReadCoilsAsync(
        Guid clientId,
        ModbusTcpFrame frame,
        CancellationToken cancellationToken)
    {
        if (!TryReadAddressAndQuantity(
                frame,
                out ushort startAddress,
                out ushort quantity))
        {
            await SendExceptionResponseAsync(
                clientId,
                frame,
                ModbusExceptionCode.IllegalDataValue,
                cancellationToken);

            return;
        }

        if (quantity < MinimumReadQuantity ||
            quantity > MaximumReadCoilQuantity)
        {
            await SendExceptionResponseAsync(
                clientId,
                frame,
                ModbusExceptionCode.IllegalDataValue,
                cancellationToken);

            return;
        }

        /*
         * Aralığın yalnızca sayısal olarak geçerli olması
         * yeterli değildir. Bütün Coil adreslerinin Slave
         * üzerinde tanımlanmış olması gerekir.
         */
        if (!_registerManager.TryReadCoils(
                startAddress,
                quantity,
                out bool[] coilValues))
        {
            await SendExceptionResponseAsync(
                clientId,
                frame,
                ModbusExceptionCode.IllegalDataAddress,
                cancellationToken);

            return;
        }

        /*
         * Yalnızca başarıyla okunmuş, önceden tanımlı
         * adresler Slave tablosuna kaydedilir.
         */
        _slaveRegisterViewStore.RecordSuccessfulRange(
            RegisterArea.Coil,
            startAddress,
            quantity,
            functionCode: ReadCoilsFunctionCode);

        byte[] response = CreateReadCoilsResponse(
            frame,
            coilValues);

        await SendResponseAsync(
            clientId,
            response,
            cancellationToken);
    }

    private async Task ProcessReadHoldingRegistersAsync(
        Guid clientId,
        ModbusTcpFrame frame,
        CancellationToken cancellationToken)
    {
        if (!TryReadAddressAndQuantity(
                frame,
                out ushort startAddress,
                out ushort quantity))
        {
            await SendExceptionResponseAsync(
                clientId,
                frame,
                ModbusExceptionCode.IllegalDataValue,
                cancellationToken);

            return;
        }

        if (quantity < MinimumReadQuantity ||
            quantity > MaximumReadRegisterQuantity)
        {
            await SendExceptionResponseAsync(
                clientId,
                frame,
                ModbusExceptionCode.IllegalDataValue,
                cancellationToken);

            return;
        }

        /*
         * Bütün Holding Register adreslerinin Slave üzerinde
         * gerçekten tanımlanmış olup olmadığını kontrol eder.
         */
        if (!_registerManager.TryReadHoldingRegisters(
                startAddress,
                quantity,
                out ushort[] registerValues))
        {
            await SendExceptionResponseAsync(
                clientId,
                frame,
                ModbusExceptionCode.IllegalDataAddress,
                cancellationToken);

            return;
        }

        _slaveRegisterViewStore.RecordSuccessfulRange(
            RegisterArea.HoldingRegister,
            startAddress,
            quantity,
            functionCode:
                ReadHoldingRegistersFunctionCode);

        byte[] response = CreateReadRegistersResponse(
            frame,
            registerValues);

        await SendResponseAsync(
            clientId,
            response,
            cancellationToken);
    }

    private async Task ProcessWriteSingleRegisterAsync(
        Guid clientId,
        ModbusTcpFrame frame,
        CancellationToken cancellationToken)
    {
        if (!TryReadAddressAndValue(
                frame,
                out ushort address,
                out ushort value))
        {
            await SendExceptionResponseAsync(
                clientId,
                frame,
                ModbusExceptionCode.IllegalDataValue,
                cancellationToken);

            return;
        }

        /*
         * Yalnızca önceden tanımlanmış Holding Register
         * adresine yazılabilir. Tanımsız adres oluşturulmaz.
         */
        if (!_registerManager.TryWriteSingleHoldingRegister(
                address,
                value))
        {
            await SendExceptionResponseAsync(
                clientId,
                frame,
                ModbusExceptionCode.IllegalDataAddress,
                cancellationToken);

            return;
        }

        _slaveRegisterViewStore.RecordSuccessfulAddress(
            RegisterArea.HoldingRegister,
            address,
            functionCode:
                WriteSingleRegisterFunctionCode);

        byte[] response =
            CreateWriteSingleRegisterResponse(
                frame,
                address,
                value);

        await SendResponseAsync(
            clientId,
            response,
            cancellationToken);
    }

    private static bool TryReadAddressAndQuantity(
        ModbusTcpFrame frame,
        out ushort startAddress,
        out ushort quantity)
    {
        startAddress = 0;
        quantity = 0;

        if (frame.Data.Length != 4)
        {
            return false;
        }

        startAddress =
            ReadUInt16BigEndian(frame.Data, 0);

        quantity =
            ReadUInt16BigEndian(frame.Data, 2);

        return true;
    }

    private static bool TryReadAddressAndValue(
        ModbusTcpFrame frame,
        out ushort address,
        out ushort value)
    {
        address = 0;
        value = 0;

        if (frame.Data.Length != 4)
        {
            return false;
        }

        address =
            ReadUInt16BigEndian(frame.Data, 0);

        value =
            ReadUInt16BigEndian(frame.Data, 2);

        return true;
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
                    ModbusExceptionCode.SlaveDeviceFailure,
                    CancellationToken.None);
            }
            catch (Exception sendException)
            {
                Debug.WriteLine(
                    "Modbus hata cevabı gönderilemedi: " +
                    sendException);
            }
        }
    }

    private async Task SendExceptionResponseAsync(
        Guid clientId,
        ModbusTcpFrame request,
        ModbusExceptionCode exceptionCode,
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
        await _sendLock.WaitAsync(
            cancellationToken);

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

    private static byte[] CreateReadRegistersResponse(
        ModbusTcpFrame request,
        IReadOnlyList<ushort> registerValues)
    {
        int registerByteCount =
            registerValues.Count * 2;

        ushort length =
            (ushort)(3 + registerByteCount);

        var response =
            new byte[9 + registerByteCount];

        WriteUInt16BigEndian(
            response,
            0,
            request.TransactionId);

        WriteUInt16BigEndian(
            response,
            2,
            request.ProtocolId);

        WriteUInt16BigEndian(
            response,
            4,
            length);

        response[6] = request.UnitId;
        response[7] = request.FunctionCode;
        response[8] = (byte)registerByteCount;

        for (int index = 0;
             index < registerValues.Count;
             index++)
        {
            WriteUInt16BigEndian(
                response,
                9 + (index * 2),
                registerValues[index]);
        }

        return response;
    }

    private static byte[] CreateReadCoilsResponse(
        ModbusTcpFrame request,
        IReadOnlyList<bool> coilValues)
    {
        int coilByteCount =
            (coilValues.Count + 7) / 8;

        ushort length =
            (ushort)(3 + coilByteCount);

        var response =
            new byte[9 + coilByteCount];

        WriteUInt16BigEndian(
            response,
            0,
            request.TransactionId);

        WriteUInt16BigEndian(
            response,
            2,
            request.ProtocolId);

        WriteUInt16BigEndian(
            response,
            4,
            length);

        response[6] = request.UnitId;
        response[7] = request.FunctionCode;
        response[8] = (byte)coilByteCount;

        for (int index = 0;
             index < coilValues.Count;
             index++)
        {
            if (!coilValues[index])
            {
                continue;
            }

            int targetByteIndex =
                9 + (index / 8);

            int targetBitIndex =
                index % 8;

            response[targetByteIndex] |=
                (byte)(1 << targetBitIndex);
        }

        return response;
    }

    private static byte[]
        CreateWriteSingleRegisterResponse(
            ModbusTcpFrame request,
            ushort address,
            ushort value)
    {
        const ushort length = 6;

        var response = new byte[12];

        WriteUInt16BigEndian(
            response,
            0,
            request.TransactionId);

        WriteUInt16BigEndian(
            response,
            2,
            request.ProtocolId);

        WriteUInt16BigEndian(
            response,
            4,
            length);

        response[6] = request.UnitId;
        response[7] =
            WriteSingleRegisterFunctionCode;

        WriteUInt16BigEndian(
            response,
            8,
            address);

        WriteUInt16BigEndian(
            response,
            10,
            value);

        return response;
    }

    private static byte[] CreateExceptionResponse(
        ModbusTcpFrame request,
        ModbusExceptionCode exceptionCode)
    {
        const ushort length = 3;

        var response = new byte[9];

        WriteUInt16BigEndian(
            response,
            0,
            request.TransactionId);

        WriteUInt16BigEndian(
            response,
            2,
            request.ProtocolId);

        WriteUInt16BigEndian(
            response,
            4,
            length);

        response[6] = request.UnitId;

        response[7] =
            (byte)(request.FunctionCode | 0x80);

        response[8] = (byte)exceptionCode;

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
        bytes[index] =
            (byte)(value >> 8);

        bytes[index + 1] =
            (byte)value;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _frameAssembler.FrameReceived -=
            OnFrameReceived;

        _sendLock.Dispose();
    }
}