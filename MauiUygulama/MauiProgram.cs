using Microsoft.Extensions.Logging;
using MauiUygulama.ViewModels;
using MauiUygulama.Core.Interfaces;
using MauiUygulama.Services.Communication;
using MauiUygulama.Services.Modbus;
using Microsoft.Extensions.DependencyInjection;

namespace MauiUygulama;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont(
                    "OpenSans-Regular.ttf",
                    "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

        // Uygulamanın kullandığı servisler Debug ve Release'te kayıtlı olmalıdır.
        builder.Services.AddSingleton<ModeSelectionViewModel>();
        builder.Services.AddSingleton<ITcpClientService, TcpClientService>();
        builder.Services.AddSingleton<ITcpServerService, TcpServerService>();

        builder.Services.AddSingleton<
            ITcpReceivedDataStore,
            TcpReceivedDataStore>();

        builder.Services.AddSingleton<
            IModbusTcpFrameAssembler,
            ModbusTcpFrameAssembler>();

        builder.Services.AddSingleton<
            IModbusFrameStore,
            ModbusFrameStore>();

        builder.Services.AddSingleton<
            IModbusMasterResponseStore,
            ModbusMasterResponseStore>();

        builder.Services.AddSingleton<
            IRegisterManager,
            RegisterManager>();

        builder.Services.AddSingleton<
            ISlaveRegisterViewStore,
            SlaveRegisterViewStore>();

        builder.Services.AddSingleton<
            IModbusRequestProcessor,
            ModbusRequestProcessor>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        MauiApp app = builder.Build();

        // IModbusRequestProcessor, FrameReceived olayina yapicisinda abone olur.
        // Singleton servisler tembel olusturuldugu icin yalnizca DI kaydi yeterli
        // degildir; uygulama acilirken servisi cozumleyerek islemciyi etkinlestiririz.
        _ = app.Services.GetRequiredService<IModbusRequestProcessor>();

        return app;
    }
}
