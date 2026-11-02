using System.Runtime.InteropServices;

namespace Halo.Modules.Productivity;

/// <summary>
/// Starts a Windows 11 focus session (Do Not Disturb plus the system focus timer) through the public
/// Windows.UI.Shell.FocusSessionManager. Halo's WinRT projection targets 10.0.19041, which predates that
/// API, so this goes through the raw ABI: activation factory, then vtable calls. Any failure (older
/// Windows, unpackaged app refused, policy) only means the notch timer runs on its own.
/// </summary>
internal sealed class FocusSession : IDisposable
{
    const string ClassName = "Windows.UI.Shell.FocusSessionManager";
    static readonly Guid StaticsIid = new("834df764-cb9a-5d0a-aa9f-73df4f249395");

    // Vtable slots, counted after IUnknown (3) and IInspectable (3), in the order the winmd declares them.
    const int StaticsGetDefault = 6, StaticsIsSupported = 7;
    const int ManagerTryStartUntil = 9; // get_IsFocusActive, GetSession, TryStartFocusSession, TryStartFocusSession(endTime)
    const int SessionEnd = 7; // get_Id, End

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetObjectFn(IntPtr self, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetBoolFn(IntPtr self, out byte value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int StartUntilFn(IntPtr self, long endTime, out IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int ActionFn(IntPtr self);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

    IntPtr _session;

    /// <summary>What happened on the last attempt, for the state pipe and the log.</summary>
    public string Status { get; private set; } = "unused";

    public bool IsActive => _session != IntPtr.Zero;

    public bool TryStart(DateTimeOffset end)
    {
        End();
        IntPtr name = IntPtr.Zero, statics = IntPtr.Zero, manager = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(ClassName, ClassName.Length, out name));
            var iid = StaticsIid;
            int hr = RoGetActivationFactory(name, ref iid, out statics);
            if (hr < 0)
            {
                Status = $"unavailable (0x{hr:X8})";
                return false;
            }
            Marshal.ThrowExceptionForHR(Slot<GetBoolFn>(statics, StaticsIsSupported)(statics, out var supported));
            if (supported == 0)
            {
                Status = "not supported";
                return false;
            }
            Marshal.ThrowExceptionForHR(Slot<GetObjectFn>(statics, StaticsGetDefault)(statics, out manager));
            if (manager == IntPtr.Zero)
            {
                Status = "no manager";
                return false;
            }
            // Windows.Foundation.DateTime is a FILETIME count in UTC.
            Marshal.ThrowExceptionForHR(Slot<StartUntilFn>(manager, ManagerTryStartUntil)(manager, end.ToFileTime(), out var session));
            if (session == IntPtr.Zero)
            {
                Status = "declined";
                return false;
            }
            _session = session;
            Status = "on";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"failed ({ex.GetType().Name}: {ex.Message})";
            return false;
        }
        finally
        {
            if (manager != IntPtr.Zero) Marshal.Release(manager);
            if (statics != IntPtr.Zero) Marshal.Release(statics);
            if (name != IntPtr.Zero) WindowsDeleteString(name);
        }
    }

    public void End()
    {
        if (_session == IntPtr.Zero) return;
        var session = _session;
        _session = IntPtr.Zero;
        try
        {
            Slot<ActionFn>(session, SessionEnd)(session);
            Status = "ended";
        }
        catch (Exception ex)
        {
            Status = $"end failed ({ex.Message})";
        }
        finally
        {
            Marshal.Release(session);
        }
    }

    public void Dispose() => End();

    static T Slot<T>(IntPtr obj, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(obj);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
    }
}
