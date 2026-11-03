using NAudio.CoreAudioApi;

namespace Shammy.SystemInfo;

internal static class AudioDeviceRelease
{
    /// <summary>
    /// Disposes a device without letting a failure turn into a crash. NAudio's endpoint-volume Dispose
    /// throws when the device has gone away (unplugged, driver reset) before it releases anything, and
    /// MMDevice's finalizer would then run it again on the finalizer thread, where a throw ends the
    /// process.
    /// </summary>
    public static void Dispose(MMDevice? device)
    {
        if (device is null) return;
        try
        {
            device.Dispose();
        }
        catch (Exception)
        {
            GC.SuppressFinalize(device);
        }
    }
}
