using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shammy.Playback;

/// <summary>Picks a tint from album art for the visualizer bars, like Apple's Now Playing does.</summary>
internal static class ArtPalette
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageSource, object> Fields = new();

    /// <summary>
    /// The Focus Wall music field for a cover (two glows and a deep base in its own colours, see
    /// <see cref="Shammy.Core.Music.ArtField"/>); the mockup's plum and rust when there's no cover. Worked out once per cover.
    /// </summary>
    public static Shammy.Core.Music.ArtFieldColors Field(ImageSource? art)
    {
        if (art is not BitmapSource source) return Shammy.Core.Music.ArtFieldColors.Default;
        if (Fields.TryGetValue(art, out var cached)) return (Shammy.Core.Music.ArtFieldColors)cached;
        var field = Shammy.Core.Music.ArtFieldColors.Default;
        try
        {
            const int size = 16;
            var small = new TransformedBitmap(new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0),
                new ScaleTransform((double)size / source.PixelWidth, (double)size / source.PixelHeight));
            var px = new byte[small.PixelWidth * small.PixelHeight * 4];
            small.CopyPixels(px, small.PixelWidth * 4, 0);
            field = Shammy.Core.Music.ArtField.Pick(px);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // An odd image format: keep the default field.
        }
        Fields.AddOrUpdate(art, field);
        return field;
    }

    public static Color Vibrant(BitmapSource source)
    {
        const int size = 16;
        var small = new TransformedBitmap(new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0),
            new ScaleTransform((double)size / source.PixelWidth, (double)size / source.PixelHeight));
        int w = small.PixelWidth, h = small.PixelHeight;
        var px = new byte[w * h * 4];
        small.CopyPixels(px, w * 4, 0);

        // Weight each pixel by saturation so a colourful accent beats a large grey background.
        double r = 0, g = 0, b = 0, total = 0;
        for (int i = 0; i < px.Length; i += 4)
        {
            double pb = px[i] / 255.0, pg = px[i + 1] / 255.0, pr = px[i + 2] / 255.0;
            double max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
            double sat = max == 0 ? 0 : (max - min) / max;
            double weight = 0.05 + sat * sat * max;
            r += pr * weight;
            g += pg * weight;
            b += pb * weight;
            total += weight;
        }
        if (total <= 0) return Colors.White;
        r /= total;
        g /= total;
        b /= total;

        // Lift dark picks so the bars stay visible on the black notch.
        double peak = Math.Max(r, Math.Max(g, b));
        if (peak < 0.75)
        {
            double lift = peak <= 0.01 ? 1 : 0.75 / peak;
            r = Math.Min(1, r * lift + 0.1);
            g = Math.Min(1, g * lift + 0.1);
            b = Math.Min(1, b * lift + 0.1);
        }
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
