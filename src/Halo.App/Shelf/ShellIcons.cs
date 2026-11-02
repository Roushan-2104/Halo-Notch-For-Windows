using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Halo.Infrastructure;
using Halo.Interop;

namespace Halo.Shelf;

/// <summary>
/// Explorer-quality icons and thumbnails (photos, videos, PDFs show their content) via
/// IShellItemImageFactory, converted with alpha intact.
/// </summary>
internal static class ShellIcons
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE
    {
        public int cx, cy;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(
        string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    const int SIIGBF_RESIZETOFIT = 0x0;

    public static ImageSource? Get(string path, int size)
    {
        IntPtr hbmp = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
            if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_RESIZETOFIT, out hbmp) != 0) return null;
            return FromHBitmap(hbmp);
        }
        catch (Exception ex)
        {
            Log.Warn($"Icon for {path} failed", ex);
            return null;
        }
        finally
        {
            if (hbmp != IntPtr.Zero) Native.DeleteObject(hbmp);
        }
    }

    /// <summary>Imaging.CreateBitmapSourceFromHBitmap drops alpha, so read the bits top-down ourselves.</summary>
    static BitmapSource FromHBitmap(IntPtr hbmp)
    {
        Native.GetObject(hbmp, Marshal.SizeOf<Native.BITMAP>(), out var bm);
        int w = bm.bmWidth, h = bm.bmHeight, stride = w * 4;
        var bits = new byte[stride * h];
        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,
            biPlanes = 1,
            biBitCount = 32,
        };
        var dc = Native.GetDC(IntPtr.Zero);
        try { Native.GetDIBits(dc, hbmp, 0, (uint)h, bits, ref bmi, 0); }
        finally { Native.ReleaseDC(IntPtr.Zero, dc); }

        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bits, stride);
        src.Freeze();
        return src;
    }
}
