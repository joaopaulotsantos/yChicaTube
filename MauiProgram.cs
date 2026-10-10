using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;

namespace yChicaTube;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
#if ANDROID
        YoutubeWebViewConfiguration.Configure();
        Microsoft.Maui.Handlers.ViewHandler.ViewMapper.AppendToMapping("AndroidTvFocus", (handler, view) =>
        {
            if (handler.PlatformView is Android.Views.View nativeView)
            {
                if (view is Entry || view is Editor || view is SearchBar)
                {
                    nativeView.Focusable = true;
                    nativeView.FocusableInTouchMode = true;
                }
                else if (view is Button || view is Picker || (view is View v && (v.GestureRecognizers.Count > 0 || v is Border)))
                {
                    nativeView.Focusable = true;
                    nativeView.FocusableInTouchMode = false; // Permite o clique imediato com 1 só toque no telemóvel!
                    nativeView.Clickable = true;
                }
            }
        });
#endif
        builder.UseMauiApp<App>().ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold"); });
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
