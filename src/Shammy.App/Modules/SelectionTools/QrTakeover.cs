using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shammy.Core.Conversions;
using ZXing;
using ZXing.Common;

namespace Shammy.Modules.SelectionTools;

/// <summary>
/// Circle a QR code or barcode: links open in the browser, Wi-Fi codes copy the password, anything else
/// is copied. Decoding gets a fixed time budget so an ordinary circle never feels slower.
/// </summary>
internal sealed class QrTakeover : ISelectionHandler
{
    static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(150);

    readonly ModuleContext _ctx;

    public QrTakeover(ModuleContext ctx) => _ctx = ctx;

    /// <summary>The last decode for the "selection-state" pipe command.</summary>
    public string Last { get; private set; } = "-";

    public async Task<CommandResult?> TryTakeOverAsync(SelectionInfo selection)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var pixels = QrReader.ReadPixels(selection);
            if (pixels is null) return null;
            var decode = Task.Run(() => QrReader.Decode(pixels));
            var left = Budget - sw.Elapsed;
            if (left <= TimeSpan.Zero || await Task.WhenAny(decode, Task.Delay(left)) != decode)
            {
                Record(null, sw.ElapsedMilliseconds, $"timeout {pixels.Width}x{pixels.Height}");
                return null;
            }
            var code = await decode;
            Record(code, sw.ElapsedMilliseconds, $"{pixels.Width}x{pixels.Height}");
            return code is null ? null : Act(code.Value.Text, code.Value.IsQr);
        }
        catch (Exception ex)
        {
            _ctx.Warn("QR check failed", ex);
            return null;
        }
    }

    public void Record((string Text, bool IsQr)? code, long ms, string note)
    {
        if (code is null)
        {
            Last = $"none in {ms} ms ({note})";
        }
        else
        {
            // QR codes carry Wi-Fi passwords, 2FA secrets (otpauth:), payment strings and token links,
            // so the log only records what kind it was, never its content.
            var payload = QrPayload.Parse(code.Value.Text);
            Last = $"{payload.Kind} in {ms} ms ({note}), {payload.Text.Length} chars";
        }
        _ctx.Info($"QR check: {Last}");
    }

    CommandResult Act(string text, bool isQr)
    {
        var payload = QrPayload.Parse(text);
        switch (payload.Kind)
        {
            case QrKind.Url:
                _ctx.Open(payload.Text);
                return new CommandResult(Glyphs.Link, "Opened link", payload.Host);
            case QrKind.Wifi when payload.Password is null:
                return new CommandResult(Glyphs.Wifi, payload.Ssid!, "Open network, no password");
            case QrKind.Wifi:
                return ClipboardText.TrySet(payload.Password, secret: true)
                    ? new CommandResult(Glyphs.Wifi, "Wi-Fi password copied", payload.Ssid)
                    : ClipboardText.Busy;
            default:
                // QR text is often a secret (otpauth: 2FA keys, payment strings), so keep it out of history too.
                return ClipboardText.TrySet(payload.Text, secret: true)
                    ? new CommandResult(Glyphs.Qr, isQr ? "Copied from QR" : "Copied from barcode", Preview(payload.Text))
                    : ClipboardText.Busy;
        }
    }

    internal static string Preview(string text)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length > 60 ? flat[..60] + "\u2026" : flat;
    }
}

/// <summary>ZXing over raw BGRA pixels, shared by the circle handler and the test pipe.</summary>
internal static class QrReader
{
    // Big circles are scaled down; a code that fills less than this is unreadable anyway.
    const int MaxSide = 1000;

    static readonly IList<BarcodeFormat> Formats = new List<BarcodeFormat>
    {
        BarcodeFormat.QR_CODE, BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A, BarcodeFormat.UPC_E,
        BarcodeFormat.CODE_128, BarcodeFormat.CODE_39, BarcodeFormat.CODE_93, BarcodeFormat.ITF, BarcodeFormat.CODABAR,
    };

    internal sealed record Pixels(byte[] Bgra, int Width, int Height);

    public static Pixels? ReadPixels(SelectionInfo selection)
    {
        if (selection.Image is { } image) return ReadPixels(image);
        if (selection.Png is not { Length: > 0 } png) return null;
        using var ms = new MemoryStream(png);
        return ReadPixels(BitmapFrame.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad));
    }

    /// <summary>Copies (and if needed shrinks) the image as BGRA. Must run on the thread that owns an unfrozen bitmap.</summary>
    public static Pixels? ReadPixels(BitmapSource source)
    {
        if (source.PixelWidth < 16 || source.PixelHeight < 16) return null;
        BitmapSource bmp = source;
        double scale = Math.Min(1.0, MaxSide / (double)Math.Max(bmp.PixelWidth, bmp.PixelHeight));
        if (scale < 1) bmp = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
        if (bmp.Format != PixelFormats.Bgra32) bmp = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        var bytes = new byte[w * h * 4];
        bmp.CopyPixels(bytes, w * 4, 0);
        return new Pixels(bytes, w, h);
    }

    /// <summary>The code's text and whether it was a QR (vs a 1D barcode), or null. Never throws.</summary>
    public static (string Text, bool IsQr)? Decode(Pixels pixels)
    {
        try
        {
            var source = new RGBLuminanceSource(pixels.Bgra, pixels.Width, pixels.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
            // A quick pass (with light-on-dark codes) finds a cleanly circled code in a few ms; TryHarder
            // costs 50-100 ms on a busy region with nothing in it, so it only runs when the quick pass misses.
            var result = Read(source, tryHarder: false, tryInverted: true) ?? Read(source, tryHarder: true, tryInverted: false);
            if (result is null || string.IsNullOrEmpty(result.Text)) return null;
            return (result.Text, result.BarcodeFormat == BarcodeFormat.QR_CODE);
        }
        catch (Exception ex)
        {
            Infrastructure.Log.Warn("QR decode failed", ex);
            return null;
        }
    }

    static Result? Read(LuminanceSource source, bool tryHarder, bool tryInverted) =>
        new BarcodeReaderGeneric
        {
            AutoRotate = false,
            Options = new DecodingOptions { TryHarder = tryHarder, TryInverted = tryInverted, PossibleFormats = Formats },
        }.Decode(source);

    /// <summary>Loads and JITs ZXing off the UI thread so the first real circle fits in the time budget.</summary>
    public static void WarmUp()
    {
        try
        {
            var code = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions { Width = 120, Height = 120, Margin = 2 },
            }.Write("shammy");
            Decode(new Pixels(code.Pixels, code.Width, code.Height));
            var blank = new byte[64 * 64 * 4];
            Array.Fill(blank, (byte)255);
            Decode(new Pixels(blank, 64, 64));
        }
        catch (Exception ex)
        {
            Infrastructure.Log.Warn("QR warm-up failed", ex);
        }
    }
}
