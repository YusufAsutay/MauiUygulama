namespace MauiUygulama.Models;

public sealed record RegisterValueChangedEventArgs(
    RegisterArea Area,
    ushort Address,
    ushort Value);
