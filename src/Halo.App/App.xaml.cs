using System.Windows;
using System.Windows.Media.Animation;
using Halo.Infrastructure;

namespace Halo;

public partial class App : Application
{
    Mutex? _instance;
    HaloHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new Mutex(true, @"Local\Halo.SingleInstance", out bool first);
        if (!first)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Fatal exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        // WPF caps storyboard animations at 60 fps by default; let high refresh displays run at full rate.
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = 240 });

        Log.Info($"Halo {typeof(App).Assembly.GetName().Version} starting on {Environment.OSVersion}");
        Theme.Accent.Apply();
        try
        {
            _host = new HaloHost(AppSettings.Load());
            _host.Start();
        }
        catch (Exception ex)
        {
            // Without the notch there's nothing to show; don't linger as an invisible process.
            Log.Error("Startup failed", ex);
            MessageBox.Show($"Halo couldn't start:\n\n{ex.Message}\n\nDetails are in {Paths.Logs}", "Halo",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _instance?.Dispose();
        Log.Info("Halo exited");
        base.OnExit(e);
    }
}
