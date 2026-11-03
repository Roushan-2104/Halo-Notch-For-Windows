namespace Shammy.Core.Notifications;

/// <summary>
/// Which notifications in the history have been seen: the ones listed when the Alerts view was last opened (or
/// left), plus whatever was already in Action Center when Shammy started. "N new" counts the rest.
/// </summary>
public sealed class UnreadTracker
{
    readonly HashSet<uint> _seen = new();

    /// <summary>How many of <paramref name="ids"/> haven't been seen.</summary>
    public int CountUnread(IEnumerable<uint> ids) => ids.Count(id => !_seen.Contains(id));

    /// <summary>Everything in <paramref name="ids"/> is now seen; ids no longer in the history are forgotten.</summary>
    public void MarkSeen(IEnumerable<uint> ids)
    {
        _seen.Clear();
        _seen.UnionWith(ids);
    }
}
