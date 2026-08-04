using Microsoft.Extensions.Logging;
using MauiUygulama.ViewModels;
using MauiUygulama.Core.Interfaces;
using MauiUygulama.Services.Communication;
using MauiUygulama.Services.Modbus;

namespace MauiUygulama
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
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

#endif

            return builder.Build();
        }
    }
}
