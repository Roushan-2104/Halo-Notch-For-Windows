using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Halo.Core.Launcher;

/// <summary>
/// The last few characters typed anywhere, so a trigger like ";email" can be spotted as it is typed.
/// Not thread-safe: the keyboard hook owns it.
/// </summary>
public sealed class SnippetBuffer
{
    public const int Capacity = 40;

    /// <summary>A keyboard hook can't see mouse clicks, so a pause stands in for "the caret may have moved".</summary>
    public const long IdleResetMs = 3000;

    readonly StringBuilder _text = new(Capacity + 1);
    long _lastMs = long.MinValue;

    public string Text => _text.ToString();
    public int Length => _text.Length;

    public void Append(char c, long nowMs)
    {
        if (_lastMs != long.MinValue && nowMs - _lastMs > IdleResetMs) _text.Clear();
        _lastMs = nowMs;
        _text.Append(c);
        if (_text.Length > Capacity) _text.Remove(0, _text.Length - Capacity);
    }

    public void Backspace()
    {
        if (_text.Length > 0) _text.Length--;
    }

    public void Reset() => _text.Clear();

    /// <summary>The longest trigger the buffer ends with (case-insensitive), or null.</summary>
    public string? MatchTrigger(IEnumerable<string> triggers) => SnippetText.MatchTrigger(Text, triggers);
}

public static class SnippetText
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string TimeFormat = "HH:mm";

    public static Dictionary<string, string> Defaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        [";date"] = "{date}",
        [";time"] = "{time}",
        [";shrug"] = "¯\\_(ツ)_/¯",
    };

    /// <summary>
    /// An existing trigger that is a prefix of <paramref name="trigger"/> or starts with it. Expansion fires
    /// on the first match while typing, so of two such triggers the longer one could never be typed.
    /// Replacing the same trigger isn't a clash.
    /// </summary>
    public static string? Clash(string trigger, IEnumerable<string> existing) =>
        existing.FirstOrDefault(k => !k.Equals(trigger, StringComparison.OrdinalIgnoreCase) &&
            (k.StartsWith(trigger, StringComparison.OrdinalIgnoreCase) || trigger.StartsWith(k, StringComparison.OrdinalIgnoreCase)));

    public static string? MatchTrigger(string typed, IEnumerable<string> triggers)
    {
        string? best = null;
        foreach (var t in triggers)
        {
            if (t.Length > 0 && typed.EndsWith(t, StringComparison.OrdinalIgnoreCase) && (best is null || t.Length > best.Length))
                best = t;
        }
        return best;
    }

    /// <summary>Fills {date}, {time} and {clipboard}; the clipboard is only read when the token is used.</summary>
    public static string Expand(string template, DateTime now, Func<string?> clipboard)
    {
        var text = template.Replace("\r\n", "\n");
        text = Replace(text, "{date}", () => now.ToString(DateFormat, CultureInfo.InvariantCulture));
        text = Replace(text, "{time}", () => now.ToString(TimeFormat, CultureInfo.InvariantCulture));
        text = Replace(text, "{clipboard}", () => (clipboard() ?? "").Replace("\r\n", "\n"));
        return text;
    }

    static string Replace(string text, string token, Func<string> value) =>
        text.Contains(token, StringComparison.OrdinalIgnoreCase) ? text.Replace(token, value(), StringComparison.OrdinalIgnoreCase) : text;

    /// <summary>';' plus 1–24 characters without spaces, so it can't appear in ordinary prose by accident.</summary>
    public static bool IsValidTrigger(string trigger) =>
        trigger.Length is >= 2 and <= 25 && trigger[0] == ';' && !trigger.Any(char.IsWhiteSpace);
}

public enum SnippetAction { Add, Delete, List }

public sealed record SnippetCommand(SnippetAction Action, string? Trigger = null, string? Expansion = null);

/// <summary>"snippet ;sig Best regards", "delete snippet ;sig", "snippets".</summary>
public static class SnippetCommands
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly Regex List = new(@"^(?:show\s+|list\s+|my\s+|show\s+my\s+)?(?:text\s+)?snippets$", Opts);
    static readonly Regex Delete = new(@"^(?:delete|remove)\s+(?:the\s+)?snippet\s+(?<trigger>;?\S+)$", Opts);
    static readonly Regex Add = new(@"^(?:add\s+|new\s+)?snippet\s+(?<trigger>;\S+)\s+(?<text>.+)$", Opts | RegexOptions.Singleline);

    public static SnippetCommand? Parse(string text)
    {
        text = text.Trim();
        if (List.IsMatch(text)) return new SnippetCommand(SnippetAction.List);

        var m = Delete.Match(text);
        if (m.Success)
        {
            var trigger = m.Groups["trigger"].Value;
            if (trigger[0] != ';') trigger = ";" + trigger;
            return SnippetText.IsValidTrigger(trigger) ? new SnippetCommand(SnippetAction.Delete, trigger.ToLowerInvariant()) : null;
        }

        m = Add.Match(text);
        if (m.Success && SnippetText.IsValidTrigger(m.Groups["trigger"].Value))
        {
            // The type box is one line, so a literal "\n" stands for a line break.
            var expansion = m.Groups["text"].Value.Trim().Replace("\\n", "\n");
            return new SnippetCommand(SnippetAction.Add, m.Groups["trigger"].Value.ToLowerInvariant(), expansion);
        }
        return null;
    }
}
