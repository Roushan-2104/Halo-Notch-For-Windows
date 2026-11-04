using System.Windows;
using System.Windows.Media;
using Shammy.Core.Ambient;
using Shammy.Core.Music;

namespace Shammy.Views.Cards;

/// <summary>
/// The artwork fields behind tiles and detail art panels — the only place Focus Wall has colour: the album's own
/// colours, the sky, the date's deep teal, the timer's dark dial, the system's graphite. Each is a base gradient
/// with soft glows laid out on the 168×140 tile (Wall.dc.html's FIELD values) and stretched to any size, so the
/// detail's 300×248 art panel is the same picture grown. Built once per state and frozen.
/// </summary>
internal static class Fields
{
    const double TileW = 168, TileH = 140;

    readonly record struct Glow(double X, double Y, double Rx, double Ry, Color Color);

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    static Color C(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>A linear gradient at a CSS angle (0° = to top, 90° = to right, 180° = to bottom).</summary>
    static LinearGradientBrush Linear(double angle, params (Color Color, double At)[] stops)
    {
        double rad = angle * Math.PI / 180;
        double dx = Math.Sin(rad) / 2, dy = -Math.Cos(rad) / 2;
        var b = new LinearGradientBrush { StartPoint = new Point(0.5 - dx, 0.5 - dy), EndPoint = new Point(0.5 + dx, 0.5 + dy) };
        foreach (var (color, at) in stops) b.GradientStops.Add(new GradientStop(color, at));
        return b;
    }

    static Brush Layered(Brush @base, params Glow[] glows)
    {
        var rect = new RectangleGeometry(new Rect(0, 0, TileW, TileH));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(@base, null, rect));
        foreach (var g in glows)
        {
            var radial = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = new Point(g.X, g.Y),
                GradientOrigin = new Point(g.X, g.Y),
                RadiusX = g.Rx,
                RadiusY = g.Ry,
            };
            // Fade to the same colour at zero alpha (WPF blends unpremultiplied; fading to "transparent" greys it).
            radial.GradientStops.Add(new GradientStop(g.Color, 0));
            radial.GradientStops.Add(new GradientStop(Color.FromArgb(0, g.Color.R, g.Color.G, g.Color.B), 1));
            group.Children.Add(new GeometryDrawing(radial, null, rect));
        }
        var brush = new DrawingBrush(group)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, TileW, TileH),
        };
        brush.Freeze();
        return brush;
    }

    static readonly Dictionary<string, Brush> Cache = new();

    static Brush Cached(string key, Func<Brush> make)
    {
        if (!Cache.TryGetValue(key, out var b)) Cache[key] = b = make();
        return b;
    }

    /// <summary>The album's colours: a bright spot top-left, the second hue top-right, a deep base.</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageSource, Brush> Covers = new();

    /// <summary>
    /// The album cover itself as a tile's or detail's artwork, cropped to fill, with a soft shade at the foot so white
    /// words stay readable on any picture. Baked once per cover: a live image plus gradient would be redrawn with
    /// every repaint of this software-rendered window.
    /// </summary>
    public static Brush Cover(ImageSource art)
    {
        if (Covers.TryGetValue(art, out var cached)) return cached;
        const int w = 600, h = 496;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, w, h);
            dc.DrawRectangle(new ImageBrush(art) { Stretch = Stretch.UniformToFill }, null, rect);
            dc.DrawRectangle(Linear(180, (Color.FromArgb(0x10, 11, 11, 13), 0), (Color.FromArgb(0x10, 11, 11, 13), 0.35), (Color.FromArgb(0xC8, 11, 11, 13), 1)), null, rect);
        }
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        Covers.AddOrUpdate(art, brush);
        return brush;
    }

    public static Brush Music(ArtFieldColors c) => Cached("music" + c.GetHashCode(), () => Layered(
        Linear(160, (C(c.Base1), 0), (C(c.Base2), 1)),
        new Glow(40, 30, 90, 70, C(c.Glow1)),
        new Glow(160, 40, 110, 80, C(c.Glow2))));

    /// <summary>The sky now: its colour by condition, darker and bluer at night.</summary>
    public static Brush Sky(WeatherKind kind, bool day) => Cached($"sky{kind}{day}", () =>
    {
        var (top, bottom, glow1, glow2) = (kind, day) switch
        {
            (WeatherKind.Clear, true) => ("#2D5A8A", "#16314F", "#C98B45", "#4F7DB0"),
            (WeatherKind.Clear, false) => ("#1A2140", "#0C1020", "#3A4676", "#232C4E"),
            (WeatherKind.PartlyCloudy, true) => ("#2F4C6E", "#18263A", "#61779A", "#4B6283"),
            (WeatherKind.PartlyCloudy, false) => ("#1C2238", "#0E121E", "#3A4466", "#2A3150"),
            (WeatherKind.Fog, _) => (day ? "#3A3F48" : "#23262C", day ? "#22252B" : "#121418", day ? "#5D636E" : "#3A3E46", day ? "#4E535C" : "#30343B"),
            (WeatherKind.Drizzle or WeatherKind.Rain, _) => (day ? "#26384A" : "#18222E", day ? "#121A23" : "#0B1016", day ? "#405A74" : "#2C3D50", day ? "#33485E" : "#223040"),
            (WeatherKind.Snow, _) => (day ? "#3E4C5E" : "#232B38", day ? "#1F2733" : "#11161E", day ? "#6E7E94" : "#45505F", day ? "#56657A" : "#36404E"),
            (WeatherKind.Thunderstorm, _) => ("#2A2440", "#120F1F", "#4C4170", "#3A3258"),
            (_, true) => ("#2E3A50", "#1C222D", "#4E5B73", "#45506A"),
            (_, false) => ("#1D2433", "#10141C", "#353F55", "#2C3447"),
        };
        return Layered(Linear(180, (C(top), 0), (C(bottom), 1)),
            new Glow(120, 30, 70, 34, C(glow1)),
            new Glow(40, 56, 80, 34, C(glow2)));
    });

    /// <summary>The calendar's date field: deep teal.</summary>
    public static Brush Calendar(bool detail = false) => Cached("cal" + detail, () =>
        Layered(Linear(150, (C(detail ? "#1A4245" : "#17383A"), 0), (C("#0F201F"), 1))));

    /// <summary>The timer's dark dial: a soft radial behind the ring.</summary>
    public static Brush Timer(bool detail = false) => Cached("timer" + detail, () =>
    {
        var dial = new RadialGradientBrush
        {
            Center = new Point(detail ? 0.5 : 0.3, detail ? 0.5 : 0.46),
            GradientOrigin = new Point(detail ? 0.5 : 0.3, detail ? 0.5 : 0.46),
            RadiusX = 0.72,
            RadiusY = 0.86,
        };
        dial.GradientStops.Add(new GradientStop(C("#2B2E36"), 0));
        dial.GradientStops.Add(new GradientStop(C(detail ? "#141519" : "#17181C"), 1));
        return Layered(dial);
    });

    /// <summary>A finished timer or a ringing alarm: the same dial, warmed with the meaning red.</summary>
    public static Brush Alarm() => Cached("alarm", () =>
        Layered(Linear(160, (C("#3A1A1C"), 0), (C("#160C0E"), 1)), new Glow(50, 50, 110, 90, C("#6A2224"))));

    /// <summary>The system's graphite, under the meters.</summary>
    public static Brush System() => Cached("system", () => Layered(Linear(160, (C("#25252E"), 0), (C("#141419"), 1))));

    /// <summary>Lists (clipboard, notes, shelf, alerts, snippets, settings): a quiet violet graphite.</summary>
    public static Brush List() => Cached("list", () => Layered(Linear(160, (C("#24222C"), 0), (C("#141419"), 1))));

    /// <summary>The mirror before the camera is on: warm dark glass.</summary>
    public static Brush Mirror() => Cached("mirror", () =>
        Layered(Linear(150, (C("#2B2622"), 0), (C("#16141A"), 0.55), (C("#0B0B0E"), 1))));
}
