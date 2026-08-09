namespace MauiUygulama.Models;

public sealed record ModbusPollingStatus(
    bool IsRunning,
    byte FunctionCode,
    byte UnitId,
    ushort StartingAddress,
    ushort Quantity,
    int IntervalMilliseconds,
    int RequestTimeoutMilliseconds,
    long SuccessfulPollCount,
    long FailedPollCount,
    long TimedOutPollCount,
    DateTime? StartedAt,
    DateTime? LastAttemptAt,
    DateTime? LastSuccessfulPollAt,
    string? LastError,
    ModbusMasterOperationResult? LastResult)
{
    public static ModbusPollingStatus Idle { get; } = new(
        IsRunning: false,
        FunctionCode: 0x03,
        UnitId: 1,
        StartingAddress: 0,
        Quantity: 1,
        IntervalMilliseconds: 1000,
        RequestTimeoutMilliseconds:
            ModbusRequestOptions.DefaultTimeoutMilliseconds,
        SuccessfulPollCount: 0,
        FailedPollCount: 0,
        TimedOutPollCount: 0,
        StartedAt: null,
        LastAttemptAt: null,
        LastSuccessfulPollAt: null,
        LastError: null,
        LastResult: null);
}
