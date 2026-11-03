using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shammy.Core.Settings;
using Shammy.Infrastructure;
using Shammy.Modules.Kit;
using Shammy.Search;

namespace Shammy.Modules.Settings;

/// <summary>
/// The Settings card's compact face on the home row. Its detail is the designed Settings screen
/// (<see cref="SettingsView"/>), which <see cref="SettingsModule"/> builds.
/// </summary>
internal sealed class SettingsCard
{
    const string Accent = NotchPalette.Purple;
    readonly AppSettings _settings;
    readonly Func<IReadOnlyList<string>> _modules;
    readonly Grid _compact = new() { Margin = new Thickness(14) };

    public SettingsCard(AppSettings settings, Func<IReadOnlyList<string>> modules)
    {
        _settings = settings;
        _modules = modules;
        _compact.IsVisibleChanged += (_, _) =>
        {
            if (_compact.IsVisible) RefreshCompact();
        };
        RefreshCompact();
    }

    public FrameworkElement Compact => _compact;

    void RefreshCompact()
    {
        _compact.Children.Clear();
        var stack = new StackPanel();
        stack.Children.Add(CardKit.Glyph("", 22, CardKit.Brush(Accent)));
        var title = CardKit.Label("Settings", 16);
        title.Margin = new Thickness(0, 10, 0, 2);
        stack.Children.Add(title);
        stack.Children.Add(CardKit.Label($"Hover {_settings.HoverSpeed.ToString().ToLowerInvariant()} · sound {_settings.NotchSoundVolume}%", 11, FontWeights.Medium, CardKit.Text2));
        stack.Children.Add(CardKit.Label(_settings.Use24Hour is { } h ? (h ? "24-hour clock" : "12-hour clock") : "Clock follows Windows", 11, FontWeights.Medium, CardKit.Text2));
        _compact.Children.Add(stack);
    }

    /// <summary>Refreshes the compact card after a change (the open Settings screen follows changes itself).</summary>
    public void Refresh() => RefreshCompact();
}
