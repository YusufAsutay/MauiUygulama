using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MauiUygulama.Core.Interfaces;
using MauiUygulama.Services.Communication;
using MauiUygulama.Services.Modbus;
using MauiUygulama.ViewModels;

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

        builder.Services.AddSingleton<ModeSelectionViewModel>();

        builder.Services.AddSingleton<
            ITcpClientService,
            TcpClientService>();

        builder.Services.AddSingleton<
            ITcpServerService,
            TcpServerService>();

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
            IModbusMasterService,
            ModbusMasterService>();

        builder.Services.AddSingleton<
            IModbusPollingService,
            ModbusPollingService>();

        builder.Services.AddSingleton<
            IRegisterManager,
            RegisterManager>();

        // Slave başlangıç register haritasını oluşturur.
        builder.Services.AddSingleton<
            SlaveRegisterMapInitializer>();

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

        /*
         * Register haritası Modbus istek işlemcisi
         * etkinleşmeden önce oluşturulmalıdır.
         */
        SlaveRegisterMapInitializer mapInitializer =
            app.Services.GetRequiredService<
                SlaveRegisterMapInitializer>();

        mapInitializer.Initialize();

        /*
         * Singleton servisler tembel oluşturulduğu için
         * istek işlemcisini uygulama başlangıcında çözümleriz.
         * Böylece FrameReceived olayına abone olur.
         */
        _ = app.Services.GetRequiredService<
            IModbusRequestProcessor>();

        return app;
    }
}