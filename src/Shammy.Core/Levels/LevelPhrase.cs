using System.Globalization;
using System.Text.RegularExpressions;
using Shammy.Core.Conversions;

namespace Shammy.Core.Levels;

public enum LevelTarget { Volume, Brightness }

public enum LevelAction { Set, Up, Down, Mute, Unmute }

/// <summary><see cref="Percent"/> is clamped to 0–100 for Set; <see cref="Clamped"/> says the user asked for more (or less).</summary>
public sealed record LevelCommand(LevelTarget Target, LevelAction Action, int Percent = 0, bool Clamped = false);

/// <summary>
/// "volume 50", "set volume to 30", "brightness to 60%", "volume up", "mute", "volume 40 kar do". Anchored to the
/// whole text so "volume of a sphere" or "mute mic" (the meeting module's) never match.
/// </summary>
public static class LevelPhrase
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>How far "volume up" or "brightness down" moves.</summary>
    public const int Step = 10;

    const string Target = @"(?:the\s+)?(?:(?:system|sound|speaker|screen|display)\s+)?(?<t>volume|sound|brightness|awaa?z|aawaa?z)";
    const string Hinglish = @"(?:\s+(?:pe|par|pr)?\s*(?:kar\s*do|kardo|karo|kar\s*dijiye|kijiye|rakho|kar))?";

    static readonly Regex SetTo = new(
        @"^(?:(?:set|change|make|put|turn|adjust)\s+)?" + Target + @"(?:\s+level)?(?:\s+(?:to|at))?\s+(?<n>\d{1,4}(?:\.\d+)?|[a-z]+(?:[\s-][a-z]+)?)\s*(?:%|percent|per\s*cent)?" + Hinglish + "$", Opts);

    static readonly Regex Extreme = new(@"^(?:(?:set|turn)\s+)?" + Target + @"(?:\s+(?:to|at))?\s+(?<x>max|maximum|full|min|minimum|zero)" + Hinglish + "$", Opts);

    static readonly Regex UpDown = new(
        @"^(?:(?:turn|set|put)\s+)?" + Target + @"\s+(?<d>up|down|higher|lower|badha(?:o|\s*do)?|badhao|tez(?:\s+karo|\s+kar\s*do)?|kam(?:\s+karo|\s+kar\s*do)?|ghata(?:o|\s*do)?|dheere(?:\s+karo|\s+kar\s*do)?)$", Opts);

    static readonly Regex VerbFirst = new(@"^(?<d>increase|raise|decrease|lower|reduce|turn\s+up|turn\s+down)\s+" + Target + "$", Opts);

    static readonly Regex Mute = new(@"^(?<m>mute|unmute)(?:\s+(?:the\s+)?(?:sound|volume|audio|speakers?|computer|pc|laptop))?$|^(?:sound|volume|audio)\s+(?<m>mute|unmute|off|on)$", Opts);

    public static LevelCommand? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = Regex.Replace(text.Trim().TrimEnd('.', '!', '?').Trim(), @"\s+", " ");
        if (s.Length > 60) return null;

        if (Mute.Match(s) is { Success: true } mute)
        {
            var m = mute.Groups["m"].Value.ToLowerInvariant();
            return new LevelCommand(LevelTarget.Volume, m is "mute" or "off" ? LevelAction.Mute : LevelAction.Unmute);
        }
        if (Extreme.Match(s) is { Success: true } ex)
        {
            int p = ex.Groups["x"].Value.ToLowerInvariant() is "max" or "maximum" or "full" ? 100 : 0;
            return new LevelCommand(TargetOf(ex), LevelAction.Set, p);
        }
        if (UpDown.Match(s) is { Success: true } ud)
            return new LevelCommand(TargetOf(ud), IsUp(ud.Groups["d"].Value) ? LevelAction.Up : LevelAction.Down);
        if (VerbFirst.Match(s) is { Success: true } vf)
            return new LevelCommand(TargetOf(vf), IsUp(vf.Groups["d"].Value) ? LevelAction.Up : LevelAction.Down);
        if (SetTo.Match(s) is { Success: true } set && Number(set.Groups["n"].Value) is { } n)
        {
            int clamped = (int)Math.Round(Math.Clamp(n, 0, 100), MidpointRounding.AwayFromZero);
            return new LevelCommand(TargetOf(set), LevelAction.Set, clamped, n > 100);
        }
        return null;
    }

    static double? Number(string word)
    {
        if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        // Spoken "volume fifty"; "volume up" etc. never reach here as numbers.
        return NumberText.TryParse(word.Replace('-', ' '), out var w) && w >= 0 && word is not ("a" or "an" or "half" or "quarter") ? w : null;
    }

    static LevelTarget TargetOf(Match m) =>
        m.Groups["t"].Value.Equals("brightness", StringComparison.OrdinalIgnoreCase) ? LevelTarget.Brightness : LevelTarget.Volume;

    static bool IsUp(string d)
    {
        d = d.ToLowerInvariant();
        return d is "up" or "higher" or "increase" or "raise" || d.StartsWith("turn up", StringComparison.Ordinal)
            || d.StartsWith("badha", StringComparison.Ordinal) || d.StartsWith("tez", StringComparison.Ordinal);
    }

    /// <summary>The new level after a step, from <paramref name="current"/> (0–100).</summary>
    public static int Apply(LevelCommand c, int current) => c.Action switch
    {
        LevelAction.Set => c.Percent,
        LevelAction.Up => Math.Min(100, current + Step),
        LevelAction.Down => Math.Max(0, current - Step),
        _ => current,
    };

    /// <summary>"Set volume to 50%", "Turn brightness up", "Mute sound" for the palette row.</summary>
    public static string Describe(LevelCommand c)
    {
        var name = c.Target == LevelTarget.Volume ? "volume" : "brightness";
        return c.Action switch
        {
            LevelAction.Set => $"Set {name} to {c.Percent}%",
            LevelAction.Up => $"Turn {name} up",
            LevelAction.Down => $"Turn {name} down",
            LevelAction.Mute => "Mute sound",
            _ => "Unmute sound",
        };
    }
}
