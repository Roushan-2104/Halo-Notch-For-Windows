using System.Runtime.InteropServices.WindowsRuntime;
using Shammy.Core.Search;
using Shammy.Infrastructure;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using CoreOcrWord = Shammy.Core.Search.OcrWord;

namespace Shammy.Search;

/// <summary>Windows' built-in, offline OCR over the frozen screen.</summary>
internal static class OcrService
{
    static OcrEngine? _engine;
    static bool _tried;

    public static bool IsAvailable
    {
        get
        {
            if (!_tried)
            {
                _tried = true;
                _engine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (_engine is null) Log.Warn("No OCR language installed; circled text can't be read");
                else Log.Info($"OCR language: {_engine.RecognizerLanguage.LanguageTag}");
            }
            return _engine is not null;
        }
    }

    public static async Task<IReadOnlyList<CoreOcrWord>> RecognizeAsync(ScreenShot shot)
    {
        if (!IsAvailable) return Array.Empty<CoreOcrWord>();
        try
        {
            var bitmap = SoftwareBitmap.CreateCopyFromBuffer(shot.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8,
                shot.Width, shot.Height, BitmapAlphaMode.Premultiplied);

            double scale = 1;
            uint max = OcrEngine.MaxImageDimension;
            if (shot.Width > max || shot.Height > max)
            {
                scale = (double)max / Math.Max(shot.Width, shot.Height);
                bitmap = await Downscale(bitmap, scale);
            }

            var result = await _engine!.RecognizeAsync(bitmap);
            var words = new List<CoreOcrWord>();
            int line = 0;
            foreach (var l in result.Lines)
            {
                foreach (var w in l.Words)
                {
                    var r = w.BoundingRect;
                    words.Add(new CoreOcrWord(w.Text, new Box(r.X / scale, r.Y / scale, r.Width / scale, r.Height / scale), line));
                }
                line++;
            }
            return words;
        }
        catch (Exception ex)
        {
            Log.Warn("OCR failed", ex);
            return Array.Empty<CoreOcrWord>();
        }
    }

    /// <summary>The text in an image (a screenshot), line by line; empty when there's none or OCR is unavailable.</summary>
    public static async Task<string> ReadTextAsync(System.Windows.Media.Imaging.BitmapSource image)
    {
        if (!IsAvailable) return "";
        try
        {
            System.Windows.Media.Imaging.BitmapSource bgra = image.Format == System.Windows.Media.PixelFormats.Pbgra32
                ? image
                : new System.Windows.Media.Imaging.FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
            int width = bgra.PixelWidth, height = bgra.PixelHeight;
            var pixels = new byte[width * height * 4];
            bgra.CopyPixels(pixels, width * 4, 0);
            var bitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
            uint max = OcrEngine.MaxImageDimension;
            if (width > max || height > max) bitmap = await Downscale(bitmap, (double)max / Math.Max(width, height));
            var result = await _engine!.RecognizeAsync(bitmap);
            return string.Join("\n", result.Lines.Select(l => l.Text.Trim()).Where(l => l.Length > 0));
        }
        catch (Exception ex)
        {
            Log.Warn("OCR of an image failed", ex);
            return "";
        }
    }

    static async Task<SoftwareBitmap> Downscale(SoftwareBitmap source, double scale)
    {
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, stream);
        encoder.SetSoftwareBitmap(source);
        encoder.BitmapTransform.ScaledWidth = (uint)(source.PixelWidth * scale);
        encoder.BitmapTransform.ScaledHeight = (uint)(source.PixelHeight * scale);
        await encoder.FlushAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}
