using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shammy.Core.Productivity;
using Shammy.Views.Screens;

namespace Shammy.Modules.Productivity.Alarms;

/// <summary>
/// The Alarms screen (the designed detail, 892×232 inside the notch's card frame): a header line with when the
/// next alarm rings and "Add alarm" (the one accent), the list (time, label, repeat text, M–S day toggles, an
/// on/off switch; click a time to edit it), and a side panel with the snooze length, gentle wake and breaking
/// through fullscreen. "Add alarm" and editing swap the list for an inline editor: time, label, days.
/// </summary>
internal sealed class AlarmsDetail : Grid
{
    readonly IAlarmHost _host;
    readonly TextBlock _next = ScreenKit.Text("", 13, Weight.Regular, ScreenKit.Fg2);
    readonly Border _listPanel;
    readonly ContentControl _side = new();
    readonly Pressable _add;
    Editor? _editor;

    public AlarmsDetail(IAlarmHost host)
    {
        _host = host;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        RowDefinitions.Add(new RowDefinition());

        var header = new Grid();
        header.Children.Add(_next);
        _add = ScreenKit.Primary("Add alarm", () => OpenEditor(null), 32, 13, ScreenKit.Paths.Plus, 14);
        _add.HorizontalAlignment = HorizontalAlignment.Right;
        _add.Padding = new Thickness(10, 0, 14, 0);
        header.Children.Add(_add);
        Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        SetRow(body, 2);
        Children.Add(body);

        _listPanel = Panel();
        _listPanel.ClipToBounds = true;
        body.Children.Add(_listPanel);
        var side = Panel();
        side.Padding = new Thickness(14);
        side.Child = _side;
        SetColumn(side, 2);
        body.Children.Add(side);

        // "in 8 h 12 m" only has to be right while it's looked at.
        Views.Cards.CardKit.TickWhileVisible(this, TimeSpan.FromSeconds(30), RefreshNext);
        Refresh();
    }

    static Border Panel() => new()
    {
        CornerRadius = new CornerRadius(16),
        Background = ScreenKit.Tile,
        BorderBrush = ScreenKit.Hairline,
        BorderThickness = new Thickness(1),
    };

    /// <summary>Rebuilds after any change (from here, a command, the pipe or a ringing alarm). An open editor stays.</summary>
    public void Refresh()
    {
        RefreshNext();
        if (_editor is null) _listPanel.Child = List();
        _side.Content = Side();
    }

    void RefreshNext()
    {
        var now = DateTime.Now;
        var next = _host.Alarms.Select(_host.UpcomingLocal).Where(t => t is not null).Min();
        _next.Text = _editor is not null ? (_editor.IsNew ? "New alarm" : "Edit alarm")
            : next is { } n ? AlarmText.NextLine(now, n)
            : _host.Alarms.Count == 0 ? "No alarms yet" : "No alarms on";
        _add.Visibility = _editor is null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- The list ----------------------------------------------------------------------------------

    FrameworkElement List()
    {
        var alarms = _host.Alarms.OrderBy(a => a.Hour * 60 + a.Minute).ThenBy(a => a.Label, StringComparer.OrdinalIgnoreCase).ToList();
        if (alarms.Count == 0)
        {
            var empty = ScreenKit.Column(6,
                ScreenKit.Text("No alarms yet", 16, Weight.Bold),
                ScreenKit.Para("Add one here, or just say “wake me up at 6:30 every weekday”.", 13));
            empty.Margin = new Thickness(20, 0, 20, 0);
            empty.VerticalAlignment = VerticalAlignment.Center;
            return empty;
        }
        var rows = new StackPanel();
        var now = DateTime.Now;
        for (int i = 0; i < alarms.Count; i++) rows.Children.Add(Row(alarms[i], now, last: i == alarms.Count - 1 && alarms.Count > 3));
        return new ScrollViewer
        {
            Content = rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
    }

    FrameworkElement Row(Alarm a, DateTime now, bool last)
    {
        var ink = a.Enabled ? ScreenKit.Fg : ScreenKit.Fg2;
        var grid = new Grid { Height = 58, Margin = new Thickness(16, 0, 16, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });

        // Time and label are one click target: it opens the editor.
        var (time, suffix) = ClockPreference.Parts(a.Hour, a.Minute);
        var big = ScreenKit.Display(time, 26, Weight.ExtraBold, -0.03, ink);
        var am = ScreenKit.Text(suffix, 12, Weight.Bold, ink);
        am.VerticalAlignment = VerticalAlignment.Bottom;
        am.Margin = new Thickness(4, 0, 0, 6);
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        timeRow.Children.Add(big);
        if (suffix.Length > 0) timeRow.Children.Add(am);

        var name = ScreenKit.Text(a.Label.Length > 0 ? a.Label : "Alarm", 14, Weight.SemiBold, ink);
        var repeat = ScreenKit.Text(RepeatLine(a, now), 12, Weight.Regular, a.MissedUtc is not null ? ScreenKit.B(NotchPalette.Alarm) : ScreenKit.Fg2);
        var words = ScreenKit.Column(2, name, repeat);
        words.VerticalAlignment = VerticalAlignment.Center;

        var open = new Pressable(Brushes.Transparent, Brushes.Transparent) { PressScale = 0.99, ToolTip = "Edit" };
        var openGrid = new Grid();
        openGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
        openGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        openGrid.ColumnDefinitions.Add(new ColumnDefinition());
        openGrid.Children.Add(timeRow);
        SetColumn(words, 2);
        openGrid.Children.Add(words);
        open.Child = openGrid;
        open.Click += () => OpenEditor(a);
        SetColumnSpan(open, 3);
        grid.Children.Add(open);

        var days = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (day, letter, dayName) in AlarmText.Week)
        {
            var toggle = new DayToggle(letter, (a.Days & day) != 0, on => _host.SetDays(a, on ? a.Days | day : a.Days & ~day),
                $"Repeat on {dayName}") { Margin = new Thickness(days.Children.Count > 0 ? 3 : 0, 0, 0, 0) };
            days.Children.Add(toggle);
        }
        SetColumn(days, 4);
        grid.Children.Add(days);

        var toggleOn = new ToggleSwitch(a.Enabled, on => _host.SetEnabled(a, on), a.Enabled ? "Turn off" : "Turn on")
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        SetColumn(toggleOn, 6);
        grid.Children.Add(toggleOn);

        return new Border
        {
            BorderBrush = ScreenKit.Hairline,
            BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1),
            Child = grid,
        };
    }

    string RepeatLine(Alarm a, DateTime now)
    {
        var repeat = AlarmText.Repeat(a, now, _host.UpcomingLocal(a));
        if (a.MissedUtc is not null) return "Missed · " + repeat;
        if (a.IsSnoozed && a.SnoozedUntilUtc is { } s) return $"Snoozed till {ClockPreference.Format(s.ToLocalTime())} · {repeat}";
        if (!a.Enabled) return "Off · " + repeat;
        return repeat;
    }

    // ---- Side panel ----------------------------------------------------------------------------------

    FrameworkElement Side()
    {
        int[] lengths = { 5, 10, 15 };
        int at = Array.IndexOf(lengths, _host.DefaultSnoozeMinutes);
        var snooze = new Segmented(lengths.Select(m => $"{m} min").ToList(), at, i => _host.SetSnoozeMinutes(lengths[i]), stretch: true);
        var gentle = SwitchRow("Gentle wake", " · fade in 30 s", _host.GentleWake, on => _host.GentleWake = on,
            "Gentle wake: the sound swells over 30 seconds");
        var breaks = SwitchRow("Break through fullscreen", "", _host.BreaksFullscreen, on => _host.BreaksFullscreen = on,
            "Ring over games, videos and slides");
        var hint = ScreenKit.Para("Same snooze as the “Usual” button when it rings.", 12);
        return ScreenKit.Column(12, ScreenKit.Column(6, ScreenKit.Label("Snooze length"), snooze), breaks, gentle, hint);
    }

    static FrameworkElement SwitchRow(string title, string aside, bool on, Action<bool> set, string tip)
    {
        var grid = new Grid();
        var text = ScreenKit.Text(title, 13, Weight.SemiBold);
        if (aside.Length > 0) text.Inlines.Add(new System.Windows.Documents.Run(aside) { Foreground = ScreenKit.Fg2, FontFamily = ScreenKit.Font(Weight.Medium).Family, FontWeight = FontWeights.Medium });
        grid.Children.Add(text);
        var toggle = new ToggleSwitch(on, set, tip) { HorizontalAlignment = HorizontalAlignment.Right };
        grid.Children.Add(toggle);
        return grid;
    }

    // ---- The editor ----------------------------------------------------------------------------------

    void OpenEditor(Alarm? a)
    {
        _editor = new Editor(a, Close, Save, a is null ? null : () =>
        {
            _host.Delete(a);
            Close();
        });
        _listPanel.Child = _editor;
        ScreenKit.Rise(_editor, 0, 6);
        RefreshNext();
    }

    void Close()
    {
        _editor = null;
        Refresh();
    }

    void Save(Alarm? a, int hour, int minute, AlarmDays days, string label)
    {
        _editor = null;
        if (a is null) _host.Add(hour, minute, days, label);
        else _host.Edit(a, hour, minute, days, label);
        Refresh();
    }

    /// <summary>
    /// The inline editor: the time as two big steppers (wheel or ±; AM/PM when the clock is 12-hour), a label,
    /// the days, and Save (the accent while editing) / Cancel / Delete.
    /// </summary>
    sealed class Editor : Grid
    {
        readonly Alarm? _alarm;
        AlarmDays _days;
        readonly Stepper _hour, _minute;
        readonly Segmented? _half;
        readonly TextBox _label;
        readonly bool _h12 = !ClockPreference.Is24Hour;

        public Editor(Alarm? alarm, Action cancel, Action<Alarm?, int, int, AlarmDays, string> save, Action? delete)
        {
            _alarm = alarm;
            var now = DateTime.Now;
            int hour = alarm?.Hour ?? 7, minute = alarm?.Minute ?? 0;
            if (alarm is null && now.Hour is >= 4 and < 10) (hour, minute) = (now.AddHours(1).Hour, 0);
            _days = alarm?.Days ?? AlarmDays.None;
            Margin = new Thickness(16, 12, 16, 12);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int shown = _h12 ? (hour % 12 == 0 ? 12 : hour % 12) : hour;
            _hour = new Stepper(shown, _h12 ? 1 : 0, _h12 ? 12 : 23, 1, null, v => _h12 ? v.ToString() : v.ToString("00"), 40, 24, 44, wrap: true, "Earlier hour", "Later hour");
            _minute = new Stepper(minute, 0, 59, 5, null, v => v.ToString("00"), 40, 24, 44, wrap: true, "5 minutes earlier", "5 minutes later");
            _minute.WheelStep = 1;
            var colon = ScreenKit.Text(":", 24, Weight.ExtraBold);
            colon.Margin = new Thickness(6, 0, 6, 2);
            var time = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            time.Children.Add(_hour);
            time.Children.Add(colon);
            time.Children.Add(_minute);
            if (_h12)
            {
                _half = new Segmented(new[] { "AM", "PM" }, hour < 12 ? 0 : 1, _ => { }, minWidth: 44, height: 34, fontSize: 13);
                _half.Margin = new Thickness(10, 0, 0, 0);
                time.Children.Add(_half);
            }

            _label = new TextBox
            {
                Style = Application.Current?.TryFindResource("BareTextBox") as Style,
                Text = alarm?.Label ?? "",
                FontSize = 13,
                FontFamily = ScreenKit.Font(Weight.SemiBold).Family,
                FontWeight = FontWeights.SemiBold,
                Foreground = ScreenKit.Fg,
                CaretBrush = ScreenKit.Fg,
                MaxLength = 40,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            var placeholder = ScreenKit.Text("Label, like Gym", 13, Weight.Medium, ScreenKit.Fg2);
            placeholder.IsHitTestVisible = false;
            placeholder.Margin = new Thickness(2, 0, 0, 0);
            void SyncPlaceholder() => placeholder.Visibility = _label.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _label.TextChanged += (_, _) => SyncPlaceholder();
            SyncPlaceholder();
            var labelBox = new Border
            {
                Width = 210,
                Height = 34,
                CornerRadius = new CornerRadius(8),
                Background = ScreenKit.Ground,
                BorderBrush = ScreenKit.Hairline,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(24, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Grid { Children = { placeholder, _label } },
            };
            _label.GotKeyboardFocus += (_, _) => labelBox.BorderBrush = ScreenKit.Fg;
            _label.LostKeyboardFocus += (_, _) => labelBox.BorderBrush = ScreenKit.Hairline;
            _label.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                Commit(save);
            };

            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(time);
            top.Children.Add(labelBox);
            Children.Add(top);

            var repeatText = ScreenKit.Text(AlarmText.Repeat(_days), 12, Weight.Regular, ScreenKit.Fg2);
            repeatText.Margin = new Thickness(12, 0, 0, 0);
            var days = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            days.Children.Add(new ScreenKitLabelHolder(ScreenKit.Label("Repeat")) { Margin = new Thickness(0, 0, 12, 0) });
            foreach (var (day, letter, dayName) in AlarmText.Week)
            {
                days.Children.Add(new DayToggle(letter, (_days & day) != 0, on =>
                {
                    _days = on ? _days | day : _days & ~day;
                    repeatText.Text = AlarmText.Repeat(_days);
                }, $"Repeat on {dayName}", 28) { Margin = new Thickness(days.Children.Count > 1 ? 4 : 0, 0, 0, 0) });
            }
            days.Children.Add(repeatText);
            SetRow(days, 1);
            Children.Add(days);

            var buttons = new Grid();
            if (delete is not null)
            {
                var del = ScreenKit.Secondary("Delete", delete);
                del.HorizontalAlignment = HorizontalAlignment.Left;
                buttons.Children.Add(del);
            }
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(ScreenKit.Secondary("Cancel", cancel, 32, 16, 13));
            var saveButton = ScreenKit.Primary(alarm is null ? "Add" : "Save", () => Commit(save), 32, 13, null, 20);
            saveButton.Margin = new Thickness(8, 0, 0, 0);
            right.Children.Add(saveButton);
            buttons.Children.Add(right);
            SetRow(buttons, 2);
            Children.Add(buttons);
        }

        public bool IsNew => _alarm is null;

        void Commit(Action<Alarm?, int, int, AlarmDays, string> save)
        {
            int hour = _hour.Value;
            if (_h12) hour = hour % 12 + (_half?.Index == 1 ? 12 : 0);
            save(_alarm, hour, _minute.Value, _days, _label.Text.Trim());
        }
    }

    /// <summary>Lets a tracked label sit in a panel with a margin and vertical centring.</summary>
    sealed class ScreenKitLabelHolder : Border
    {
        public ScreenKitLabelHolder(FrameworkElement label)
        {
            VerticalAlignment = VerticalAlignment.Center;
            Child = label;
        }
    }
}
