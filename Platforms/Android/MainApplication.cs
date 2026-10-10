using Android.App;
using Android.Runtime;
using Android.Util;

namespace yChicaTube;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();

        AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
        {
            Log.Error("yChicaTube.Crash", $"Unhandled Exception: {e.Exception}");
            System.Diagnostics.Debug.WriteLine($"[CRASH] {e.Exception}");
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log.Error("yChicaTube.Crash", $"AppDomain Unhandled: {e.ExceptionObject}");
            System.Diagnostics.Debug.WriteLine($"[CRASH AppDomain] {e.ExceptionObject}");
        };
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
