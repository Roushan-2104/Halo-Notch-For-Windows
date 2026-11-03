using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shammy.Core.DropActions;
using Shammy.Modules;
using Shammy.Modules.DropActions;
using Shammy.Modules.Kit;
using Shammy.Shell;

namespace Shammy.Views;

/// <summary>
/// What the notch shows while files are dragged over it (v2 "Drop a file"): a chip for what's being dragged,
/// and four targets: Shelf, Send to phone, Compress (.zip) and Convert (PNG ↔ JPG). The target under the
/// pointer lights up; <see cref="TakeTarget"/> tells the window where the files landed.
/// </summary>
public partial class DropView : UserControl
{
    const string Accent = NotchPalette.Purple;

    sealed record Target(DropAction Action, Grid Root, System.Windows.Shapes.Rectangle Frame, Border Tile, TextBlock Icon, TextBlock Subtitle, string Normal);

    readonly List<Target> _targets = new();
    readonly TextBlock _chipText = CardKit.Label("", 13, FontWeights.SemiBold);
    readonly TextBlock _chipSize = CardKit.Label("", 12, FontWeights.Medium, CardKit.Text2);
    readonly TextBlock _chipIcon = CardKit.Glyph("", 15, CardKit.Brush(NotchPalette.Calendar));
    IDataObject? _data;
    string[] _files = Array.Empty<string>();
    DropAction? _hovered;

    public DropView()
    {
        InitializeComponent();
        // Fits the window: the design's 924 where the notch window is wide enough, else its width less a margin.
        Width = Math.Min(924, NotchWindow.WindowWidth - 40);
        Build();
        PreviewDragEnter += OnDragOver;
        PreviewDragOver += OnDragOver;
    }

    /// <summary>Files dropped on Send to phone, Compress or Convert (Shelf drops keep the window's own path).</summary>
    internal event Action<DropAction, string[]>? ActionDropped;

    /// <summary>The target under the pointer, if any.</summary>
    internal DropAction? Hovered => _hovered;

    /// <summary>Where the files landed (the hovered target), clearing the highlight for the next drag.</summary>
    internal DropAction? TakeTarget()
    {
        var t = _hovered;
        Reset();
        return t;
    }

    internal void Reset()
    {
        _data = null;
        SetHovered(null);
    }

    internal void RaiseDropped(DropAction action, string[] files) => ActionDropped?.Invoke(action, files);

    void Build()
    {
        var header = new DockPanel { Height = 36, Margin = new Thickness(0, 0, 0, 12), LastChildFill = false };
        var chipRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _chipIcon.Margin = new Thickness(0, 0, 9, 0);
        chipRow.Children.Add(_chipIcon);
        chipRow.Children.Add(_chipText);
        _chipSize.Margin = new Thickness(6, 0, 0, 0);
        chipRow.Children.Add(_chipSize);
        var chip = new Border
        {
            Height = 36,
            CornerRadius = new CornerRadius(12),
            Background = CardKit.Raised,
            BorderBrush = CardKit.Brush("#2E2E33"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 0, 14, 0),
            MaxWidth = 380,
            Child = chipRow,
            RenderTransform = new RotateTransform(-2),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        header.Children.Add(chip);
        var dropTo = CardKit.Caption("Drop to");
        dropTo.Margin = new Thickness(14, 0, 0, 0);
        header.Children.Add(dropTo);
        var cancel = CardKit.Label("Release outside the notch to cancel", 11, FontWeights.Normal, CardKit.Text2);
        DockPanel.SetDock(cancel, Dock.Right);
        header.Children.Add(cancel);
        DockPanel.SetDock(header, Dock.Top);
        Root.Children.Add(header);

        var grid = new Grid();
        for (int i = 0; i < 4; i++)
        {
            if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        AddTarget(grid, 0, DropAction.Shelf, "", "Shelf", "Keep for later");
        AddTarget(grid, 2, DropAction.Phone, "", "Send to phone", "QR · same Wi-Fi");
        AddTarget(grid, 4, DropAction.Compress, "", "Compress", ".zip");
        AddTarget(grid, 6, DropAction.Convert, "", "Convert", "PNG ↔ JPG");
        Root.Children.Add(grid);
    }

    void AddTarget(Grid grid, int column, DropAction action, string glyph, string title, string subtitle)
    {
        var root = new Grid { Background = Brushes.Transparent };
        var frame = new System.Windows.Shapes.Rectangle
        {
            RadiusX = 18,
            RadiusY = 18,
            StrokeThickness = 2,
        };
        root.Children.Add(frame);
        var icon = CardKit.Glyph(glyph, 20);
        var tile = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(14), Child = icon, HorizontalAlignment = HorizontalAlignment.Left };
        var sub = CardKit.Label(subtitle, 11, FontWeights.Normal, CardKit.Text2);
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 12, 0) };
        content.Children.Add(tile);
        var name = CardKit.Label(title, 15, FontWeights.Bold);
        name.Margin = new Thickness(0, 10, 0, 3);
        content.Children.Add(name);
        content.Children.Add(sub);
        root.Children.Add(content);
        Grid.SetColumn(root, column);
        grid.Children.Add(root);
        var t = new Target(action, root, frame, tile, icon, sub, subtitle);
        _targets.Add(t);
        Paint(t, false);
        CardKit.AutomationName(root, $"Drop on {title}, {subtitle}");
    }

    static void Paint(Target t, bool hot)
    {
        t.Frame.Stroke = hot ? CardKit.Brush(Accent) : CardKit.Brush("#3A3A40");
        t.Frame.StrokeDashArray = hot ? null : new DoubleCollection { 4, 3 };
        t.Frame.Fill = hot ? CardKit.Tint(Accent, 0.14) : CardKit.Brush("#0C0C0E");
        t.Tile.Background = hot ? CardKit.Brush(Accent) : CardKit.Raised;
        t.Icon.Foreground = hot ? CardKit.Brush("#000000") : CardKit.Text;
        t.Subtitle.Foreground = hot ? CardKit.Brush("#E2D4FF") : CardKit.Text2;
    }

    void SetHovered(DropAction? action)
    {
        if (action == _hovered) return;
        _hovered = action;
        foreach (var t in _targets) Paint(t, t.Action == action);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        if (!ReferenceEquals(e.Data, _data))
        {
            _data = e.Data;
            Describe(e.Data);
        }
        DropAction? over = null;
        foreach (var t in _targets)
        {
            var p = e.GetPosition(t.Root);
            if (p.X >= 0 && p.Y >= 0 && p.X <= t.Root.ActualWidth && p.Y <= t.Root.ActualHeight) over = t.Action;
        }
        SetHovered(over);
        // Not handled: the window's own drag handler still decides the cursor and the drop.
    }

    void Describe(IDataObject data)
    {
        _files = data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] f ? f : Array.Empty<string>();
        _chipText.Text = _files.Length switch
        {
            0 => "File",
            1 => Path.GetFileName(_files[0].TrimEnd('\\', '/')),
            _ => $"{_files.Length} items",
        };
        long size = DropFiles.Size(_files);
        _chipSize.Text = size > 0 ? "· " + DropPlan.Size(size) : "";
        _chipIcon.Text = ((char)(_files.Length == 1 && Directory.Exists(_files[0]) ? 0xE8B7 : _files.Length > 1 ? 0xE8F1 : 0xE8A5)).ToString();
        bool images = _files.Length > 0 && _files.All(DropPlan.IsImage);
        bool anyImage = _files.Any(DropPlan.IsImage);
        foreach (var t in _targets)
        {
            string text = t.Normal;
            if (t.Action == DropAction.Convert)
                text = images ? (_files.All(p => DropPlan.ConvertExtension(p) == ".png") ? "to PNG" : _files.All(p => DropPlan.ConvertExtension(p) == ".jpg") ? "to JPG" : "PNG ↔ JPG")
                    : anyImage ? "Images only" : "Images only for now";
            if (t.Action == DropAction.Phone && (_files.Length > 1 || _files.Any(Directory.Exists))) text = "As a .zip · QR";
            t.Subtitle.Text = text;
        }
    }
}
