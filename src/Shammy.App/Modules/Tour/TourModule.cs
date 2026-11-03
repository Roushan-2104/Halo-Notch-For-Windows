using System.Globalization;
using System.Windows.Threading;
using Shammy.Core.Notch;
using Shammy.Infrastructure;
using Shammy.Modules.Settings;
using Shammy.Shell;
using Shammy.Views.Screens;

namespace Shammy.Modules.Tour;

/// <summary>
/// The first-run welcome tour: five steps led by the pet in the notch (hello, hold Right Alt to talk, tap it to
/// type, the Copilot key to circle, hover for the rest and pick a name). Shown once, after the start-up question
/// has had its turn; replayable from the tray ("Show the tour") and by typing or saying "tour" or "help".
/// Pipes: "tour-start", "tour-step &lt;1-5|done|skip|close&gt;", "tour-state".
/// </summary>
internal sealed class TourModule : IShammyModule
{
    // The start-up question shows ~6 s in; look a little after, and wait while any card is up.
    static readonly TimeSpan FirstLook = TimeSpan.FromSeconds(9), Retry = TimeSpan.FromSeconds(3), GiveUp = TimeSpan.FromSeconds(90);
    // Left alone this long, the tour folds away (and counts as seen: it never nags).
    static readonly TimeSpan Idle = TimeSpan.FromSeconds(90);

    ModuleContext _ctx = null!;
    readonly DispatcherTimer _wait = new(DispatcherPriority.Background), _idle = new(DispatcherPriority.Background), _goodbye = new(DispatcherPriority.Background);
    TourCard? _card;
    DateTime _waitStarted;
    int _step = 1;
    TourMode _mode = TourMode.Tour;

    public string Name => "tour";

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        _wait.Tick += (_, _) => TryFirstRun();
        _idle.Interval = Idle;
        _idle.Tick += (_, _) => Close();
        _goodbye.Tick += (_, _) => Close(openNotch: _mode == TourMode.Finished);

        ctx.AddTrayItem("Show the tour", () => Show(1));
        ctx.AddCommand(new TourCommand(this));
        ctx.AddPipeCommand("tour-start", _ =>
        {
            Show(1);
            return State();
        });
        ctx.AddPipeCommand("tour-step", arg =>
        {
            var a = arg.Trim().ToLowerInvariant();
            switch (a)
            {
                case "done": Finish(); break;
                case "skip": Skip(); break;
                case "close": Close(); break;
                default:
                    if (!int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n is < 1 or > TourScript.Steps)
                        return "error: usage tour-step <1-5|done|skip|close>";
                    Show(n);
                    break;
            }
            return State();
        });
        ctx.AddPipeCommand("tour-state", _ => State());

        if (!ctx.Settings.TourSeen)
        {
            _waitStarted = DateTime.UtcNow;
            _wait.Interval = FirstLook;
            _wait.Start();
        }
    }

    public void Dispose()
    {
        _wait.Stop();
        _idle.Stop();
        _goodbye.Stop();
    }

    string State() =>
        $"seen={_ctx.Settings.TourSeen} shown={(_card?.IsVisible == true ? "yes" : _card is null ? "no" : "hidden")} " +
        $"mode={_mode} step={_step} name={PetLook.Name} notch={_ctx.Notch.Mode}";

    /// <summary>First run: once nothing else holds the notch (the start-up question, a fullscreen app, a search).</summary>
    void TryFirstRun()
    {
        _wait.Stop();
        if (_ctx.Settings.TourSeen || _card is not null) return;
        bool free = _ctx.Notch.Mode is NotchMode.Idle or NotchMode.Compact or NotchMode.Peek;
        if (!free)
        {
            if (DateTime.UtcNow - _waitStarted < GiveUp)
            {
                _wait.Interval = Retry;
                _wait.Start();
            }
            // Busy the whole time (a game, say): it shows next launch.
            return;
        }
        Show(1);
    }

    public void Show(int step)
    {
        _wait.Stop();
        _goodbye.Stop();
        _step = Math.Clamp(step, 1, TourScript.Steps);
        _mode = TourMode.Tour;
        Render();
    }

    void Render()
    {
        var card = new TourCard(_step, _mode, PetLook.Name, new TourCard.Actions(
            Next: () =>
            {
                if (_step < TourScript.Steps) Show(_step + 1);
                else Finish();
            },
            Back: () => Show(Math.Max(1, _step - 1)),
            Skip: Skip,
            Restart: () => Show(1),
            OpenPalette: () =>
            {
                // The palette takes the notch; Esc (or running something) brings the tour back where it was.
                _idle.Stop();
                HostLinks.OpenPalette?.Invoke();
                _idle.Start();
            },
            PickName: PickName,
            Close: () => Close(openNotch: _mode == TourMode.Finished)));
        _card = card;
        // Not interactive: it never takes focus from what you're doing; its buttons work by mouse.
        _ctx.ShowCard(card, TourCard.CardWidth, TourCard.CardHeight, interactive: false);
        _idle.Stop();
        _idle.Start();
    }

    void PickName(string name)
    {
        if (SettingsCatalog.Find(nameof(AppSettings.PetName)) is { } info) SettingsCatalog.Set(_ctx.Settings, info, name);
        // Re-draw the step so the card says the new name (the pet does a happy wiggle anyway).
        Render();
    }

    void Finish() => Goodbye(TourMode.Finished, TimeSpan.FromSeconds(5));

    void Skip() => Goodbye(TourMode.Skipped, TimeSpan.FromSeconds(4));

    void Goodbye(TourMode mode, TimeSpan stay)
    {
        _mode = mode;
        MarkSeen();
        Render();
        _goodbye.Stop();
        _goodbye.Interval = stay;
        _goodbye.Start();
    }

    void Close(bool openNotch = false)
    {
        _idle.Stop();
        _goodbye.Stop();
        MarkSeen();
        // Only if it's still ours on screen (an alarm may have replaced it).
        if (_card?.Parent is not null) _ctx.CloseCard();
        _card = null;
        _mode = TourMode.Tour;
        // "Hover me for the rest": the finished tour opens the notch once, so the rail is right there.
        if (openNotch) _ctx.OpenCard("home");
    }

    void MarkSeen()
    {
        if (_ctx.Settings.TourSeen) return;
        _ctx.Settings.TourSeen = true;
        _ctx.Settings.Save();
    }

    sealed class TourCommand(TourModule module) : ITextCommand, ICommandPreview
    {
        public bool CanHandle(string text) => TourScript.IsTourCommand(text);

        public Task<CommandResult> RunAsync(string text)
        {
            // After the confirmation has had its moment, the tour takes the notch.
            module._ctx.Ui.BeginInvoke(() => module.Show(1), DispatcherPriority.Background);
            return Task.FromResult(new CommandResult("", "The tour", $"{PetLook.Name} shows you around"));
        }

        public CommandPreview? Preview(string text) => new("", "Show the tour", "Five quick steps with the pet");
    }
}
