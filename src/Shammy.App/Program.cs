using Shammy.Infrastructure;
using Velopack;

namespace Shammy;

/// <summary>
/// Entry point. Velopack must run first: the installer, updater and uninstaller start Shammy.exe with
/// special arguments, handle them here and exit before any window exists.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            // Don't leave a startup entry pointing at a deleted exe.
            .OnBeforeUninstallFastCallback(_ => AppSettings.RemoveStartupEntry())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
