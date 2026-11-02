using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Halo.Core.Launcher;
using Halo.Interop;
using static Halo.Modules.Launcher.SnippetNative;

namespace Halo.Modules.Launcher;

/// <summary>
/// System-wide text expansion: type ";sig" anywhere and it becomes "Best regards, …". A watch-only keyboard
/// hook keeps the last 40 typed characters; when they end with a trigger, a background thread erases the
/// trigger and types the expansion. Managed with "snippet ;sig …", "delete snippet ;sig" and "snippets".
/// </summary>
internal sealed class SnippetsModule : IHaloModule, ITextCommand
{
    // A keyboard hook can't tell a password box from any other text field, so the apps where passwords are
    // typed most (password managers, Windows credential and lock prompts) and Halo itself are never watched.
    static readonly HashSet<string> SkipProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "1password", "keepass", "keepassxc", "bitwarden", "dashlane", "lastpass", "nordpass", "roboform", "enpass",
        "keeperpasswordmanager", "protonpass", "passwordsafe", "pwsafe", "credentialuibroker", "consent", "logonui", "lockapp",
    };

    sealed class Data
    {
        public bool Enabled { get; set; } = true;
        public Dictionary<string, string>? Snippets { get; set; }
    }

    readonly SnippetBuffer _buffer = new();
    readonly object _gate = new();
    readonly uint _ownPid = (uint)Environment.ProcessId;
    readonly byte[] _keyState = new byte[256];
    readonly char[] _chars = new char[8];

    ModuleContext _ctx = null!;
    Data _data = new();
    Dictionary<string, string> _snippets = new(StringComparer.OrdinalIgnoreCase);
    volatile Dictionary<string, string> _live = new(StringComparer.OrdinalIgnoreCase);
    SnippetsPanel _panel = null!;
    DispatcherTimer _release = null!;
    SnippetKeyHook? _hook;
    SnippetTyper? _typer;
    bool _holding;

    // Hook thread state (guarded by _gate).
    IntPtr _foreground;
    string? _skipped;
    string? _lastTrigger;
    DateTime _lastAt;

    public string Name => "snippets";

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        _data = ctx.Load<Data>();
        foreach (var (trigger, text) in _data.Snippets ?? SnippetText.Defaults())
            if (SnippetText.IsValidTrigger(trigger)) _snippets[trigger.ToLowerInvariant()] = text;
        Publish();

        _panel = new SnippetsPanel();
        _panel.DeleteRequested += trigger =>
        {
            if (_snippets.Remove(trigger)) Changed();
        };
        ctx.AddTab("", "Snippets", _panel);
        _panel.Show(_snippets);

        // OpenTab(hold) keeps the notch open until something collapses it, and the API has no way to drop
        // just our hold, so let go once the user has had a look and the pointer isn't on the notch.
        _release = new DispatcherTimer(TimeSpan.FromSeconds(9), DispatcherPriority.Normal, (_, _) =>
        {
            _release.Stop();
            if (!ctx.Notch.IsMouseOver) Release();
        }, ctx.Ui) { IsEnabled = false };
        ctx.Notch.MouseLeave += (_, _) =>
        {
            if (_holding && !_release.IsEnabled) Release();
        };

        ctx.AddCommand(this);
        ctx.AddTrayToggle("Text snippets", () => _data.Enabled, on =>
        {
            _data.Enabled = on;
            Save();
            Apply();
        });
        ctx.AddPipeCommand("snippets-expand", Preview);
        ctx.AddPipeCommand("snippets-state", _ => State());
        Apply();
    }

    // ---- Commands --------------------------------------------------------------------------------

    public bool CanHandle(string text) => SnippetCommands.Parse(text) is not null;

    public Task<CommandResult> RunAsync(string text)
    {
        var command = SnippetCommands.Parse(text);
        CommandResult result;
        switch (command)
        {
            case { Action: SnippetAction.Add, Trigger: { } trigger, Expansion: { } expansion }:
                bool replaced = _snippets.ContainsKey(trigger);
                _snippets[trigger] = expansion;
                Changed();
                result = new CommandResult("", replaced ? "Snippet updated" : "Snippet saved",
                    $"{trigger} → {OneLine(expansion)}" + (_data.Enabled ? "" : " (snippets are off in the tray)"));
                break;
            case { Action: SnippetAction.Delete, Trigger: { } trigger }:
                result = _snippets.Remove(trigger)
                    ? new CommandResult("", "Snippet deleted", trigger)
                    : new CommandResult("", $"No snippet {trigger}", null, IsError: true);
                if (!result.IsError) Changed();
                break;
            case { Action: SnippetAction.List }:
                ShowPanel();
                result = new CommandResult("", "Snippets", _snippets.Count == 1 ? "1 snippet" : $"{_snippets.Count} snippets");
                break;
            default:
                result = new CommandResult("", "Not a snippet command", text, IsError: true);
                break;
        }
        return Task.FromResult(result);
    }

    void ShowPanel()
    {
        _panel.Show(_snippets);
        _ctx.OpenTab(_panel, hold: true);
        _holding = true;
        _release.Stop();
        _release.Start();
    }

    void Release()
    {
        if (!_holding) return;
        _holding = false;
        _ctx.Notch.Collapse();
    }

    void Changed()
    {
        Publish();
        Save();
        _panel.Show(_snippets);
    }

    void Publish() => _live = new Dictionary<string, string>(_snippets, StringComparer.OrdinalIgnoreCase);

    void Save()
    {
        _data.Snippets = new Dictionary<string, string>(_snippets);
        _ctx.Save(_data);
    }

    void Apply()
    {
        if (_data.Enabled && _hook is null)
        {
            _typer ??= new SnippetTyper(ReadClipboard, Warn);
            _hook = new SnippetKeyHook(OnKey, Warn);
            _ctx.Info($"Text snippets on (hook installed: {_hook.IsInstalled})");
        }
        else if (!_data.Enabled && _hook is not null)
        {
            _hook.Dispose();
            _hook = null;
            lock (_gate) _buffer.Reset();
            _ctx.Info("Text snippets off");
        }
    }

    // ---- Keyboard hook thread --------------------------------------------------------------------

    /// <summary>Runs inside the hook callback: no waiting, no UI, never swallows the key.</summary>
    void OnKey(uint vk, uint scanCode, uint flags)
    {
        // Our own expansion (and other tools' typing) comes back through the hook marked as injected.
        if ((flags & Native.LLKHF_INJECTED) != 0) return;

        var fg = Native.GetForegroundWindow();
        uint thread = Native.GetWindowThreadProcessId(fg, out uint pid);
        lock (_gate)
        {
            if (fg != _foreground)
            {
                _foreground = fg;
                _buffer.Reset();
                _skipped = SkipReason(pid);
            }
            if (_skipped is not null) return;

            switch (vk)
            {
                // Modifiers and lock keys only change what the next key types.
                case 0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C or 0x90 or 0x91 or >= 0xA0 and <= 0xA5:
                    return;
            }

            bool ctrl = IsDown(VK_CONTROL), alt = IsDown(VK_MENU);
            // Shortcuts (Ctrl+V, Ctrl+Backspace, Alt+Tab, Win+…) can change the text or move the caret.
            // Ctrl+Alt together is AltGr, which types characters on many layouts.
            if (IsDown(VK_LWIN) || IsDown(VK_RWIN) || ctrl != alt)
            {
                _buffer.Reset();
                return;
            }
            if (vk == VK_BACK)
            {
                _buffer.Backspace();
                return;
            }

            // Enter, Tab, Escape, arrows, Home/End, Page Up/Down and function keys type nothing printable,
            // and they all might move the caret, so they start the buffer over.
            var c = Translate(vk, scanCode, thread, ctrl && alt);
            if (c is null)
            {
                _buffer.Reset();
                return;
            }

            _buffer.Append(c.Value, Environment.TickCount64);
            var live = _live;
            var trigger = _buffer.MatchTrigger(live.Keys);
            if (trigger is null) return;
            _buffer.Reset();
            _lastTrigger = trigger;
            _lastAt = DateTime.Now;
            _typer?.Enqueue(trigger.Length, live[trigger]);
        }
    }

    /// <summary>
    /// What the key types in the foreground app's keyboard layout. The hook thread has no key state of its
    /// own, so Shift/AltGr/Caps Lock are read from the global state.
    /// </summary>
    char? Translate(uint vk, uint scanCode, uint thread, bool altGr)
    {
        Array.Clear(_keyState);
        if (IsDown(VK_SHIFT)) _keyState[VK_SHIFT] = 0x80;
        if (altGr) _keyState[VK_CONTROL] = _keyState[VK_MENU] = 0x80;
        if ((GetKeyState(VK_CAPITAL) & 1) != 0) _keyState[VK_CAPITAL] = 0x01;
        int n = ToUnicodeEx(vk, scanCode, _keyState, _chars, _chars.Length, TOUNICODE_NO_STATE_CHANGE, GetKeyboardLayout(thread));
        // 0: nothing, -1: a dead key, >1: a dead key that couldn't combine. None of them are part of a trigger.
        if (n != 1 || char.IsControl(_chars[0])) return null;
        return _chars[0];
    }

    string? SkipReason(uint pid)
    {
        if (pid == 0) return null;
        if (pid == _ownPid) return "halo";
        var name = ProcessName(pid);
        return name is not null && SkipProcesses.Contains(name) ? name : null;
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    string? ReadClipboard()
    {
        try
        {
            return _ctx.Ui.Invoke(() => Clipboard.ContainsText() ? Clipboard.GetText() : null,
                DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            Warn("Reading the clipboard for a snippet failed", ex);
            return null;
        }
    }

    void Warn(string message, Exception? ex) => _ctx.Warn(message, ex);

    static string OneLine(string s) => s.Replace("\r\n", "\n").Replace("\n", " ↵ ");

    /// <summary>Pipe: "snippets-expand thanks ;sig" → what typing that would produce, without typing it.</summary>
    string Preview(string text)
    {
        var live = _live;
        var trigger = SnippetText.MatchTrigger(text, live.Keys);
        if (trigger is null) return "no trigger";
        var expanded = SnippetText.Expand(live[trigger], DateTime.Now, ReadClipboard);
        return $"{trigger} -> {expanded.Replace("\r", "").Replace("\n", "\\n")}";
    }

    string State()
    {
        lock (_gate)
        {
            // The buffer's contents are never reported: it may hold anything the user typed.
            return $"enabled={_data.Enabled} hook={_hook?.IsInstalled ?? false} snippets={_live.Count} " +
                   $"triggers={string.Join(",", _live.Keys.OrderBy(k => k, StringComparer.Ordinal))} buffer={_buffer.Length} " +
                   $"skipping={_skipped ?? "-"} " +
                   $"last={(_lastTrigger is null ? "-" : $"{_lastTrigger}@{_lastAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}")}";
        }
    }

    public void Dispose()
    {
        _hook?.Dispose();
        _typer?.Dispose();
        _release?.Stop();
    }
}
