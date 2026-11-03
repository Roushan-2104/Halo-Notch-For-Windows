using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Shammy.Infrastructure;

/// <summary>
/// The app used to be called Halo (Halo.exe). Updating to Shammy.exe leaves the old files in the install
/// folder and the Desktop / Start menu shortcuts still open them, which would start the old version.
/// This points those shortcuts at Shammy, renames them, and removes the old program files.
/// </summary>
static class RenameCleanup
{
    const string OldName = "Halo";

    public static void Run()
    {
        if (Environment.ProcessPath is not { } exe) return;
        var current = Path.GetDirectoryName(exe)!;
        // Only the installed copy, which lives in …\HaloNotch\current\.
        if (!current.EndsWith(@"\HaloNotch\current", StringComparison.OrdinalIgnoreCase)) return;
        var root = Path.GetDirectoryName(current)!;
        var oldTargets = new[] { Path.Combine(current, OldName + ".exe"), Path.Combine(root, OldName + ".exe") };

        foreach (var folder in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        })
        {
            try { FixShortcut(Path.Combine(folder, OldName + ".lnk"), oldTargets, exe, rename: true); }
            catch (Exception ex) { Log.Warn($"Updating the {OldName} shortcut in {folder} failed", ex); }
        }

        // A pinned taskbar icon is found by its file name, so it's re-pointed but keeps its name.
        var pins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        try
        {
            if (Directory.Exists(pins))
                foreach (var lnk in Directory.EnumerateFiles(pins, "*.lnk"))
                    FixShortcut(lnk, oldTargets, exe, rename: false);
        }
        catch (Exception ex) { Log.Warn("Updating a pinned taskbar icon failed", ex); }

        int removed = 0;
        foreach (var file in new[] { "Halo.exe", "Halo.dll", "Halo.Core.dll", "Halo.deps.json", "Halo.runtimeconfig.json", "Halo.pdb", "Halo.Core.pdb" })
            removed += TryDelete(Path.Combine(current, file));
        removed += TryDelete(Path.Combine(root, OldName + ".exe"));
        if (removed > 0) Log.Info($"Removed {removed} leftover file(s) from the old Halo name");
    }

    static void FixShortcut(string path, string[] oldTargets, string exe, bool rename)
    {
        if (!File.Exists(path)) return;
        var link = (IShellLinkW)new ShellLink();
        try
        {
            var file = (IPersistFile)link;
            file.Load(path, 2 /* STGM_READWRITE */);
            var target = new StringBuilder(1024);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            var current = target.ToString();
            if (!oldTargets.Any(t => string.Equals(t, current, StringComparison.OrdinalIgnoreCase))) return;

            link.SetPath(exe);
            link.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
            link.SetIconLocation(exe, 0);
            var saveAs = rename ? Path.Combine(Path.GetDirectoryName(path)!, "Shammy.lnk") : path;
            file.Save(saveAs, true);
            if (rename && !string.Equals(saveAs, path, StringComparison.OrdinalIgnoreCase)) File.Delete(path);
            Log.Info($"Shortcut now opens Shammy: {saveAs}");
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    static int TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            File.Delete(path);
            return 1;
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't remove {Path.GetFileName(path)}", ex);
            return 0;
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
