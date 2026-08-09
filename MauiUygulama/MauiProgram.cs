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
            IModbusMasterService,
            ModbusMasterService>();

        builder.Services.AddSingleton<
            IModbusPollingService,
            ModbusPollingService>();

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

        // Slave işlemcisi FrameReceived olayına yapıcısında abone olur.
        // Singleton servisler tembel oluşturulduğu için uygulama açılırken
        // çözümleyerek FC01/FC03/FC06 istek işlemcisini etkinleştiririz.
        _ = app.Services.GetRequiredService<IModbusRequestProcessor>();

        return app;
    }
}
