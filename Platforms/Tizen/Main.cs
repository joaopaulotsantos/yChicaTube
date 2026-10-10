using Microsoft.Maui.Hosting;

namespace yChicaTube;

internal class Program : MauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    static void Main(string[] args) { var app = new Program(); app.Run(args); }
}
