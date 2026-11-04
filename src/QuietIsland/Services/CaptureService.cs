using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using ZXing;
using ZXing.Common;

namespace QuietIsland.Services;

public sealed record CaptureResult(BitmapSource Image, Rectangle Bounds);
public sealed class CaptureService(DesktopState state)
{
    public async Task<CaptureResult?> SelectAsync(bool color = false)
    {
        await Task.Delay(160);
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        var image = Screen(bounds);
        var window = new Windows.SelectionWindow(image, bounds, color);
        return window.ShowDialog() == true ? window.Result : null;
    }
    public static BitmapSource Screen(Rectangle bounds)
    {
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
        return FromBitmap(bitmap);
    }
    public static BitmapSource FromBitmap(Bitmap bitmap)
    {
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    public static void SavePng(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file);
    }
    public string NewPath(string extension) => Path.Combine(state.Data.Settings.CaptureFolder, "Island " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff") + extension);
    public async Task<string> OcrAsync(BitmapSource image)
    {
        if (image.PixelWidth > OcrEngine.MaxImageDimension || image.PixelHeight > OcrEngine.MaxImageDimension)
        {
            var scale = OcrEngine.MaxImageDimension / (double)Math.Max(image.PixelWidth, image.PixelHeight);
            image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var bytes = new MemoryStream(); encoder.Save(bytes); bytes.Position = 0;
        using var random = bytes.AsRandomAccessStream();
        var decoder = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(random);
        using var software = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var engines = OcrEngine.AvailableRecognizerLanguages.Where(x => x.LanguageTag.StartsWith("ru") || x.LanguageTag.StartsWith("en")).Select(OcrEngine.TryCreateFromLanguage).Where(x => x != null).ToList();
        if (engines.Count == 0) { var fallback = OcrEngine.TryCreateFromUserProfileLanguages(); if (fallback != null) engines.Add(fallback); }
        if (engines.Count == 0) throw new InvalidOperationException("В Windows нет языков OCR. Добавьте русский или английский языковой пакет.");
        var lines = new List<(double Y, string Text)>();
        foreach (var engine in engines)
        {
            var result = await engine!.RecognizeAsync(software);
            foreach (var line in result.Lines)
            {
                var y = line.Words.Count > 0 ? line.Words[0].BoundingRect.Y : 0;
                var duplicate = lines.FindIndex(x => Math.Abs(x.Y - y) < 10);
                if (duplicate < 0) lines.Add((y, line.Text));
                else if (Score(line.Text) > Score(lines[duplicate].Text)) lines[duplicate] = (y, line.Text);
            }
        }
        return string.Join(Environment.NewLine, lines.OrderBy(x => x.Y).Select(x => x.Text));
    }
    private static int Score(string text) => text.Count(char.IsLetterOrDigit) + text.Count(c => c is >= 'А' and <= 'я') * 2;
    public static string? DecodeQr(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        var source = new RGBLuminanceSource(pixels, converted.PixelWidth, converted.PixelHeight, RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = new BarcodeReaderGeneric { Options = new DecodingOptions { TryHarder = true, PossibleFormats = [BarcodeFormat.QR_CODE] } };
        return reader.Decode(source)?.Text;
    }
    public static BitmapSource CreateQr(string text)
    {
        var writer = new BarcodeWriterPixelData { Format = BarcodeFormat.QR_CODE, Options = new EncodingOptions { Width = 360, Height = 360, Margin = 2 } };
        var data = writer.Write(text); var bitmap = BitmapSource.Create(data.Width, data.Height, 96, 96, PixelFormats.Bgra32, null, data.Pixels, data.Width * 4); bitmap.Freeze(); return bitmap;
    }
}
