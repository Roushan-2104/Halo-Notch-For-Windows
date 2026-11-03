using System.Windows;
using System.Windows.Controls;

namespace Shammy.Views;

/// <summary>
/// Instant local answers (arithmetic) that don't need a web search, and confirmations for commands. Focus Wall: the
/// answer as a big tight numeral, Copy as the one accent control, Save as note and Search quiet beside it.
/// </summary>
public partial class AnswerView : UserControl
{
    string _query = "";
    string? _copyText;

    public AnswerView() => InitializeComponent();

    internal event Action<string>? SearchWebRequested;

    internal void Show(string query, string expression, string result)
    {
        _query = query;
        Expression.Text = expression + " =";
        Expression.Visibility = Visibility.Visible;
        Result.Text = result;
        // A sum's answer is the poster: ExtraBold and tight.
        WallParts.Face(Result, FontWeights.ExtraBold);
        Result.FontSize = FitSize(result, 40, 24);
        Result.LineHeight = Result.FontSize + 4;
        CopyButton.Content = "";
        NoteButton.Content = "";
        GlyphOrb.Visibility = Visibility.Collapsed;
        _copyText = null;
        SearchWebButton.Visibility = Visibility.Visible;
        Actions.Visibility = Visibility.Visible;
    }

    /// <summary>Confirmation for a module command ("Timer set", "Playing …").</summary>
    internal void ShowCommand(Modules.CommandResult r)
    {
        Glyph.Text = r.Glyph;
        // Colour only for meaning: red when it failed, white otherwise.
        Glyph.Foreground = (System.Windows.Media.Brush)Application.Current.Resources[r.IsError ? "WallRed" : "WallText"];
        GlyphOrb.Visibility = Visibility.Visible;
        Result.Text = r.Title;
        WallParts.Face(Result, FontWeights.Bold);
        Result.FontSize = 19;
        Result.LineHeight = 24;
        Expression.Text = r.Detail ?? "";
        Expression.Visibility = string.IsNullOrEmpty(r.Detail) ? Visibility.Collapsed : Visibility.Visible;
        // An answer worth keeping (a converted amount, a time elsewhere) gets Copy and Save as note; the rest need none.
        _copyText = r.CopyText;
        NoteButton.Content = "";
        CopyButton.Content = "";
        SearchWebButton.Visibility = Visibility.Collapsed;
        Actions.Visibility = r.CopyText is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Width the answer has beside the buttons (the card is a fixed 440 wide).</summary>
    const double ResultRoom = 270;

    /// <summary>Long answers step down from the poster size instead of being cut off with an ellipsis.</summary>
    double FitSize(string text, double size, double min)
    {
        var face = new System.Windows.Media.Typeface(Result.FontFamily, FontStyles.Normal, Result.FontWeight, FontStretches.Normal);
        double width = new System.Windows.Media.FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            face, size, Result.Foreground, System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip).Width;
        return width <= ResultRoom ? size : Math.Max(min, Math.Floor(size * ResultRoom / width));
    }

    /// <summary>For the test pipe: the shown answer and whether it offers Copy.</summary>
    internal string Describe() => $"title={Result.Text} copy={(Actions.Visibility == Visibility.Visible && CopyButton.IsVisible ? _copyText ?? Result.Text : "-")}";

    void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_copyText ?? Result.Text.Replace(",", ""));
            CopyButton.Content = "";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another app is holding the clipboard; nothing useful to do.
        }
    }

    void OnSearchWeb(object sender, RoutedEventArgs e) => SearchWebRequested?.Invoke(_query);

    /// <summary>"Save as note": the answer as "12 × 7 = 84"; a listener (Connect) saves it.</summary>
    internal event Func<string, bool>? SaveNoteRequested;

    void OnSaveNote(object sender, RoutedEventArgs e)
    {
        var expression = Expression.Text.TrimEnd('=', ' ');
        var text = expression.Length > 0 ? $"{expression} = {Result.Text}" : Result.Text;
        if (SaveNoteRequested?.Invoke(text) == true) NoteButton.Content = "";
    }
}
