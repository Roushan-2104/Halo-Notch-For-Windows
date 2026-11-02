using System.Runtime.InteropServices;
using Halo.Interop;

namespace Halo.Modules.Launcher;

/// <summary>
/// A watch-only low-level keyboard hook, like Interop.KeyboardHook but it also hands over the scan code
/// and flags (needed for ToUnicodeEx and to skip injected keys) and can never swallow a key. Runs on its
/// own thread; the observer must return quickly or Windows silently unhooks it.
/// </summary>
internal sealed class SnippetKeyHook : IDisposable
{
    public delegate void KeyObserver(uint vk, uint scanCode, uint flags);

    readonly KeyObserver _observer;
    readonly Action<string, Exception?> _warn;
    readonly Native.LowLevelKeyboardProc _proc;
    readonly ManualResetEventSlim _started = new();
    uint _threadId;
    IntPtr _hook;

    public SnippetKeyHook(KeyObserver observer, Action<string, Exception?> warn)
    {
        _observer = observer;
        _warn = warn;
        _proc = HookProc;
        var thread = new Thread(Run) { IsBackground = true, Name = "Halo snippet hook", Priority = ThreadPriority.AboveNormal };
        thread.Start();
        _started.Wait(2000);
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    void Run()
    {
        _threadId = Native.GetCurrentThreadId();
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) _warn($"Snippet keyboard hook failed: {Marshal.GetLastWin32Error()}", null);
        _started.Set();

        while (Native.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
        }

        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)
        {
            var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            try { _observer(k.vkCode, k.scanCode, k.flags); }
            catch (Exception ex) { _warn("Snippet key observer threw", ex); }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
