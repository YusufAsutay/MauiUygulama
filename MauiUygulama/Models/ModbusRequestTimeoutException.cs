namespace MauiUygulama.Models;

public sealed class ModbusRequestTimeoutException : TimeoutException
{
    public ModbusRequestTimeoutException(
        ushort transactionId,
        byte functionCode,
        ushort startAddress,
        int timeoutMilliseconds)
        : base(
            $"Transaction ID {transactionId} için FC{functionCode:X2} isteği " +
            $"{timeoutMilliseconds} ms içinde cevaplanmadı.")
    {
        TransactionId = transactionId;
        FunctionCode = functionCode;
        StartAddress = startAddress;
        TimeoutMilliseconds = timeoutMilliseconds;
    }

    public ushort TransactionId { get; }

    public byte FunctionCode { get; }

    public ushort StartAddress { get; }

    public int TimeoutMilliseconds { get; }
}
