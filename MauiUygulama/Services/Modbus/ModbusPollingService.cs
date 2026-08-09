using MauiUygulama.Core.Interfaces;
using MauiUygulama.Models;

namespace MauiUygulama.Services.Modbus;

public sealed class ModbusPollingService : IModbusPollingService, IDisposable
{
    private const byte ReadCoilsFunctionCode = 0x01;
    private const byte ReadHoldingRegistersFunctionCode = 0x03;
    private const int MinimumIntervalMilliseconds = 200;
    private const int MaximumIntervalMilliseconds = 60000;

    private readonly IModbusMasterService _modbusMaster;
    private readonly object _syncRoot = new();

    private ModbusPollingStatus _status = ModbusPollingStatus.Idle;
    private CancellationTokenSource? _pollingCancellation;
    private Task? _pollingTask;
    private bool _isDisposed;

    public ModbusPollingService(IModbusMasterService modbusMaster)
    {
        _modbusMaster = modbusMaster;
        _modbusMaster.ConnectionStateChanged += OnMasterConnectionStateChanged;
    }

    public ModbusPollingStatus Status
    {
        get
        {
            lock (_syncRoot)
            {
                return _status;
            }
        }
    }

    public event Action? StatusChanged;

    public Task StartAsync(
        ModbusPollingSettings settings,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateSettings(settings);

        if (!_modbusMaster.IsConnected)
        {
            throw new InvalidOperationException(
                "Polling başlatılmadan önce Slave sunucusuna bağlanılmalıdır.");
        }

        ModbusPollingSettings settingsSnapshot = settings.Copy();

        lock (_syncRoot)
        {
            if (_pollingCancellation is not null)
            {
                throw new InvalidOperationException(
                    "Başka bir polling işlemi zaten çalışıyor.");
            }

            _pollingCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _status = new ModbusPollingStatus(
                IsRunning: true,
                FunctionCode: settingsSnapshot.FunctionCode,
                UnitId: settingsSnapshot.UnitId,
                StartingAddress: settingsSnapshot.StartingAddress,
                Quantity: settingsSnapshot.Quantity,
                IntervalMilliseconds: settingsSnapshot.IntervalMilliseconds,
                RequestTimeoutMilliseconds:
                    settingsSnapshot.RequestTimeoutMilliseconds,
                SuccessfulPollCount: 0,
                FailedPollCount: 0,
                TimedOutPollCount: 0,
                StartedAt: DateTime.Now,
                LastAttemptAt: null,
                LastSuccessfulPollAt: null,
                LastError: null,
                LastResult: null);

            _pollingTask = RunPollingLoopAsync(
                settingsSnapshot,
                _pollingCancellation);
        }

        NotifyStatusChanged();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? pollingTask;

        lock (_syncRoot)
        {
            cancellation = _pollingCancellation;
            pollingTask = _pollingTask;
        }

        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (pollingTask is not null)
        {
            try
            {
                await pollingTask;
            }
            catch (OperationCanceledException)
            {
                // Polling durdurulurken beklenen durumdur.
            }
        }
    }

    private async Task RunPollingLoopAsync(
        ModbusPollingSettings settings,
        CancellationTokenSource cancellation)
    {
        // StartAsync içindeki görev alanlarının atanmasına fırsat verir.
        await Task.Yield();

        CancellationToken cancellationToken = cancellation.Token;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_modbusMaster.IsConnected)
                {
                    RecordFailure(
                        "TCP bağlantısı kesildiği için polling durduruldu.");
                    break;
                }

                try
                {
                    ModbusMasterOperationResult result =
                        await ExecuteReadAsync(settings, cancellationToken);

                    RecordSuccess(result);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ModbusRequestTimeoutException exception)
                {
                    RecordTimeout(exception.Message);
                }
                catch (Exception exception)
                {
                    RecordFailure(exception.Message);

                    if (!_modbusMaster.IsConnected)
                    {
                        break;
                    }
                }

                await Task.Delay(
                    settings.IntervalMilliseconds,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // StopAsync veya bağlantı kaybı polling döngüsünü iptal etti.
        }
        finally
        {
            lock (_syncRoot)
            {
                if (ReferenceEquals(_pollingCancellation, cancellation))
                {
                    _pollingCancellation = null;
                    _pollingTask = null;
                    _status = _status with { IsRunning = false };
                }
            }

            cancellation.Dispose();
            NotifyStatusChanged();
        }
    }

    private Task<ModbusMasterOperationResult> ExecuteReadAsync(
        ModbusPollingSettings settings,
        CancellationToken cancellationToken)
    {
        var requestOptions = new ModbusRequestOptions
        {
            TimeoutMilliseconds = settings.RequestTimeoutMilliseconds
        };

        return settings.FunctionCode switch
        {
            ReadCoilsFunctionCode =>
                _modbusMaster.ReadCoilsAsync(
                    settings.UnitId,
                    settings.StartingAddress,
                    settings.Quantity,
                    requestOptions,
                    cancellationToken),

            ReadHoldingRegistersFunctionCode =>
                _modbusMaster.ReadHoldingRegistersAsync(
                    settings.UnitId,
                    settings.StartingAddress,
                    settings.Quantity,
                    requestOptions,
                    cancellationToken),

            _ => throw new InvalidOperationException(
                "Polling yalnızca FC01 ve FC03 için kullanılabilir.")
        };
    }

    private void RecordSuccess(ModbusMasterOperationResult result)
    {
        lock (_syncRoot)
        {
            DateTime completedAt = DateTime.Now;

            _status = _status with
            {
                SuccessfulPollCount = _status.SuccessfulPollCount + 1,
                LastAttemptAt = completedAt,
                LastSuccessfulPollAt = completedAt,
                LastError = null,
                LastResult = result
            };
        }

        NotifyStatusChanged();
    }

    private void RecordFailure(string errorMessage)
    {
        lock (_syncRoot)
        {
            _status = _status with
            {
                FailedPollCount = _status.FailedPollCount + 1,
                LastAttemptAt = DateTime.Now,
                LastError = errorMessage
            };
        }

        NotifyStatusChanged();
    }

    private void RecordTimeout(string errorMessage)
    {
        lock (_syncRoot)
        {
            _status = _status with
            {
                FailedPollCount = _status.FailedPollCount + 1,
                TimedOutPollCount = _status.TimedOutPollCount + 1,
                LastAttemptAt = DateTime.Now,
                LastError = errorMessage
            };
        }

        NotifyStatusChanged();
    }

    private static void ValidateSettings(ModbusPollingSettings settings)
    {
        ushort maximumQuantity = settings.FunctionCode switch
        {
            ReadCoilsFunctionCode => 2000,
            ReadHoldingRegistersFunctionCode => 125,
            _ => throw new ArgumentOutOfRangeException(
                nameof(settings.FunctionCode),
                "Polling fonksiyonu FC01 veya FC03 olmalıdır.")
        };

        if (settings.Quantity < 1 || settings.Quantity > maximumQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings.Quantity),
                $"Miktar 1 ile {maximumQuantity} arasında olmalıdır.");
        }

        int lastAddress = settings.StartingAddress + settings.Quantity - 1;

        if (lastAddress > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings.Quantity),
                "Başlangıç adresi ve miktar geçerli adres aralığını aşıyor.");
        }

        if (settings.IntervalMilliseconds is
            < MinimumIntervalMilliseconds or
            > MaximumIntervalMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings.IntervalMilliseconds),
                $"Polling aralığı {MinimumIntervalMilliseconds} ile " +
                $"{MaximumIntervalMilliseconds} ms arasında olmalıdır.");
        }

        if (settings.RequestTimeoutMilliseconds is
            < ModbusRequestOptions.MinimumTimeoutMilliseconds or
            > ModbusRequestOptions.MaximumTimeoutMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings.RequestTimeoutMilliseconds),
                $"Polling istek timeout değeri " +
                $"{ModbusRequestOptions.MinimumTimeoutMilliseconds} ile " +
                $"{ModbusRequestOptions.MaximumTimeoutMilliseconds} ms " +
                "arasında olmalıdır.");
        }
    }

    private void NotifyStatusChanged()
    {
        Action? handlers = StatusChanged;

        if (handlers is null)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch
            {
                // Bir ekran dinleyicisinin hatası polling döngüsünü durdurmamalıdır.
            }
        }
    }

    private void OnMasterConnectionStateChanged(bool isConnected)
    {
        if (isConnected)
        {
            return;
        }

        CancellationTokenSource? cancellation;

        lock (_syncRoot)
        {
            cancellation = _pollingCancellation;

            if (cancellation is null)
            {
                return;
            }

            _status = _status with
            {
                FailedPollCount = _status.FailedPollCount + 1,
                LastAttemptAt = DateTime.Now,
                LastError = "TCP bağlantısı kesildiği için polling durduruldu."
            };
        }

        NotifyStatusChanged();

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Polling döngüsü aynı anda tamamlandıysa ek işlem gerekmez.
        }
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
        _modbusMaster.ConnectionStateChanged -= OnMasterConnectionStateChanged;

        CancellationTokenSource? cancellation;

        lock (_syncRoot)
        {
            cancellation = _pollingCancellation;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Polling döngüsü aynı anda tamamlandıysa ek işlem gerekmez.
        }
    }
}
