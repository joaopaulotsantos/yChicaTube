using Android.Util;
using Android.Webkit;
using Microsoft.Maui.Handlers;

namespace yChicaTube;

internal static class YoutubeWebViewConfiguration
{
    private static bool _configured;

    public static void Configure()
    {
        if (_configured) return;
        _configured = true;

        WebViewHandler.Mapper.AppendToMapping("ChicaTubeYouTube", (handler, _) =>
        {
            try
            {
                var webView = handler.PlatformView;
                if (webView is null) return;
                var settings = webView.Settings;
                if (settings is null) return;
                
                settings.JavaScriptEnabled = true;
                settings.DomStorageEnabled = true;
                settings.DatabaseEnabled = true;
                settings.MediaPlaybackRequiresUserGesture = false;
                settings.LoadsImagesAutomatically = true;
                settings.UseWideViewPort = true;
                settings.LoadWithOverviewMode = true;
                settings.MixedContentMode = MixedContentHandling.AlwaysAllow;
                
                // Habilita cookies de terceiros necessários para o stream do YouTube IFrame
                var cookieManager = CookieManager.Instance;
                if (cookieManager is not null)
                {
                    cookieManager.SetAcceptCookie(true);
                    try
                    {
                        cookieManager.SetAcceptThirdPartyCookies(webView, true);
                    }
                    catch { }
                }

                // Limpa o marcador de WebView (; wv) do User-Agent
                var ua = settings.UserAgentString;
                if (!string.IsNullOrEmpty(ua))
                {
                    settings.UserAgentString = System.Text.RegularExpressions.Regex.Replace(ua, @";\s*wv", string.Empty);
                }

                webView.SetLayerType(Android.Views.LayerType.None, null);
                webView.SetBackgroundColor(Android.Graphics.Color.Black);
                webView.OverScrollMode = Android.Views.OverScrollMode.Never;
                webView.VerticalScrollBarEnabled = false;
                webView.HorizontalScrollBarEnabled = false;
                webView.SetWebChromeClient(new YoutubeWebChromeClient());
#if DEBUG
                Android.Webkit.WebView.SetWebContentsDebuggingEnabled(true);
#endif
            }
            catch (System.Exception ex)
            {
                Log.Warn("yChicaTube.YouTube", $"WebView setup warning: {ex.Message}");
            }
        });
    }

    private sealed class YoutubeWebChromeClient : WebChromeClient
    {
        public override bool OnConsoleMessage(ConsoleMessage? consoleMessage)
        {
            if (consoleMessage is not null)
                Log.Debug("yChicaTube.YouTube", $"{consoleMessage.Message()} (line {consoleMessage.LineNumber()})");
            return false;
        }
    }
}
