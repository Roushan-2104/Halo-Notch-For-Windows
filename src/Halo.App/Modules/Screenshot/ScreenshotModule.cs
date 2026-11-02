using System.Text.RegularExpressions;
using System.Windows.Threading;
using Halo.Search;

namespace Halo.Modules.Screenshot;

/// <summary>"take a screenshot" by voice or typing: captures the whole screen the pointer is on.</summary>
internal sealed class ScreenshotModule : IHaloModule, ITextCommand
{
    static readonly Regex Phrase = new(
        @"^(?:please\s+)?(?:take|grab|capture|save)\s+(?:a\s+|the\s+)?(?:screen\s*shot|screen|snapshot)(?:\s+of\s+(?:the\s+)?(?:screen|whole\s+screen|everything|this|page))?(?:\s+please)?$|^screen\s*shot(?:\s+(?:this|now|please))?$|^print\s*screen$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    ModuleContext _ctx = null!;

    public string Name => "screenshot";

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        ctx.AddCommand(this);
        ctx.AddPipeCommand("screenshot-now", _ =>
        {
            Schedule();
            return "ok";
        });
    }

    public bool CanHandle(string text) => Phrase.IsMatch(text.Trim().TrimEnd('.', '!', '?').Trim());

    public Task<CommandResult> RunAsync(string text)
    {
        Schedule();
        return Task.FromResult(new CommandResult("", "Screenshot", "Saved to Pictures › Screenshots and copied"));
    }

    /// <summary>
    /// Wait for the frozen-screen overlay (voice commands) to fade out first, and keep the notch's own
    /// confirmation card out of the picture.
    /// </summary>
    void Schedule()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _ctx.Ui) { Interval = TimeSpan.FromMilliseconds(650) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                _ctx.Notch.ExcludeFromCapture(true);
                var shot = ScreenCapture.CaptureMonitorAtCursor();
                ScreenshotSaver.Save(shot.Image);
            }
            catch (Exception ex)
            {
                _ctx.Warn("Screenshot failed", ex);
                _ctx.ShowMessage("", "Screenshot failed", error: true);
            }
            finally
            {
                _ctx.Notch.ExcludeFromCapture(false);
            }
        };
        timer.Start();
    }

    public void Dispose() { }
}
