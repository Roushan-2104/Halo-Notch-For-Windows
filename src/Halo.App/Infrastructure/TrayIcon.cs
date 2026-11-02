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
        Action onPlacementChanged, Action onVoiceLanguageChanged, Action onNotificationSettingsChanged,
        IReadOnlyList<Modules.ModuleTrayItem> moduleItems, UpdateService updates, Action onRestartForUpdate)
    {
        _image = DrawIcon();
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add(new ToolStripLabel("Halo") { Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(new ToolStripLabel("Right Alt: hold to talk · tap to type") { ForeColor = Color.Gray });
        menu.Items.Add(new ToolStripLabel("Copilot key or hold Right Ctrl: circle to search") { ForeColor = Color.Gray });
        menu.Items.Add(new ToolStripSeparator());

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = settings.StartWithWindows, CheckOnClick = true };
        // It can also be turned on from the first-run question; show the real state each time.
        menu.Opening += (_, _) => startup.Checked = settings.StartWithWindows;
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

        var notifications = new ToolStripMenuItem("Show notifications in the notch") { Checked = settings.ShowNotifications, CheckOnClick = true };
        notifications.CheckedChanged += (_, _) =>
        {
            settings.ShowNotifications = notifications.Checked;
            settings.Save();
            onNotificationSettingsChanged();
        };
        // Only Windows can hide its own pop-ups; this opens the page with Do Not Disturb and per-app banners.
        var windowsPopups = new ToolStripMenuItem("Turn off Windows pop-ups and sounds…")
        {
            ToolTipText = "Turn on Do Not Disturb, or turn off \"Allow notifications to play sounds\"; Halo keeps showing notifications with its own ping",
        };
        windowsPopups.Click += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true });
        var unmute = new ToolStripMenuItem("Unmute all apps") { Enabled = settings.MutedNotificationApps.Count > 0 };
        unmute.Click += (_, _) =>
        {
            settings.MutedNotificationApps.Clear();
            settings.Save();
        };
        menu.Opening += (_, _) => unmute.Enabled = settings.MutedNotificationApps.Count > 0;

        menu.Items.Add(startup);
        menu.Items.Add(pin);
        menu.Items.Add(autoLang);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(notifications);
        menu.Items.Add(windowsPopups);
        menu.Items.Add(unmute);
        menu.Items.Add(volume);
        menu.Items.Add(viz);
        menu.Items.Add(compact);
        if (moduleItems.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
            foreach (var item in moduleItems)
            {
                var mi = new ToolStripMenuItem(item.Text);
                if (item.IsChecked is { } isChecked)
                {
                    mi.CheckOnClick = true;
                    mi.Checked = isChecked();
                    mi.CheckedChanged += (_, _) => item.SetChecked?.Invoke(mi.Checked);
                    menu.Opening += (_, _) => mi.Checked = isChecked();
                }
                else
                {
                    mi.Click += (_, _) => item.OnClick?.Invoke();
                }
                menu.Items.Add(mi);
            }
        }

        menu.Items.Add(new ToolStripSeparator());
        var update = new ToolStripMenuItem("Check for updates") { Enabled = updates.IsInstalled };
        if (!updates.IsInstalled) update.ToolTipText = "Install Halo with Setup to get automatic updates";
        update.Click += async (_, _) =>
        {
            if (updates.ReadyVersion is not null)
            {
                onRestartForUpdate();
                return;
            }
            update.Text = "Checking for updates…";
            update.Enabled = false;
            var result = await updates.CheckAsync();
            update.Enabled = true;
            update.Text = result == "up to date" ? "Halo is up to date" : "Check for updates";
        };
        menu.Opening += (_, _) =>
        {
            if (updates.ReadyVersion is { } v) update.Text = $"Restart to update to {v}";
        };
        menu.Items.Add(new ToolStripLabel($"Version {updates.CurrentVersion}") { ForeColor = Color.Gray });
        menu.Items.Add(update);
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
