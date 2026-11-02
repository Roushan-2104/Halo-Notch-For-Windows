using System.Windows;
using System.Windows.Controls;

namespace Halo.Views;

/// <summary>Instant local answers (arithmetic) that don't need a web search.</summary>
public partial class AnswerView : UserControl
{
    string _query = "";

    public AnswerView() => InitializeComponent();

    internal event Action<string>? SearchWebRequested;

    internal void Show(string query, string expression, string result)
    {
        _query = query;
        Expression.Text = expression + " =";
        Expression.Visibility = Visibility.Visible;
        Result.Text = result;
        Result.FontSize = 34;
        Result.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Fg"];
        CopyButton.Content = "";
        GlyphOrb.Visibility = Visibility.Collapsed;
        Actions.Visibility = Visibility.Visible;
    }

    /// <summary>Confirmation for a module command ("Timer set", "Playing …").</summary>
    internal void ShowCommand(Modules.CommandResult r)
    {
        Glyph.Text = r.Glyph;
        Glyph.Foreground = (System.Windows.Media.Brush)Application.Current.Resources[r.IsError ? "Red" : "AccentBrush"];
        GlyphOrb.Visibility = Visibility.Visible;
        Result.Text = r.Title;
        Result.FontSize = 19;
        Result.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Fg"];
        Expression.Text = r.Detail ?? "";
        Expression.Visibility = string.IsNullOrEmpty(r.Detail) ? Visibility.Collapsed : Visibility.Visible;
        Actions.Visibility = Visibility.Collapsed;
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Result.Text.Replace(",", ""));
            CopyButton.Content = "";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another app is holding the clipboard; nothing useful to do.
        }
    }

    void OnSearchWeb(object sender, RoutedEventArgs e) => SearchWebRequested?.Invoke(_query);
}
