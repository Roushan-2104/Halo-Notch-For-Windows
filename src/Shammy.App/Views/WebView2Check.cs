using Microsoft.Web.WebView2.Core;

namespace Shammy.Views;

/// <summary>The results card runs on the Microsoft Edge WebView2 Runtime; this finds it or sends the user to get it.</summary>
internal static class WebView2Check
{
    // Microsoft's own Evergreen bootstrapper (a ~2 MB installer that fetches the runtime).
    const string DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    public static bool IsInstalled()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void OpenDownload() => ResultView.OpenExternal(DownloadUrl);
}
