using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shammy.Core.Settings;
using Shammy.Infrastructure;
using Shammy.Modules.Kit;
using Shammy.Search;

namespace Shammy.Modules.Settings;

/// <summary>
/// A temporary, plain Settings card so every setting is usable before the designed screen exists: sections on
/// the left, one row per setting on the right (switch pills, cycling choices, −/+ steppers, text boxes, the
/// microphone list, muted apps and module switches). Built from <see cref="SettingsCatalog"/>; replace freely.
/// </summary>
internal sealed class SettingsCard
{
    const string Accent = NotchPalette.Purple;
    readonly AppSettings _settings;
    readonly Func<IReadOnlyList<string>> _modules;
    readonly Grid _compact = new() { Margin = new Thickness(14) };
    ContentControl? _detail;
    string _section = SettingsCatalog.Sections[0];

    public SettingsCard(AppSettings settings, Func<IReadOnlyList<string>> modules)
    {
        _settings = settings;
        _modules = modules;
        _compact.IsVisibleChanged += (_, _) =>
        {
            if (_compact.IsVisible) RefreshCompact();
        };
        RefreshCompact();
    }

    public FrameworkElement Compact => _compact;

    void RefreshCompact()
    {
        _compact.Children.Clear();
        var stack = new StackPanel();
        stack.Children.Add(CardKit.Glyph("", 22, CardKit.Brush(Accent)));
        var title = CardKit.Label("Settings", 16);
        title.Margin = new Thickness(0, 10, 0, 2);
        stack.Children.Add(title);
        stack.Children.Add(CardKit.Label($"Hover {_settings.HoverSpeed.ToString().ToLowerInvariant()} · sound {_settings.NotchSoundVolume}%", 11, FontWeights.Medium, CardKit.Text2));
        stack.Children.Add(CardKit.Label(_settings.Use24Hour is { } h ? (h ? "24-hour clock" : "12-hour clock") : "Clock follows Windows", 11, FontWeights.Medium, CardKit.Text2));
        _compact.Children.Add(stack);
    }

    public FrameworkElement BuildDetail()
    {
        _detail = new ContentControl { Content = DetailContent() };
        return _detail;
    }

    /// <summary>Rebuilds the open detail (after a change) and the compact card.</summary>
    public void Refresh()
    {
        if (_detail is { IsVisible: true } d) d.Content = DetailContent();
        RefreshCompact();
    }

    FrameworkElement DetailContent()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        var sections = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        foreach (var name in SettingsCatalog.Sections)
        {
            bool on = name == _section;
            var pill = CardKit.PillButton(name, () =>
            {
                _section = name;
                Refresh();
            }, 26, background: on ? CardKit.Tint(Accent, 0.25) : CardKit.Raised, foreground: on ? CardKit.Text : CardKit.Text2);
            pill.HorizontalAlignment = HorizontalAlignment.Stretch;
            pill.Margin = new Thickness(0, 0, 0, 4);
            sections.Children.Add(pill);
        }
        root.Children.Add(new ScrollViewer { Content = sections, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var rows = new WrapPanel { Orientation = Orientation.Horizontal };
        if (_section == "Modules") AddModuleRows(rows);
        else
        {
            foreach (var info in SettingsCatalog.In(_section))
            {
                if (info.Name == nameof(AppSettings.MutedNotificationApps)) AddMutedRows(rows);
                else rows.Children.Add(Row(info.Label, Control(info)));
            }
        }
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1);
        root.Children.Add(scroll);
        return root;
    }

    static Border Row(string label, FrameworkElement control)
    {
        var grid = new Grid { Width = 340, Height = 32 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(CardKit.Label(label, 12, FontWeights.Medium));
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return new Border
        {
            Background = CardKit.Raised,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 2, 6, 2),
            Margin = new Thickness(0, 0, 8, 6),
            Child = grid,
        };
    }

    FrameworkElement Control(SettingInfo info)
    {
        var value = SettingsCatalog.Get(_settings, info);
        var type = Nullable.GetUnderlyingType(info.Type) ?? info.Type;
        if (info.Name == nameof(AppSettings.MicrophoneDeviceId)) return Microphone();
        if (type == typeof(bool))
        {
            // bool? cycles Auto → On → Off.
            bool nullable = Nullable.GetUnderlyingType(info.Type) is not null;
            string text = value is bool b ? (b ? "On" : "Off") : "Auto";
            object? next = value switch
            {
                null => true,
                true => false,
                _ => nullable ? null : true,
            };
            return Pill(text, () => Set(info, next), value is true);
        }
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToList();
            int i = values.IndexOf(value!);
            return Pill(value?.ToString() ?? "-", () => Set(info, values[(i + 1) % values.Count]), false);
        }
        if (type == typeof(int))
        {
            int n = (int)value!;
            var label = CardKit.Label(n.ToString(System.Globalization.CultureInfo.InvariantCulture), 12);
            label.MinWidth = 34;
            label.TextAlignment = TextAlignment.Center;
            return CardKit.Row(4,
                CardKit.RoundButton("", () => Set(info, n - info.Step), 24, "Less", 9),
                label,
                CardKit.RoundButton("", () => Set(info, n + info.Step), 24, "More", 9));
        }
        if (type == typeof(string)) return TextField(info, value as string ?? "");
        return CardKit.Label(SettingValue.Format(value), 12, brush: CardKit.Text2);
    }

    static Border Pill(string text, Action click, bool on) =>
        CardKit.PillButton(text, click, 24, background: on ? CardKit.Tint(Accent, 0.3) : null);

    FrameworkElement TextField(SettingInfo info, string value)
    {
        var box = new TextBox
        {
            Style = (Style)Application.Current.Resources["BareTextBox"],
            FontSize = 12,
            FontFamily = CardKit.Font,
            Text = value,
            Width = 80,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        void Commit()
        {
            if (box.Text.Trim() != value) Set(info, box.Text.Trim(), text: true);
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Commit();
        };
        box.LostKeyboardFocus += (_, _) => Commit();
        return new Border { Background = CardKit.Round, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 8, 3), Child = box };
    }

    FrameworkElement Microphone()
    {
        // Cycles Windows default → each microphone → back.
        var names = new List<string?> { null };
        names.AddRange(MicRecorder.DeviceNames());
        int i = names.FindIndex(n => n is not null && string.Equals(n, _settings.MicrophoneDeviceId, StringComparison.OrdinalIgnoreCase));
        if (i < 0) i = 0;
        var current = names[i] ?? "Windows default";
        var pill = Pill(current.Length > 24 ? current[..24] + "…" : current, () =>
        {
            var next = names[(i + 1) % names.Count];
            Set(SettingsCatalog.Find(nameof(AppSettings.MicrophoneDeviceId))!, next);
        }, false);
        pill.ToolTip = current;
        return pill;
    }

    void AddMutedRows(Panel rows)
    {
        if (_settings.MutedNotificationApps.Count == 0)
        {
            rows.Children.Add(Row("Muted apps", CardKit.Label("None", 12, brush: CardKit.Text2)));
            return;
        }
        foreach (var app in _settings.MutedNotificationApps.ToList())
        {
            var name = app.Split('!')[0];
            if (name.Length > 28) name = name[..28] + "…";
            rows.Children.Add(Row($"Muted: {name}", Pill("Unmute", () =>
            {
                _settings.UnmuteApp(app);
                _settings.NotifyChanged(nameof(AppSettings.MutedNotificationApps));
                Refresh();
            }, false)));
        }
    }

    void AddModuleRows(Panel rows)
    {
        foreach (var name in _modules().Where(m => m != SettingsModule.ModuleName).Order())
        {
            bool off = _settings.DisabledModules.Contains(name, StringComparer.OrdinalIgnoreCase);
            rows.Children.Add(Row(name, Pill(off ? "Off" : "On", () =>
            {
                var list = _settings.DisabledModules.ToList();
                if (off) list.RemoveAll(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase));
                else list.Add(name);
                Set(SettingsCatalog.Find(nameof(AppSettings.DisabledModules))!, list);
            }, !off)));
        }
        rows.Children.Add(Row("Changes apply when Shammy restarts", new Border()));
    }

    void Set(SettingInfo info, object? value, bool text = false)
    {
        if (text)
        {
            if (SettingsCatalog.TrySet(_settings, info, (string)value!) is { } error) Log.Warn($"Settings: {error}");
        }
        else SettingsCatalog.Set(_settings, info, value);
        // Rebuild after the click has finished (the clicked pill is part of what gets replaced).
        Application.Current.Dispatcher.BeginInvoke(Refresh);
    }
}
