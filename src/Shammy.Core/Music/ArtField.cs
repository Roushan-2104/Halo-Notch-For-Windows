namespace Shammy.Core.Music;

/// <summary>An sRGB colour as bytes.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// The music tile's artwork field (Focus Wall): two glows and a deep base taken from the album cover's own
/// colours, toned down so white type stays readable on them. Two different hues when the cover has them
/// (rust and plum, say); one hue in two depths when it doesn't; neutral greys for a black-and-white cover.
/// </summary>
/// <param name="Glow1">The brighter spot, top-left.</param>
/// <param name="Glow2">The second spot, top-right.</param>
/// <param name="Base1">The field's base where it starts (top).</param>
/// <param name="Base2">The field's base where it ends (bottom), darkest.</param>
public readonly record struct ArtFieldColors(Rgb Glow1, Rgb Glow2, Rgb Base1, Rgb Base2)
{
    /// <summary>What a cover with no colour (or no cover) gets: the mockup's warm plum and rust.</summary>
    public static readonly ArtFieldColors Default = new(new(0x9A, 0x3A, 0x2C), new(0x5D, 0x1E, 0x58), new(0x3A, 0x16, 0x36), new(0x1A, 0x10, 0x30));
}

public static class ArtField
{
    const int Buckets = 12;

    /// <summary>
    /// Picks the field from a small BGRA thumbnail (16×16 is plenty): pixels are binned by hue, weighted by how
    /// colourful and bright they are, so a vivid accent wins over a large grey background.
    /// </summary>
    public static ArtFieldColors Pick(byte[] bgra)
    {
        var weight = new double[Buckets];
        var r = new double[Buckets];
        var g = new double[Buckets];
        var b = new double[Buckets];
        double colourful = 0, total = 0, grey = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            double pb = bgra[i] / 255.0, pg = bgra[i + 1] / 255.0, pr = bgra[i + 2] / 255.0;
            var (h, s, v) = Hsv(pr, pg, pb);
            total++;
            grey += v;
            if (s < 0.18 || v < 0.12) continue;
            double w = s * s * (0.25 + v);
            int k = (int)(h / 360 * Buckets) % Buckets;
            weight[k] += w;
            r[k] += pr * w;
            g[k] += pg * w;
            b[k] += pb * w;
            colourful += w;
        }
        if (total == 0) return ArtFieldColors.Default;

        // Too little colour to read as a hue: a neutral field at the cover's brightness.
        if (colourful / total < 0.02)
        {
            double lum = Math.Clamp(grey / total, 0.15, 0.6);
            return new(Grey(0.30 + lum * 0.2), Grey(0.22 + lum * 0.15), Grey(0.16), Grey(0.08));
        }

        int first = 0;
        for (int k = 1; k < Buckets; k++)
            if (weight[k] > weight[first]) first = k;
        // A second hue at least 60° away that carries real weight; otherwise the same hue, deeper.
        int second = -1;
        for (int k = 0; k < Buckets; k++)
        {
            int apart = Math.Min(Math.Abs(k - first), Buckets - Math.Abs(k - first));
            if (apart < 2 || weight[k] < weight[first] * 0.12) continue;
            if (second < 0 || weight[k] > weight[second]) second = k;
        }

        var c1 = Average(first);
        var c2 = second >= 0 ? Average(second) : c1;
        var (h1, s1, _) = Hsv(c1.R, c1.G, c1.B);
        var (h2, s2, _) = Hsv(c2.R, c2.G, c2.B);
        if (second < 0) h2 = (h1 + 330) % 360; // a neighbouring hue, so the field still has depth

        return new(
            FromHsv(h1, Math.Clamp(s1, 0.45, 0.72), 0.60),
            FromHsv(h2, Math.Clamp(s2, 0.45, 0.70), 0.38),
            FromHsv(h2, Math.Clamp(s2, 0.40, 0.65), 0.23),
            FromHsv((h2 + 350) % 360, 0.55, 0.12));

        (double R, double G, double B) Average(int k) => (r[k] / weight[k], g[k] / weight[k], b[k] / weight[k]);
    }

    static Rgb Grey(double v)
    {
        byte x = (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        // A hair of blue keeps greys from reading as brown on the near-black ground.
        return new(x, x, (byte)Math.Min(255, x + 6));
    }

    static (double H, double S, double V) Hsv(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    /// <summary>HSV (hue in degrees, s and v 0–1) to sRGB bytes.</summary>
    public static Rgb FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static byte B(double f) => (byte)Math.Round(Math.Clamp(f, 0, 1) * 255);
        return new(B(r + m), B(g + m), B(b + m));
    }
}
