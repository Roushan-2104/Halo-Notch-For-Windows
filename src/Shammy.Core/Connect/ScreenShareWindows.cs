using System.Text.RegularExpressions;

namespace Shammy.Core.Connect;

/// <summary>
/// Spots a whole-screen share from the window its app shows while sharing. Chrome and Edge capture without touching
/// the Windows capability keys, so their "meet.google.com is sharing your screen." bar is the only reliable sign.
/// Only the whole screen counts: a shared tab or window can't show the notch to anyone.
/// </summary>
public static class ScreenShareWindows
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly Regex Browser = new(@"^(?<site>\S+) is sharing your screen\.?$", Opts);
    static readonly Regex Zoom = new(@"^(?:you are screen sharing|zoom share (?:statusbar|toolbar) window)", Opts);
    static readonly Regex Teams = new(@"^sharing control bar\b", Opts);

    /// <summary>Who is sharing, judging by this window's title; null when it isn't a sharing window.</summary>
    public static string? Sharer(string? title)
    {
        var t = (title ?? "").Trim();
        if (t.Length == 0) return null;
        var m = Browser.Match(t);
        if (m.Success) return m.Groups["site"].Value;
        if (Zoom.IsMatch(t)) return "Zoom";
        if (Teams.IsMatch(t)) return "Microsoft Teams";
        return null;
    }
}
