namespace Shammy.Infrastructure;

/// <summary>
/// Whether someone else may be watching the screen: an app has the microphone or camera open (a call)
/// or is capturing the screen. Notifications show only the app name while this is on.
/// </summary>
internal static class PrivacyState
{
    static volatile bool _presenting;

    public static bool Presenting => _presenting;

    public static void Set(bool presenting) => _presenting = presenting;
}
