namespace MauiUygulama.Services.Modbus;

public sealed class ModbusExceptionResponseException : Exception
{
    public ModbusExceptionResponseException(
        ushort transactionId,
        byte unitId,
        byte requestFunctionCode,
        byte exceptionFunctionCode,
        ModbusExceptionCode exceptionCode)
        : base(CreateMessage(
            transactionId,
            unitId,
            requestFunctionCode,
            exceptionCode))
    {
        TransactionId = transactionId;
        UnitId = unitId;
        RequestFunctionCode = requestFunctionCode;
        ExceptionFunctionCode = exceptionFunctionCode;
        ExceptionCode = exceptionCode;
    }

    public ushort TransactionId { get; }

    public byte UnitId { get; }

    public byte RequestFunctionCode { get; }

    public byte ExceptionFunctionCode { get; }

    public ModbusExceptionCode ExceptionCode { get; }

    public string ExceptionName => GetExceptionName(ExceptionCode);

    public string Description => GetExceptionDescription(ExceptionCode);

    private static string CreateMessage(
        ushort transactionId,
        byte unitId,
        byte requestFunctionCode,
        ModbusExceptionCode exceptionCode)
    {
        return
            $"FC{requestFunctionCode:X2} işlemi başarısız. " +
            $"{GetExceptionName(exceptionCode)} " +
            $"(0x{(byte)exceptionCode:X2}): " +
            $"{GetExceptionDescription(exceptionCode)} " +
            $"Transaction ID: {transactionId}, Unit ID: {unitId}.";
    }

    private static string GetExceptionName(
        ModbusExceptionCode exceptionCode)
    {
        return exceptionCode switch
        {
            ModbusExceptionCode.IllegalFunction =>
                "Illegal Function",

            ModbusExceptionCode.IllegalDataAddress =>
                "Illegal Data Address",

            ModbusExceptionCode.IllegalDataValue =>
                "Illegal Data Value",

            ModbusExceptionCode.SlaveDeviceFailure =>
                "Slave Device Failure",

            _ => "Bilinmeyen Modbus Exception"
        };
    }

    private static string GetExceptionDescription(
        ModbusExceptionCode exceptionCode)
    {
        return exceptionCode switch
        {
            ModbusExceptionCode.IllegalFunction =>
                "Slave istenen Function Code'u desteklemiyor.",

            ModbusExceptionCode.IllegalDataAddress =>
                "İstenen adres veya adres aralığı Slave'de tanımlı değil.",

            ModbusExceptionCode.IllegalDataValue =>
                "İstekteki miktar veya değer geçerli değil.",

            ModbusExceptionCode.SlaveDeviceFailure =>
                "Slave isteği işlerken beklenmeyen bir hata oluştu.",

            _ =>
                "Slave tanınmayan bir Modbus exception kodu gönderdi."
        };
    }
}