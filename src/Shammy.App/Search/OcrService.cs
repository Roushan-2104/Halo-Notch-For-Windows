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
