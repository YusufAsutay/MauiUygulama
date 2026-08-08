namespace MauiUygulama.Models;

public sealed record ModbusMasterOperationResult(
    ushort TransactionId,
    byte UnitId,
    byte FunctionCode,
    ushort StartAddress,
    ushort QuantityOrValue,
    string RequestHex,
    ModbusMasterResponseRecord Response);
