using System.Windows.Threading;

namespace Halo.Interop;

/// <summary>
/// Routes the global keys Halo cares about: Right Alt (hold to search), the volume keys (so Windows'
/// own volume flyout never appears) and Escape while a search is open. Everything else passes through.
/// </summary>
internal sealed class GlobalKeys : IDisposable
{
    const int VK_RMENU = 0xA5, VK_ESCAPE = 0x1B;
    const int VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF;

    readonly Dispatcher _ui;
    readonly KeyboardHook _hook;
    volatile bool _searchHeld;

    public GlobalKeys(Dispatcher ui)
    {
        _ui = ui;
        _hook = new KeyboardHook(Filter);
    }

    public bool IsInstalled => _hook.IsInstalled;

    public volatile bool InterceptVolume;
    public volatile bool InterceptEscape;

    public event Action? SearchPressed;
    public event Action? SearchReleased;
    public event Action? EscapePressed;

    /// <summary>+1 volume up, -1 volume down, 0 mute toggle.</summary>
    public event Action<int>? VolumeKey;

    bool Filter(int vk, bool down)
    {
        switch (vk)
        {
            case VK_RMENU:
                if (down && !_searchHeld)
                {
                    _searchHeld = true;
                    Post(() => SearchPressed?.Invoke());
                }
                else if (!down && _searchHeld)
                {
                    _searchHeld = false;
                    Post(() => SearchReleased?.Invoke());
                }
                return true;

            case VK_VOLUME_UP or VK_VOLUME_DOWN or VK_VOLUME_MUTE when InterceptVolume:
                if (down)
                {
                    int dir = vk == VK_VOLUME_UP ? 1 : vk == VK_VOLUME_DOWN ? -1 : 0;
                    Post(() => VolumeKey?.Invoke(dir));
                }
                return true;

            case VK_ESCAPE when InterceptEscape:
                if (down) Post(() => EscapePressed?.Invoke());
                return true;
        }
        return false;
    }

    void Post(Action action) => _ui.BeginInvoke(action);

    public void Dispose() => _hook.Dispose();
}
