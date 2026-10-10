namespace yChicaTube.Services;

public static class DeviceModeService
{
    private static bool? _cachedIsTv;

    public static bool IsTv
    {
        get
        {
            if (_cachedIsTv.HasValue) return _cachedIsTv.Value;

            if (DeviceInfo.Idiom == DeviceIdiom.TV)
            {
                _cachedIsTv = true;
                return true;
            }

#if ANDROID
            try
            {
                var ctx = Android.App.Application.Context;
                var pm = ctx.PackageManager;
                if (pm != null)
                {
                    if (pm.HasSystemFeature(Android.Content.PM.PackageManager.FeatureLeanback) ||
                        pm.HasSystemFeature(Android.Content.PM.PackageManager.FeatureTelevision) ||
                        pm.HasSystemFeature("android.hardware.type.television"))
                    {
                        _cachedIsTv = true;
                        return true;
                    }
                }

                var uiMode = (Android.App.UiModeManager?)ctx.GetSystemService(Android.Content.Context.UiModeService);
                if (uiMode != null && uiMode.CurrentModeType == Android.Content.Res.UiMode.TypeTelevision)
                {
                    _cachedIsTv = true;
                    return true;
                }

                var model = Android.OS.Build.Model?.ToLowerInvariant() ?? string.Empty;
                var brand = Android.OS.Build.Brand?.ToLowerInvariant() ?? string.Empty;
                if (model.Contains("tv") || brand.Contains("tv") || model.Contains("box") || model.Contains("shield") || model.Contains("bravia"))
                {
                    _cachedIsTv = true;
                    return true;
                }
            }
            catch { }
#endif
            _cachedIsTv = false;
            return false;
        }
    }

    public static bool IsMobile => !IsTv;

    public static int GetOptimalGridSpan(double width)
    {
        if (IsTv)
        {
            // Na TV em ecrã panorâmico 16:9 (1080p ou 4K), 4 colunas dá a proporção ideal
            return width >= 1200 ? 5 : 4;
        }

        if (DeviceInfo.Idiom == DeviceIdiom.Tablet || width >= 650)
        {
            return 2;
        }

        return 1;
    }

    public static double GetOptimalThumbnailHeight(double width)
    {
        if (IsTv)
        {
            // Altura compacta para cartões em grelha de 4 colunas na TV
            return 135;
        }

        if (DeviceInfo.Idiom == DeviceIdiom.Tablet || width >= 650)
        {
            return 170;
        }

        // Smartphone vertical
        return 200;
    }
}

