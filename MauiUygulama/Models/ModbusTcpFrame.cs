using System;
using System.Collections.Generic;
using System.Text;

namespace MauiUygulama.Models;

public sealed class ModbusTcpFrame
{
    public ushort TransactionId { get; init; }

    public ushort ProtocolId { get; init; }

    public ushort Length { get; init; }

    public byte UnitId { get; init; }

    public byte FunctionCode { get; init; }

    // Function Code dâhil PDU bölümü
    public byte[] Pdu { get; init; } = Array.Empty<byte>();

    // Function Code'dan sonraki veri bölümü
    public byte[] Data { get; init; } = Array.Empty<byte>();

    // MBAP Header ve PDU dâhil paketin tamamı
    public byte[] RawData { get; init; } = Array.Empty<byte>();

    public int TotalByteCount => RawData.Length;

    public string Hex =>
        BitConverter.ToString(RawData).Replace("-", " ");
}