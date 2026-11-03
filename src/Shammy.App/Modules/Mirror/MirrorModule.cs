using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Shammy.Modules.Kit;

namespace Shammy.Modules.Mirror;

/// <summary>Remembered between opens: mirrored or not, and the brightness step.</summary>
internal sealed class MirrorStore
{
    public bool Mirrored { get; set; } = true;
    public int Level { get; set; } = 50;
}

/// <summary>
/// A webcam mirror for a quick look before a call. The camera runs only while the mirror's detail is on
/// screen: it starts when the detail shows and is released when it closes, fades, or the PC locks.
/// Chip-only on the home row (never "relevant"), so the camera is always something you opened.
/// </summary>
internal sealed class MirrorModule : IShammyModule
{
    ModuleContext _ctx = null!;
    MirrorStore _store = new();
    MirrorView? _view;
    bool _locked;

    public string Name => "mirror";

    internal MirrorStore Store => _store;
    internal bool Locked => _locked;

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        _store = ctx.Load<MirrorStore>();
        _store.Level = Math.Clamp(_store.Level / 10 * 10, 0, 100);

        ctx.AddCard(new NotchCard
        {
            Id = "mirror",
            Title = "Mirror",
            Width = 176,
            Order = 90,
            Chip = true,
            IsRelevant = () => false,
            Compact = BuildCompact(),
            Detail = () =>
            {
                _view = new MirrorView(this, ctx.Ui);
                return _view;
            },
        });
        ctx.AddPipeCommand("mirror-state", _ => State());
        // Opens the mirror (and so the camera) as a popup card, for a live check before the notch opens cards in place.
        ctx.AddPipeCommand("mirror-open", _ =>
        {
            CardKit.ShowDetailPopup(ctx, () => _view = new MirrorView(this, ctx.Ui));
            return "ok";
        });
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        bool locked = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect;
        bool unlocked = e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect;
        if (!locked && !unlocked) return;
        _ctx.Ui.BeginInvoke(() =>
        {
            _locked = locked;
            _view?.Evaluate();
        });
    }

    internal void Save() => _ctx.Save(_store);

    internal void OpenSettings() => _ctx.Open("ms-settings:privacy-webcam");

    internal void Info(string message) => _ctx.Info(message);

    static FrameworkElement BuildCompact()
    {
        var root = new Grid { Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(124) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var tile = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop((Color)ColorConverter.ConvertFromString("#2B2622"), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#16141A"), 0.55),
                new GradientStop((Color)ColorConverter.ConvertFromString("#0B0B0E"), 1),
            }, new Point(0, 0), new Point(0.6, 1)),
            Child = CardKit.Glyph("", 34, CardKit.Brush("#99F5F5F7")),
        };
        root.Children.Add(tile);

        var row = new DockPanel { Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var hint = CardKit.Label("Tap to open", 11, FontWeights.Medium, CardKit.Text2);
        DockPanel.SetDock(hint, Dock.Right);
        row.Children.Add(hint);
        row.Children.Add(CardKit.Row(7, CardKit.Dot(8, CardKit.Text2), CardKit.Label("Mirror", 13, FontWeights.Bold)));
        Grid.SetRow(row, 1);
        root.Children.Add(row);
        CardKit.AutomationName(root, "Mirror, camera preview. Open mirror");
        return root;
    }

    string State()
    {
        var v = _view;
        bool open = v is { IsVisible: true };
        return string.Create(CultureInfo.InvariantCulture,
            $"detail={(open ? "open" : "closed")} camera={(v?.CameraState ?? "stopped")} frames={v?.Frames ?? 0} size={v?.FrameSize ?? "-"} " +
            $"mirrored={_store.Mirrored} level={_store.Level} locked={_locked} problem={v?.Problem.ToString() ?? "-"}");
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _view?.Shutdown();
    }
}
