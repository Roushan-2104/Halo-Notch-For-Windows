using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Halo.Interop;

namespace Halo.Infrastructure;

/// <summary>The tray icon and its menu: the only chrome Halo has outside the notch.</summary>
internal sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;
    readonly Icon _image;

    public TrayIcon(AppSettings settings, Action onQuit, Action<bool> onReplaceVolumeChanged, Action<bool> onVisualizerChanged,
        Action onPlacementChanged, Action onVoiceLanguageChanged)
    {
        _image = DrawIcon();
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add(new ToolStripLabel("Halo") { Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(new ToolStripLabel("Hold Right Alt to search · tap to type") { ForeColor = Color.Gray });
        menu.Items.Add(new ToolStripSeparator());

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = settings.StartWithWindows, CheckOnClick = true };
        startup.CheckedChanged += (_, _) =>
        {
            try { settings.StartWithWindows = startup.Checked; }
            catch (Exception ex) { Log.Warn("Updating startup entry failed", ex); }
        };
        var volume = new ToolStripMenuItem("Replace Windows volume popup") { Checked = settings.ReplaceVolumeFlyout, CheckOnClick = true };
        volume.CheckedChanged += (_, _) =>
        {
            settings.ReplaceVolumeFlyout = volume.Checked;
            settings.Save();
            onReplaceVolumeChanged(volume.Checked);
        };
        var viz = new ToolStripMenuItem("Audio visualizer") { Checked = settings.ShowVisualizer, CheckOnClick = true };
        viz.CheckedChanged += (_, _) =>
        {
            settings.ShowVisualizer = viz.Checked;
            settings.Save();
            onVisualizerChanged(viz.Checked);
        };
        var compact = new ToolStripMenuItem("Compact (mobile) results layout") { Checked = settings.CompactResults, CheckOnClick = true, ToolTipText = "Applies after restarting Halo" };
        compact.CheckedChanged += (_, _) =>
        {
            settings.CompactResults = compact.Checked;
            settings.Save();
        };

        var pin = new ToolStripMenuItem("Pin to the very top edge (over a top taskbar)") { Checked = settings.PinToScreenEdge, CheckOnClick = true };
        pin.CheckedChanged += (_, _) =>
        {
            settings.PinToScreenEdge = pin.Checked;
            settings.Save();
            onPlacementChanged();
        };

        var autoLang = new ToolStripMenuItem("Detect voice language (Hindi etc., slower)") { Checked = settings.VoiceLanguage == "auto", CheckOnClick = true };
        autoLang.CheckedChanged += (_, _) =>
        {
            settings.VoiceLanguage = autoLang.Checked ? "auto" : "en";
            settings.Save();
            onVoiceLanguageChanged();
        };

        menu.Items.Add(startup);
        menu.Items.Add(pin);
        menu.Items.Add(autoLang);
        menu.Items.Add(volume);
        menu.Items.Add(viz);
        menu.Items.Add(compact);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open logs folder", null, (_, _) => Process.Start(new ProcessStartInfo(Paths.Logs) { UseShellExecute = true }));
        menu.Items.Add("Quit Halo", null, (_, _) => onQuit());

        _icon = new NotifyIcon { Icon = _image, Text = "Halo", ContextMenuStrip = menu, Visible = true };
    }

    /// <summary>A small notch glyph drawn at runtime, so there's no binary asset to keep in sync.</summary>
    static Icon DrawIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var path = new GraphicsPath();
            var r = new RectangleF(3, 9, 26, 14);
            float d = 14;
            path.AddArc(r.X, r.Y, d, d, 90, 180);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            path.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(255, 18, 18, 20));
            using var pen = new Pen(Color.White, 2f);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
            using var dot = new SolidBrush(Color.FromArgb(255, 76, 194, 255));
            g.FillEllipse(dot, 19, 13, 6, 6);
        }
        var h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        Native.DestroyIcon(h);
        return icon;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
