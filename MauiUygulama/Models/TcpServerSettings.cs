namespace MauiUygulama.Models;

public sealed class TcpServerSettings
{
    public string IpAddress { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 1502;

    public int Backlog { get; set; } = 100;

    public int ReceiveBufferSize { get; set; } = 4096;
}