using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Shammy.Interop;

namespace Shammy.Modules.Wings;

/// <summary>Win32 and shell calls for the app wings that the shared Interop.Native doesn't have.</summary>
internal static class WingsNative
{
    // ---- App bars -------------------------------------------------------------------------------

    public const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_WINDOWPOSCHANGED = 9;
    public const uint ABE_TOP = 1;
    public const int ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;

    /// <summary>Same layout as the native struct (48 bytes on x64); cbSize must be set from Marshal.SizeOf.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public Native.RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

    // ---- Windows --------------------------------------------------------------------------------

    public const int GWL_STYLE = -16;
    public const long WS_EX_APPWINDOW = 0x40000, WS_EX_TRANSPARENT = 0x20, WS_CAPTION = 0x00C00000;
    public const uint GW_OWNER = 4, GW_HWNDNEXT = 2;
    public const int DWMWA_CLOAKED = 14, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    public const int SW_RESTORE = 9;
    public const uint WM_SYSCOMMAND = 0x0112, WM_GETICON = 0x007F, WM_WINDOWPOSCHANGED = 0x0047;
    public const int SC_MINIMIZE = 0xF020, SC_CLOSE = 0xF060;
    public const int ICON_BIG = 1, ICON_SMALL2 = 2, GCLP_HICON = -14, GCLP_HICONSM = -34;
    public const uint SMTO_ABORTIFHUNG = 0x2, SMTO_BLOCK = 0x1;
    public const uint MONITORINFOF_PRIMARY = 1;

    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B, EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x2;

    // Shell hook codes (wParam of the "SHELLHOOK" message).
    public const int HSHELL_WINDOWCREATED = 1, HSHELL_WINDOWDESTROYED = 2, HSHELL_WINDOWACTIVATED = 4, HSHELL_REDRAW = 6,
        HSHELL_RUDEAPPACTIVATED = 0x8004, HSHELL_FLASH = 0x8006, HSHELL_MONITORCHANGED = 16;

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] public static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd, int cmd);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] public static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern bool RegisterShellHookWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool DeregisterShellHookWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static string WindowTitle(IntPtr hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        // The marshaller hands Windows Capacity + 1 characters, so a max of Capacity always fits.
        var sb = new StringBuilder(Math.Min(len, 4096) + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsCloaked(IntPtr hwnd) => Native.DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>No rounded corners or accent border from Windows 11 on a strip that runs edge to edge.</summary>
    public static void SquareCorners(IntPtr hwnd)
    {
        int round = DWMWCP_DONOTROUND, none = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref none, sizeof(int));
    }

    public static List<IntPtr> Monitors()
    {
        var list = new List<IntPtr>();
        MonitorEnumProc proc = (m, _, _, _) =>
        {
            list.Add(m);
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        GC.KeepAlive(proc);
        return list;
    }

    // ---- Processes ------------------------------------------------------------------------------

    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const int APPMODEL_ERROR_NO_APPLICATION = 15703;

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder id);

    /// <summary>The exe path, and the app ID if it's a packaged (Store/MSIX) process.</summary>
    public static (string? Path, string? PackagedId) ProcessInfo(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return (null, null);
        try
        {
            string? path = null, id = null;
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (QueryFullProcessImageName(h, 0, sb, ref size)) path = sb.ToString();
            // APPLICATION_USER_MODEL_ID_MAX_LENGTH is 130 characters including the terminator.
            var idBuf = new StringBuilder(256);
            uint len = (uint)idBuf.Capacity;
            if (GetApplicationUserModelId(h, ref len, idBuf) == 0) id = idBuf.ToString();
            return (path, string.IsNullOrEmpty(id) ? null : id);
        }
        finally
        {
            CloseHandle(h);
        }
    }

    // ---- Property stores (app IDs) --------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    /// <summary>24 bytes on x64 like the native union; only the string case is read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PROPVARIANT
    {
        public ushort vt;
        ushort r1, r2, r3;
        public IntPtr p;
        IntPtr p2;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll")]
    static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);

    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT value);

    const ushort VT_LPWSTR = 31;
    static readonly PROPERTYKEY AppUserModelId = new() { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };

    static string? ReadAppId(IPropertyStore store)
    {
        var key = AppUserModelId;
        if (store.GetValue(ref key, out var pv) != 0) return null;
        try { return pv.vt == VT_LPWSTR && pv.p != IntPtr.Zero ? Marshal.PtrToStringUni(pv.p) : null; }
        finally { PropVariantClear(ref pv); }
    }

    /// <summary>The window's System.AppUserModel.ID (Store app frames, Chrome profiles and web apps set one).</summary>
    public static string? WindowAppId(IntPtr hwnd)
    {
        IPropertyStore? store = null;
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store is null) return null;
            var id = ReadAppId(store);
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (store is not null) Marshal.ReleaseComObject(store);
        }
    }

    // ---- Shortcuts ------------------------------------------------------------------------------

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class CShellLink
    {
    }

    /// <summary>All methods in vtable order; only GetPath is called.</summary>
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr pidl);
        [PreserveSig] int SetIDList(IntPtr pidl);
        [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int showCmd);
        [PreserveSig] int SetShowCmd(int showCmd);
        [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int index);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(IntPtr hwnd, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    /// <summary>A shortcut's target exe (environment variables expanded) and its app ID, without resolving it.</summary>
    public static (string? Target, string? AppId) ReadShortcut(string lnkPath)
    {
        object? link = null;
        try
        {
            link = new CShellLink();
            ((IPersistFile)link).Load(lnkPath, 0);
            string? target = null;
            var sb = new StringBuilder(1024);
            if (((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0) == 0 && sb.Length > 0)
                target = Environment.ExpandEnvironmentVariables(sb.ToString());
            string? id = link is IPropertyStore store ? ReadAppId(store) : null;
            return (target, string.IsNullOrWhiteSpace(id) ? null : id);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or FileNotFoundException or UnauthorizedAccessException)
        {
            return (null, null);
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
        }
    }

    // ---- Shell item names -----------------------------------------------------------------------

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr bindCtx, ref Guid handler, ref Guid iid, out IntPtr result);
        [PreserveSig] int GetParent(out IntPtr parent);
        [PreserveSig] int GetDisplayName(uint sigdn, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IntPtr other, uint hint, out int order);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);

    /// <summary>What Explorer calls a parsing name, e.g. "shell:AppsFolder\&lt;AUMID&gt;" → "Calculator".</summary>
    public static string? DisplayName(string parsingName)
    {
        IShellItem? item = null;
        IntPtr name = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out item) != 0 || item is null) return null;
            if (item.GetDisplayName(0 /* SIGDN_NORMALDISPLAY */, out name) != 0 || name == IntPtr.Zero) return null;
            return Marshal.PtrToStringUni(name);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name);
            if (item is not null) Marshal.ReleaseComObject(item);
        }
    }
}
