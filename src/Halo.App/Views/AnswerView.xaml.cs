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
        Result.Text = result;
        CopyButton.Content = "";
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
