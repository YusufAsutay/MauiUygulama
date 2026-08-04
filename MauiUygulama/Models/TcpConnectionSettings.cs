namespace MauiUygulama.Models;

public sealed class TcpConnectionSettings
{
    public string IpAddress { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 502;

    public int ConnectionTimeoutMilliseconds { get; set; } = 5000;
}