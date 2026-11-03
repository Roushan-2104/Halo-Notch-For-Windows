using System.Globalization;
using System.Windows.Threading;
using Shammy.Core.Activities;
using Shammy.Core.Calendar;
using Shammy.Core.Connect;
using Shammy.Core.Devices;
using Shammy.Infrastructure;
using Shammy.Modules.Calendar;
using Shammy.SystemInfo;

namespace Shammy.Modules.Connect;

/// <summary>
/// Glue between features that each live in their own module: quiet mode while a calendar meeting is on,
/// "remind me 5 minutes before standup", and low-battery alerts for Bluetooth earbuds.
/// </summary>
internal sealed class ConnectModule : IShammyModule
{
    const string EarbudsId = "connect.earbuds";
    // Above the Bluetooth connect pill (60), below low laptop battery (75).
    const int EarbudsPriority = 62;
    const string BellGlyph = "", HeadsetGlyph = "";
    static readonly TimeSpan MeetingCheckMax = TimeSpan.FromMinutes(5);
    static readonly TimeSpan EarbudsEvery = TimeSpan.FromMinutes(10);

    readonly DispatcherTimer _meeting = new(DispatcherPriority.Background);
    readonly DispatcherTimer _earbudsPoll = new(DispatcherPriority.Background) { Interval = EarbudsEvery };
    readonly EarbudAlerts _earbuds = new();
    ModuleContext _ctx = null!;
    BluetoothService? _bluetooth;
    string _lastEarbuds = "-";

    public string Name => "connect";

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        ctx.AddCommand(new MeetingReminderCommand(this));

        _meeting.Tick += (_, _) => CheckMeeting();
        CheckMeeting();

        _bluetooth = ctx.Services.Bluetooth;
        if (_bluetooth is not null)
        {
            _bluetooth.Changed += OnBluetoothChanged;
            // A cheap SetupAPI pass every 10 minutes, only while a headset is connected.
            _earbudsPoll.Tick += (_, _) => _ = _bluetooth.RefreshBatteryAsync();
            OnBluetoothChanged();
        }

        // "Save as note" on a calculator answer.
        ctx.Notch.AnswerView.SaveNoteRequested += SaveAsNote;

        ctx.AddPipeCommand("connect-state", _ => State());
        ctx.AddPipeCommand("save-note-test", text => SaveAsNote(text) ? "ok" : "error: nothing to save");
        // "earbuds-test Airdopes 311 Pro|18": a reading through the same alert rule (address "TEST").
        ctx.AddPipeCommand("earbuds-test", arg =>
        {
            var f = arg.Split('|');
            if (f.Length != 2 || !int.TryParse(f[1].Trim(), out var level)) return "error: usage earbuds-test <name>|<percent>";
            return Observe("TEST", f[0].Trim(), level) is { } t ? $"alert at {t}%: {_lastEarbuds}" : "no alert";
        });
        // "meeting-remind remind me 5 min before standup": what the command would plan, without adding it.
        ctx.AddPipeCommand("meeting-remind", text =>
        {
            if (MeetingReminder.Parse(text) is not { } r) return "not a meeting reminder";
            return MeetingReminder.Plan(r, CalendarModule.Agenda, DateTime.Now) is { } p
                ? string.Create(CultureInfo.InvariantCulture, $"due={p.Due:ddd HH:mm} meeting=\"{p.Meeting.Title}\" text=\"{p.Text}\"")
                : "no matching meeting";
        });
    }

    public void Dispose()
    {
        _meeting.Stop();
        _earbudsPoll.Stop();
        if (_bluetooth is not null) _bluetooth.Changed -= OnBluetoothChanged;
        _ctx.Notch.AnswerView.SaveNoteRequested -= SaveAsNote;
    }

    /// <summary>Keeps a text (an answer, a copied snippet) as a note; true when it was saved.</summary>
    internal bool SaveAsNote(string text)
    {
        if (ConnectText.NoteFrom(text) is not { } note || Links.Notes is not { } notes) return false;
        var result = notes.SaveNote(note);
        _ctx.ShowResult(result);
        return !result.IsError;
    }

    // ---- Quiet during calendar meetings -----------------------------------------------------------

    /// <summary>Quiet while a linked meeting is on; wakes when that next changes (at least every 5 min, for new agendas).</summary>
    void CheckMeeting()
    {
        _meeting.Stop();
        var now = DateTime.Now;
        var items = CalendarModule.Agenda;
        QuietState.Set(QuietReason.Meeting, QuietRules.MeetingNow(items, now) is not null);
        var wait = QuietRules.NextMeetingChange(items, now) is { } at && at - now < MeetingCheckMax ? at - now : MeetingCheckMax;
        _meeting.Interval = wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
        _meeting.Start();
    }

    // ---- Earbuds low battery ----------------------------------------------------------------------

    void OnBluetoothChanged()
    {
        if (_bluetooth is null) return;
        var devices = _bluetooth.AudioDevices;
        if (devices.Count > 0 && !_earbudsPoll.IsEnabled) _earbudsPoll.Start();
        else if (devices.Count == 0) _earbudsPoll.Stop();
        foreach (var d in devices)
            if (d.Battery is { } level) Observe(d.Address.Length > 0 ? d.Address : d.Id, d.Name, level);
    }

    int? Observe(string key, string name, int level)
    {
        if (_earbuds.Observe(key, level) is not { } threshold) return null;
        _lastEarbuds = EarbudAlerts.Pill(name, level, threshold);
        _ctx.Info($"Earbuds low: {level}% (threshold {threshold})");
        _ctx.Post(new Activity(EarbudsId, ActivityKind.Message, EarbudsPriority,
            new MessagePayload(HeadsetGlyph, _lastEarbuds, threshold <= 10), DateTime.UtcNow.AddSeconds(6)));
        return threshold;
    }

    // ---- "remind me 5 minutes before standup" -----------------------------------------------------

    CommandResult RunReminder(string text)
    {
        if (MeetingReminder.Parse(text) is not { } request) return new CommandResult(BellGlyph, "Not a meeting reminder", IsError: true);
        var items = CalendarModule.Agenda;
        if (MeetingReminder.Plan(request, items, DateTime.Now) is not { } plan)
        {
            if (items.Count == 0) return new CommandResult(BellGlyph, "No calendar yet", "Connect Google Calendar from the tray", IsError: true);
            return new CommandResult(BellGlyph, request.IsNextMeeting ? "No meetings coming up" : $"No “{request.Target}” coming up",
                "Nothing in your calendar this week", IsError: true);
        }
        if (Links.Notes is not { } notes) return new CommandResult(BellGlyph, "Reminders aren't available", IsError: true);
        return notes.AddReminder(plan.Due, plan.Text);
    }

    CommandPreview? PreviewReminder(string text)
    {
        if (MeetingReminder.Parse(text) is not { } request) return null;
        var now = DateTime.Now;
        if (MeetingReminder.Plan(request, CalendarModule.Agenda, now) is not { } plan) return new CommandPreview(BellGlyph, "Remind me before a meeting", "No matching meeting");
        var before = request.Before.TotalMinutes >= 60 && request.Before.TotalMinutes % 60 == 0
            ? $"{request.Before.TotalHours:0} h" : $"{request.Before.TotalMinutes:0} min";
        var title = PrivacyState.Presenting ? "your meeting" : plan.Meeting.Title;
        return new CommandPreview(BellGlyph, $"Remind me {before} before {title}", $"At {CalendarText.ClockAmPm(plan.Due)}");
    }

    bool CanHandleReminder(string text) =>
        MeetingReminder.Parse(text) is { } r && (r.IsNextMeeting || CalendarModule.Agenda.Count > 0);

    string State()
    {
        var now = DateTime.Now;
        var meeting = QuietRules.MeetingNow(CalendarModule.Agenda, now);
        var devices = _bluetooth?.AudioDevices.Select(d => $"{d.Name}={(d.Battery is { } b ? b + "%" : "-")}") ?? Array.Empty<string>();
        return $"quiet={QuietState.IsQuiet} ({QuietState.Describe()}) meeting={(meeting is null ? "-" : PrivacyState.Presenting ? "yes" : $"\"{meeting.Title}\"")} " +
               $"meetingCheck={(_meeting.IsEnabled ? $"{_meeting.Interval.TotalSeconds:0}s" : "off")} " +
               $"headsets=[{string.Join(", ", devices)}] poll={(_earbudsPoll.IsEnabled ? "10m" : "off")} lastAlert={_lastEarbuds}";
    }

    sealed class MeetingReminderCommand(ConnectModule module) : ITextCommand, ICommandPreview
    {
        public bool CanHandle(string text) => module.CanHandleReminder(text);
        public Task<CommandResult> RunAsync(string text) => Task.FromResult(module.RunReminder(text));
        public CommandPreview? Preview(string text) => module.PreviewReminder(text);
    }
}
