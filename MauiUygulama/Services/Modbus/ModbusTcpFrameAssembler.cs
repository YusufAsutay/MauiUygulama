using System.Collections.Concurrent;
using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Modbus;

public sealed class ModbusTcpFrameAssembler :
    IModbusTcpFrameAssembler
{
    private const int MbapPrefixSize = 6;
    private const int MinimumLengthValue = 2;
    private const int MaximumLengthValue = 254;

    private readonly ITcpServerService _tcpServer;

    private readonly ConcurrentDictionary<Guid, ClientBuffer>
        _clientBuffers = new();

    private bool _isDisposed;

    public ModbusTcpFrameAssembler(
        ITcpServerService tcpServer)
    {
        _tcpServer = tcpServer;

        _tcpServer.DataReceived += OnDataReceived;
        _tcpServer.ClientDisconnected += OnClientDisconnected;
    }

    public event Action<Guid, ModbusTcpFrame>? FrameReceived;

    public event Action<Guid, string>? FrameError;

    private void OnDataReceived(
        Guid clientId,
        ReadOnlyMemory<byte> receivedData)
    {
        if (_isDisposed || receivedData.IsEmpty)
        {
            return;
        }

        ClientBuffer clientBuffer =
            _clientBuffers.GetOrAdd(
                clientId,
                _ => new ClientBuffer());

        var completedFrames = new List<ModbusTcpFrame>();
        string? errorMessage = null;

        lock (clientBuffer.SyncRoot)
        {
            clientBuffer.Bytes.AddRange(receivedData.ToArray());

            while (true)
            {
                // Transaction ID, Protocol ID ve Length alanlarının
                // tamamlanmasını bekliyoruz.
                if (clientBuffer.Bytes.Count < MbapPrefixSize)
                {
                    break;
                }

                ushort protocolId = ReadUInt16BigEndian(
                    clientBuffer.Bytes,
                    2);

                ushort length = ReadUInt16BigEndian(
                    clientBuffer.Bytes,
                    4);

                if (protocolId != 0)
                {
                    errorMessage =
                        $"Geçersiz Protocol ID: {protocolId}. " +
                        "Modbus TCP için Protocol ID değeri 0 olmalıdır.";

                    clientBuffer.Bytes.Clear();
                    break;
                }

                if (length is < MinimumLengthValue
                    or > MaximumLengthValue)
                {
                    errorMessage =
                        $"Geçersiz MBAP Length değeri: {length}.";

                    clientBuffer.Bytes.Clear();
                    break;
                }

                // Length alanı, kendisinden sonra gelen byte sayısıdır.
                int totalFrameLength =
                    MbapPrefixSize + length;

                // Paket parçalanmışsa kalan byte'ları bekle.
                if (clientBuffer.Bytes.Count < totalFrameLength)
                {
                    break;
                }

                byte[] rawFrame = clientBuffer.Bytes
                    .GetRange(0, totalFrameLength)
                    .ToArray();

                clientBuffer.Bytes.RemoveRange(
                    0,
                    totalFrameLength);

                completedFrames.Add(
                    CreateFrame(rawFrame));
            }
        }

        if (errorMessage is not null)
        {
            FrameError?.Invoke(clientId, errorMessage);
        }

        foreach (ModbusTcpFrame frame in completedFrames)
        {
            FrameReceived?.Invoke(clientId, frame);
        }
    }

    private static ModbusTcpFrame CreateFrame(
        byte[] rawFrame)
    {
        ushort transactionId =
            ReadUInt16BigEndian(rawFrame, 0);

        ushort protocolId =
            ReadUInt16BigEndian(rawFrame, 2);

        ushort length =
            ReadUInt16BigEndian(rawFrame, 4);

        byte unitId = rawFrame[6];
        byte functionCode = rawFrame[7];

        byte[] pdu = rawFrame
            .AsSpan(7)
            .ToArray();

        byte[] data = rawFrame.Length > 8
            ? rawFrame.AsSpan(8).ToArray()
            : Array.Empty<byte>();

        return new ModbusTcpFrame
        {
            TransactionId = transactionId,
            ProtocolId = protocolId,
            Length = length,
            UnitId = unitId,
            FunctionCode = functionCode,
            Pdu = pdu,
            Data = data,
            RawData = rawFrame
        };
    }

    private void OnClientDisconnected(
        Guid clientId,
        string remoteEndPoint)
    {
        _clientBuffers.TryRemove(clientId, out _);
    }

    private static ushort ReadUInt16BigEndian(
        IReadOnlyList<byte> bytes,
        int index)
    {
        return (ushort)(
            (bytes[index] << 8) |
            bytes[index + 1]);
    }

    private static ushort ReadUInt16BigEndian(
        byte[] bytes,
        int index)
    {
        return (ushort)(
            (bytes[index] << 8) |
            bytes[index + 1]);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _tcpServer.DataReceived -= OnDataReceived;
        _tcpServer.ClientDisconnected -= OnClientDisconnected;

        _clientBuffers.Clear();
        _isDisposed = true;
    }

    private sealed class ClientBuffer
    {
        public object SyncRoot { get; } = new();

        public List<byte> Bytes { get; } = new();
    }
}