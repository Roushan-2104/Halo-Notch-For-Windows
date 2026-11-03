namespace Shammy.Infrastructure;

/// <summary>
/// Whether someone else may be watching the screen: an app has the microphone or camera open (a call)
/// or is capturing the screen. Notifications (pop-ups and the Notifications tab) show only the app name
/// while this is on.
/// </summary>
internal static class PrivacyState
{
    static volatile bool _presenting;

    public static bool Presenting => _presenting;

    /// <summary>Raised on the setter's thread when <see cref="Presenting"/> flips, so open views can re-hide or reveal.</summary>
    public static event Action<bool>? Changed;

    public static void Set(bool presenting)
    {
        if (_presenting == presenting) return;
        _presenting = presenting;
        Changed?.Invoke(presenting);
    }
}
