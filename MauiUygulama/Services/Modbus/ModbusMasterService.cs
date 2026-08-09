using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Modbus;

public sealed class ModbusMasterService : IModbusMasterService, IDisposable
{
    private const byte ReadCoilsFunctionCode = 0x01;
    private const byte ReadHoldingRegistersFunctionCode = 0x03;
    private const byte WriteSingleRegisterFunctionCode = 0x06;

    private const int ReceiveBufferSize = 4096;
    private const int MbapHeaderByteCount = 7;

    private readonly ITcpClientService _tcpClient;
    private readonly IModbusMasterResponseStore _responseStore;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _pendingLock = new();
    private readonly object _receiveLoopLock = new();
    private readonly object _ignoredTransactionsLock = new();
    private readonly Dictionary<ushort, long> _ignoredTransactionIds = new();
    private readonly Queue<(ushort TransactionId, long Sequence)>
        _ignoredTransactionOrder = new();

    private ushort _nextTransactionId = 1;
    private PendingOperation? _pendingOperation;
    private CancellationTokenSource? _receiveCancellation;
    private long _ignoredTransactionSequence;
    private bool _isDisposed;

    public ModbusMasterService(
        ITcpClientService tcpClient,
        IModbusMasterResponseStore responseStore)
    {
        _tcpClient = tcpClient;
        _responseStore = responseStore;

        _tcpClient.ConnectionStateChanged += OnTcpConnectionStateChanged;

        if (_tcpClient.IsConnected)
        {
            StartReceiveLoop();
        }
    }

    public bool IsConnected => _tcpClient.IsConnected;

    public event Action<bool>? ConnectionStateChanged;

    public async Task ConnectAsync(
        TcpConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);

        await _connectionLock.WaitAsync(cancellationToken);

        try
        {
            if (_tcpClient.IsConnected)
            {
                return;
            }

            await _tcpClient.ConnectAsync(settings, cancellationToken);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();

        await _connectionLock.WaitAsync();

        try
        {
            StopReceiveLoop();
            FailPendingOperation(
                new InvalidOperationException("TCP bağlantısı kapatıldı."));

            if (_tcpClient.IsConnected)
            {
                await _tcpClient.DisconnectAsync();
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public Task<ModbusMasterOperationResult> ReadCoilsAsync(
        byte unitId,
        ushort startingAddress,
        ushort quantity,
        ModbusRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidateReadRange(
            startingAddress,
            quantity,
            maximumQuantity: 2000,
            quantityName: "Coil");

        return ExecuteAsync(
            unitId,
            ReadCoilsFunctionCode,
            startingAddress,
            quantity,
            options,
            cancellationToken);
    }

    public Task<ModbusMasterOperationResult> ReadHoldingRegistersAsync(
        byte unitId,
        ushort startingAddress,
        ushort quantity,
        ModbusRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidateReadRange(
            startingAddress,
            quantity,
            maximumQuantity: 125,
            quantityName: "Register");

        return ExecuteAsync(
            unitId,
            ReadHoldingRegistersFunctionCode,
            startingAddress,
            quantity,
            options,
            cancellationToken);
    }

    public Task<ModbusMasterOperationResult> WriteSingleHoldingRegisterAsync(
        byte unitId,
        ushort address,
        ushort value,
        ModbusRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            unitId,
            WriteSingleRegisterFunctionCode,
            address,
            value,
            options,
            cancellationToken);
    }

    private async Task<ModbusMasterOperationResult> ExecuteAsync(
        byte unitId,
        byte functionCode,
        ushort startAddress,
        ushort quantityOrValue,
        ModbusRequestOptions? options,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        ModbusRequestOptions optionsSnapshot =
            (options ?? new ModbusRequestOptions()).Copy();

        ValidateRequestOptions(optionsSnapshot);

        // Timeout, işlem servis sırasına girdikten sonra başlar. Kilidi bekleme
        // süresi istek timeout süresinden sayılmaz; kullanıcı iptali ise bekleyen
        // işlemi de hemen sonlandırabilir.
        await _operationLock.WaitAsync(cancellationToken);

        try
        {
            if (!_tcpClient.IsConnected)
            {
                throw new InvalidOperationException(
                    "Önce Slave sunucusuna bağlanmalısınız.");
            }

            ushort transactionId = GetNextTransactionId();
            byte[] request = CreateRequest(
                transactionId,
                unitId,
                functionCode,
                startAddress,
                quantityOrValue);

            var pending = new PendingOperation(
                transactionId,
                unitId,
                functionCode,
                startAddress,
                quantityOrValue,
                request);

            SetPendingOperation(pending);

            using var timeoutCancellation = new CancellationTokenSource(
                optionsSnapshot.TimeoutMilliseconds);
            using var operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCancellation.Token);

            try
            {
                pending.RequestWasSent = true;
                await _tcpClient.SendAsync(
                    request,
                    operationCancellation.Token);

                return await pending.Completion.Task.WaitAsync(
                    operationCancellation.Token);
            }
            catch (OperationCanceledException exception)
                when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    $"Transaction ID {transactionId} için FC{functionCode:X2} " +
                    "isteği kullanıcı veya üst işlem tarafından iptal edildi.",
                    exception,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (timeoutCancellation.IsCancellationRequested)
            {
                throw new ModbusRequestTimeoutException(
                    transactionId,
                    functionCode,
                    startAddress,
                    optionsSnapshot.TimeoutMilliseconds);
            }
            finally
            {
                ClearPendingOperation(pending);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private void StartReceiveLoop()
    {
        lock (_receiveLoopLock)
        {
            if (_isDisposed || !_tcpClient.IsConnected)
            {
                return;
            }

            _receiveCancellation?.Cancel();
            _receiveCancellation?.Dispose();

            _receiveCancellation = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_receiveCancellation.Token);
        }
    }

    private void StopReceiveLoop()
    {
        lock (_receiveLoopLock)
        {
            _receiveCancellation?.Cancel();
            _receiveCancellation?.Dispose();
            _receiveCancellation = null;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        byte[] receiveBuffer = new byte[ReceiveBufferSize];
        var pendingBytes = new List<byte>();

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   _tcpClient.IsConnected)
            {
                int byteCount = await _tcpClient.ReceiveAsync(
                    receiveBuffer,
                    cancellationToken);

                if (byteCount == 0)
                {
                    FailPendingOperation(
                        new IOException("Slave TCP bağlantısını kapattı."));
                    return;
                }

                pendingBytes.AddRange(
                    receiveBuffer.AsSpan(0, byteCount).ToArray());

                ProcessPendingBytes(pendingBytes);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Bağlantı kapatılırken veya servis sonlandırılırken normaldir.
        }
        catch (Exception exception)
        {
            FailPendingOperation(
                new IOException(
                    $"Slave cevabı alınamadı: {exception.Message}",
                    exception));
        }
    }

    private void ProcessPendingBytes(List<byte> pendingBytes)
    {
        while (pendingBytes.Count >= MbapHeaderByteCount)
        {
            ushort protocolId = ReadUInt16BigEndian(pendingBytes, 2);
            ushort length = ReadUInt16BigEndian(pendingBytes, 4);

            if (protocolId != 0)
            {
                pendingBytes.Clear();
                FailPendingOperation(
                    new InvalidDataException(
                        $"Geçersiz Modbus Protocol ID alındı: {protocolId}."));
                return;
            }

            if (length is < 2 or > 254)
            {
                pendingBytes.Clear();
                FailPendingOperation(
                    new InvalidDataException(
                        $"Geçersiz Modbus MBAP Length alındı: {length}."));
                return;
            }

            int totalFrameByteCount = 6 + length;

            if (pendingBytes.Count < totalFrameByteCount)
            {
                return;
            }

            byte[] responseBytes = pendingBytes
                .GetRange(0, totalFrameByteCount)
                .ToArray();

            pendingBytes.RemoveRange(0, totalFrameByteCount);
            ProcessResponse(responseBytes);
        }
    }

    private void ProcessResponse(byte[] response)
    {
        ushort transactionId = ReadUInt16BigEndian(response, 0);

        if (TryConsumeIgnoredTransaction(transactionId))
        {
            // İptal edilmiş bir isteğin gecikmiş cevabı güvenle atlanır.
            return;
        }

        PendingOperation? pending = GetPendingOperation();

        if (pending is null)
        {
            return;
        }

        if (transactionId != pending.TransactionId)
        {
            // Eski veya başka bir isteğe ait çerçeve aktif işlemi bozmamalıdır.
            // Aktif istek kendi Transaction ID cevabını timeout süresine kadar
            // beklemeye devam eder.
            return;
        }

        try
        {
            ModbusMasterOperationResult result =
                ParseAndValidateResponse(response, pending);

            _responseStore.Add(result.Response);
            pending.Completion.TrySetResult(result);
        }
        catch (Exception exception)
        {
            pending.Completion.TrySetException(exception);
        }
    }

    private static ModbusMasterOperationResult ParseAndValidateResponse(
        byte[] response,
        PendingOperation pending)
    {
        if (response.Length < 8)
        {
            throw new InvalidDataException("Eksik Modbus TCP cevabı alındı.");
        }

        ushort transactionId = ReadUInt16BigEndian(response, 0);
        byte unitId = response[6];
        byte functionCode = response[7];

        if (unitId != pending.UnitId)
        {
            throw new InvalidDataException(
                $"Unit ID eşleşmedi. Beklenen: {pending.UnitId}, alınan: {unitId}.");
        }

        if ((functionCode & 0x80) != 0)
        {
            byte exceptionCode = response.Length > 8
                ? response[8]
                : (byte)0;

            throw new InvalidOperationException(
                $"Slave Modbus hata cevabı gönderdi. " +
                $"Exception Code: 0x{exceptionCode:X2} " +
                $"({GetExceptionName(exceptionCode)}).");
        }

        if (functionCode != pending.FunctionCode)
        {
            throw new InvalidDataException(
                $"Transaction ID {transactionId} için " +
                $"0x{pending.FunctionCode:X2} cevabı beklenirken " +
                $"0x{functionCode:X2} alındı.");
        }

        ModbusMasterResponseRecord responseRecord = functionCode switch
        {
            ReadCoilsFunctionCode =>
                ParseReadCoilsResponse(response, pending),

            ReadHoldingRegistersFunctionCode =>
                ParseReadHoldingRegistersResponse(response, pending),

            WriteSingleRegisterFunctionCode =>
                ParseWriteSingleRegisterResponse(response, pending),

            _ => throw new InvalidDataException(
                $"Desteklenmeyen Function Code alındı: 0x{functionCode:X2}.")
        };

        return new ModbusMasterOperationResult(
            transactionId,
            unitId,
            functionCode,
            pending.StartAddress,
            pending.QuantityOrValue,
            BitConverter.ToString(pending.Request).Replace("-", " "),
            responseRecord);
    }

    private static ModbusMasterResponseRecord ParseReadCoilsResponse(
        byte[] response,
        PendingOperation pending)
    {
        ValidateReadResponseHeader(response, pending.FunctionCode);

        int dataByteCount = response[8];
        int expectedByteCount = (pending.QuantityOrValue + 7) / 8;

        if (dataByteCount != expectedByteCount)
        {
            throw new InvalidDataException(
                $"Slave {pending.QuantityOrValue} Coil için " +
                $"{expectedByteCount} byte yerine {dataByteCount} byte döndürdü.");
        }

        var values = new ushort[pending.QuantityOrValue];

        for (int index = 0; index < values.Length; index++)
        {
            int sourceByteIndex = 9 + (index / 8);
            int sourceBitIndex = index % 8;

            values[index] =
                (response[sourceByteIndex] & (1 << sourceBitIndex)) != 0
                    ? (ushort)1
                    : (ushort)0;
        }

        return CreateResponseRecord(response, pending, values);
    }

    private static ModbusMasterResponseRecord ParseReadHoldingRegistersResponse(
        byte[] response,
        PendingOperation pending)
    {
        ValidateReadResponseHeader(response, pending.FunctionCode);

        int dataByteCount = response[8];
        int expectedByteCount = pending.QuantityOrValue * 2;

        if (dataByteCount != expectedByteCount)
        {
            throw new InvalidDataException(
                $"Slave {pending.QuantityOrValue} Register için " +
                $"{expectedByteCount} byte yerine {dataByteCount} byte döndürdü.");
        }

        var values = new ushort[pending.QuantityOrValue];

        for (int index = 0; index < values.Length; index++)
        {
            values[index] = ReadUInt16BigEndian(
                response,
                9 + (index * 2));
        }

        return CreateResponseRecord(response, pending, values);
    }

    private static ModbusMasterResponseRecord ParseWriteSingleRegisterResponse(
        byte[] response,
        PendingOperation pending)
    {
        if (response.Length != 12)
        {
            throw new InvalidDataException(
                $"FC06 cevabı 12 byte olmalıdır; {response.Length} byte alındı.");
        }

        ushort echoedAddress = ReadUInt16BigEndian(response, 8);
        ushort echoedValue = ReadUInt16BigEndian(response, 10);

        if (echoedAddress != pending.StartAddress ||
            echoedValue != pending.QuantityOrValue)
        {
            throw new InvalidDataException(
                "FC06 cevabı gönderilen adres/değer ile eşleşmiyor. " +
                $"Beklenen: {pending.StartAddress}/{pending.QuantityOrValue}, " +
                $"alınan: {echoedAddress}/{echoedValue}.");
        }

        return CreateResponseRecord(
            response,
            pending,
            new[] { echoedValue });
    }

    private static void ValidateReadResponseHeader(
        byte[] response,
        byte functionCode)
    {
        if (response.Length < 9)
        {
            throw new InvalidDataException(
                $"Eksik Function {functionCode:X2} cevabı alındı.");
        }

        int dataByteCount = response[8];

        if (dataByteCount == 0 ||
            response.Length != 9 + dataByteCount)
        {
            throw new InvalidDataException(
                $"Function {functionCode:X2} cevabındaki byte sayısı geçersiz.");
        }
    }

    private static ModbusMasterResponseRecord CreateResponseRecord(
        byte[] response,
        PendingOperation pending,
        ushort[] values)
    {
        return new ModbusMasterResponseRecord
        {
            TransactionId = pending.TransactionId,
            UnitId = pending.UnitId,
            FunctionCode = pending.FunctionCode,
            StartAddress = pending.StartAddress,
            RegisterValues = values,
            ReceivedAt = DateTime.Now,
            Hex = BitConverter.ToString(response).Replace("-", " ")
        };
    }

    private static byte[] CreateRequest(
        ushort transactionId,
        byte unitId,
        byte functionCode,
        ushort startAddress,
        ushort quantityOrValue)
    {
        byte[] request = new byte[12];

        WriteUInt16BigEndian(request, 0, transactionId);
        WriteUInt16BigEndian(request, 2, 0);
        WriteUInt16BigEndian(request, 4, 6);

        request[6] = unitId;
        request[7] = functionCode;

        WriteUInt16BigEndian(request, 8, startAddress);
        WriteUInt16BigEndian(request, 10, quantityOrValue);

        return request;
    }

    private ushort GetNextTransactionId()
    {
        ushort transactionId = _nextTransactionId;

        _nextTransactionId = transactionId == ushort.MaxValue
            ? (ushort)1
            : (ushort)(transactionId + 1);

        // Transaction ID çevrimi sırasında eski iptal kayıtları yeni isteği
        // yanlışlıkla etkilemesin.
        TryConsumeIgnoredTransaction(transactionId);

        return transactionId;
    }

    private void SetPendingOperation(PendingOperation pending)
    {
        lock (_pendingLock)
        {
            if (_pendingOperation is not null)
            {
                throw new InvalidOperationException(
                    "Başka bir Modbus işlemi hâlen devam ediyor.");
            }

            _pendingOperation = pending;
        }
    }

    private PendingOperation? GetPendingOperation()
    {
        lock (_pendingLock)
        {
            return _pendingOperation;
        }
    }

    private void ClearPendingOperation(PendingOperation pending)
    {
        bool shouldIgnoreLateResponse = false;

        lock (_pendingLock)
        {
            if (ReferenceEquals(_pendingOperation, pending))
            {
                _pendingOperation = null;

                shouldIgnoreLateResponse =
                    pending.RequestWasSent &&
                    !pending.Completion.Task.IsCompleted;
            }
        }

        if (shouldIgnoreLateResponse)
        {
            RememberIgnoredTransaction(pending.TransactionId);
        }
    }

    private void RememberIgnoredTransaction(ushort transactionId)
    {
        const int maximumRememberedTransactionCount = 4096;

        lock (_ignoredTransactionsLock)
        {
            long sequence = ++_ignoredTransactionSequence;

            _ignoredTransactionIds[transactionId] = sequence;
            _ignoredTransactionOrder.Enqueue((transactionId, sequence));

            while (_ignoredTransactionOrder.Count >
                   maximumRememberedTransactionCount)
            {
                (ushort oldestTransactionId, long oldestSequence) =
                    _ignoredTransactionOrder.Dequeue();

                if (_ignoredTransactionIds.TryGetValue(
                        oldestTransactionId,
                        out long currentSequence) &&
                    currentSequence == oldestSequence)
                {
                    _ignoredTransactionIds.Remove(oldestTransactionId);
                }
            }
        }
    }

    private bool TryConsumeIgnoredTransaction(ushort transactionId)
    {
        lock (_ignoredTransactionsLock)
        {
            return _ignoredTransactionIds.Remove(transactionId);
        }
    }

    private static void ValidateRequestOptions(
        ModbusRequestOptions options)
    {
        if (options.TimeoutMilliseconds is
            < ModbusRequestOptions.MinimumTimeoutMilliseconds or
            > ModbusRequestOptions.MaximumTimeoutMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.TimeoutMilliseconds),
                $"İstek timeout değeri " +
                $"{ModbusRequestOptions.MinimumTimeoutMilliseconds} ile " +
                $"{ModbusRequestOptions.MaximumTimeoutMilliseconds} ms " +
                "arasında olmalıdır.");
        }
    }

    private void FailPendingOperation(Exception exception)
    {
        PendingOperation? pending;

        lock (_pendingLock)
        {
            pending = _pendingOperation;
            _pendingOperation = null;
        }

        pending?.Completion.TrySetException(exception);
    }

    private void OnTcpConnectionStateChanged(bool isConnected)
    {
        if (isConnected)
        {
            StartReceiveLoop();
        }
        else
        {
            StopReceiveLoop();
            FailPendingOperation(
                new IOException("Slave TCP bağlantısı kesildi."));
        }

        ConnectionStateChanged?.Invoke(isConnected);
    }

    private static void ValidateReadRange(
        ushort startingAddress,
        ushort quantity,
        ushort maximumQuantity,
        string quantityName)
    {
        if (quantity < 1 || quantity > maximumQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                $"{quantityName} miktarı 1 ile {maximumQuantity} arasında olmalıdır.");
        }

        int lastAddress = startingAddress + quantity - 1;

        if (lastAddress > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                $"Başlangıç adresi ile {quantityName.ToLowerInvariant()} miktarı " +
                "geçerli adres aralığını aşıyor.");
        }
    }

    private static string GetExceptionName(byte exceptionCode)
    {
        return exceptionCode switch
        {
            0x01 => "Illegal Function",
            0x02 => "Illegal Data Address",
            0x03 => "Illegal Data Value",
            0x04 => "Server Device Failure",
            _ => "Bilinmeyen hata"
        };
    }

    private static ushort ReadUInt16BigEndian(
        IReadOnlyList<byte> bytes,
        int index)
    {
        return (ushort)(
            (bytes[index] << 8) |
            bytes[index + 1]);
    }

    private static void WriteUInt16BigEndian(
        byte[] destination,
        int index,
        ushort value)
    {
        destination[index] = (byte)(value >> 8);
        destination[index + 1] = (byte)value;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _tcpClient.ConnectionStateChanged -= OnTcpConnectionStateChanged;

        StopReceiveLoop();
        FailPendingOperation(
            new ObjectDisposedException(nameof(ModbusMasterService)));

        _connectionLock.Dispose();
        _operationLock.Dispose();
    }

    private sealed class PendingOperation
    {
        public PendingOperation(
            ushort transactionId,
            byte unitId,
            byte functionCode,
            ushort startAddress,
            ushort quantityOrValue,
            byte[] request)
        {
            TransactionId = transactionId;
            UnitId = unitId;
            FunctionCode = functionCode;
            StartAddress = startAddress;
            QuantityOrValue = quantityOrValue;
            Request = request;
        }

        public ushort TransactionId { get; }
        public byte UnitId { get; }
        public byte FunctionCode { get; }
        public ushort StartAddress { get; }
        public ushort QuantityOrValue { get; }
        public byte[] Request { get; }
        public bool RequestWasSent { get; set; }

        public TaskCompletionSource<ModbusMasterOperationResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
