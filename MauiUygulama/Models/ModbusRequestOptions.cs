namespace MauiUygulama.Models;

public sealed class ModbusRequestOptions
{
    public const int MinimumTimeoutMilliseconds = 100;
    public const int MaximumTimeoutMilliseconds = 60000;
    public const int DefaultTimeoutMilliseconds = 3000;

    public int TimeoutMilliseconds { get; set; } =
        DefaultTimeoutMilliseconds;

    public ModbusRequestOptions Copy()
    {
        return new ModbusRequestOptions
        {
            TimeoutMilliseconds = TimeoutMilliseconds
        };
    }
}
