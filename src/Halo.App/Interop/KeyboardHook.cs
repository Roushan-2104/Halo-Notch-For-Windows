using System.Runtime.InteropServices;
using Halo.Infrastructure;

namespace Halo.Interop;

/// <summary>
/// A low-level keyboard hook on its own thread with its own message loop. Windows silently removes a
/// hook whose callback takes longer than about a second, so the hook must never wait on the UI thread:
/// the filter decides synchronously whether to swallow a key and posts any work elsewhere.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    /// <summary>Return true to swallow the key so no other app sees it.</summary>
    public delegate bool KeyFilter(int vk, bool down);

    readonly KeyFilter _filter;
    readonly Native.LowLevelKeyboardProc _proc;
    readonly Thread _thread;
    readonly ManualResetEventSlim _started = new();
    uint _threadId;
    IntPtr _hook;

    public KeyboardHook(KeyFilter filter)
    {
        _filter = filter;
        _proc = HookProc;
        _thread = new Thread(Run) { IsBackground = true, Name = "Halo keyboard hook", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _started.Wait(2000);
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    void Run()
    {
        _threadId = Native.GetCurrentThreadId();
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) Log.Error($"Keyboard hook failed: {Marshal.GetLastWin32Error()}");
        _started.Set();

        while (Native.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
        }

        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
    }

    IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            bool down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            bool up = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;
            if (down || up)
            {
                var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
                try
                {
                    if (_filter((int)k.vkCode, down)) return new IntPtr(1);
                }
                catch (Exception ex)
                {
                    Log.Error("Keyboard filter threw", ex);
                }
            }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
