using Windows.Foundation.Metadata;
using Windows.UI.Shell;

namespace Shammy.Modules.Productivity;

/// <summary>
/// Starts a Windows 11 focus session (Do Not Disturb plus the system focus timer) through the public
/// Windows.UI.Shell.FocusSessionManager. Any failure (Windows 10, policy, the shell declining) only
/// means the notch timer runs on its own.
/// </summary>
internal sealed class FocusSession : IDisposable
{
    Windows.UI.Shell.FocusSession? _session;

    /// <summary>What happened on the last attempt, for the state pipe and the log.</summary>
    public string Status { get; private set; } = "unused";

    public bool IsActive => _session is not null;

    public bool TryStart(DateTimeOffset end)
    {
        End();
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Shell.FocusSessionManager") || !FocusSessionManager.IsSupported)
            {
                Status = "not supported";
                return false;
            }
            _session = FocusSessionManager.GetDefault()?.TryStartFocusSession(end);
            Status = _session is null ? "declined" : "on";
            return _session is not null;
        }
        catch (Exception ex)
        {
            Status = $"failed ({ex.GetType().Name}: {ex.Message})";
            return false;
        }
    }

    public void End()
    {
        var session = _session;
        _session = null;
        if (session is null) return;
        try
        {
            session.End();
            Status = "ended";
        }
        catch (Exception ex)
        {
            Status = $"end failed ({ex.Message})";
        }
    }

    public void Dispose() => End();
}
