using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuietIsland.Controls;
using QuietIsland.Models;
using QuietIsland.Services;

namespace QuietIsland;

internal static class InstallerPreview
{
    private const int TileWidth = 352;
    private const int TileHeight = 64;
    private const int PixelScale = 2;
    private const int AtlasSide = 8;
    private const int MusicFrames = 31;
    private const double FrameSeconds = .04;
    private static readonly string[] AccentColors = ["#FF8B81", "#FFC56C", "#B4A4FF", "#80CDF5", "#7CDEBA"];

    internal static void Write(string directory)
    {
        var scratch = Directory.CreateTempSubdirectory("quiet-island-setup-preview-");
        try
        {
            using var context = new IslandContext(scratch.FullName);
            context.Settings.UseAlbumAccent = false;
            var music = new MediaSnapshot("Тихая орбита", "Пример трека", "", true, "preview", 40, 210,
                Diagnostics.Render(Icons.Tile("music", 80), 80, 80, PixelScale));
            var audio = Enumerable.Range(0, MusicFrames).Select(SampleAudio).ToArray();

            foreach (var accent in AccentColors)
            {
                context.Settings.AccentColor = accent;
                var atlas = RenderAtlas(context, music, audio);
                CaptureService.SavePng(atlas, Path.Combine(Path.GetFullPath(directory), $"QuietIslandPreview-{accent[1..]}.png"));
            }
        }
        finally { scratch.Delete(recursive: true); }
    }

    private static BitmapSource RenderAtlas(IslandContext context, MediaSnapshot music, AudioFrame[] audio)
    {
        var atlas = new WriteableBitmap(
            AtlasSide * TileWidth * PixelScale, AtlasSide * TileHeight * PixelScale,
            96 * PixelScale, 96 * PixelScale, PixelFormats.Pbgra32, null);
        var pixels = new byte[TileWidth * TileHeight * PixelScale * PixelScale * 4];
        var currentMedia = typeof(MediaService).GetProperty(nameof(MediaService.Current))!;
        var themes = new[] { "Dark", "Light" };
        for (var theme = 0; theme < themes.Length; theme++)
        {
            context.Settings.Theme = themes[theme];
            currentMedia.SetValue(context.Media, MediaSnapshot.Empty);
            var window = new MainWindow(context, true);
            try
            {
                // Each theme has an idle tile followed by one seamless loop of the real surface.
                var firstTile = theme * (MusicFrames + 1);
                DrawNotch(atlas, pixels, window, firstTile);
                currentMedia.SetValue(context.Media, music);
                window.RefreshCompactForChecks();
                var surface = (LiquidSurface)window.FindName("Fluid");
                surface.Update(context.Media);
                for (var frame = 0; frame < MusicFrames * 2; frame++)
                    surface.AdvanceAudioFrame(audio[frame % MusicFrames], FrameSeconds);
                for (var frame = 0; frame < MusicFrames; frame++)
                {
                    surface.AdvanceAudioFrame(audio[frame], FrameSeconds);
                    DrawNotch(atlas, pixels, window, firstTile + frame + 1);
                }
            }
            finally { window.Close(); }
        }
        return atlas;
    }

    private static AudioFrame SampleAudio(int frame)
    {
        var phase = Math.Tau * frame / MusicFrames;
        var spectrum = new float[32];
        for (var i = 0; i < spectrum.Length; i++)
        {
            var band = i / (double)(spectrum.Length - 1);
            var bass = .5 + .5 * Math.Sin(phase - band * 7);
            var beat = .5 + .5 * Math.Sin(2 * phase + band * 8);
            spectrum[i] = (float)(.025 + .6 * bass * bass + .35 * beat * beat * beat);
        }
        return new(spectrum, spectrum.Max());
    }

    private static void DrawNotch(WriteableBitmap atlas, byte[] pixels, MainWindow window, int tile)
    {
        if (window.NotchWidth > TileWidth || window.NotchHeight > TileHeight)
            throw new InvalidOperationException("The compact notch exceeds the installer preview tile.");

        const int rootWidth = 744;
        const int width = TileWidth * PixelScale;
        const int height = TileHeight * PixelScale;
        ((TextBlock)window.FindName("ClockText")).Text = "12:48";
        var source = Diagnostics.Render(window.RenderRoot, rootWidth, TileHeight, PixelScale);
        source.CopyPixels(new Int32Rect((rootWidth - TileWidth) * PixelScale / 2, 0, width, height),
            pixels, width * 4, 0);
        var destination = new Int32Rect(tile % AtlasSide * width, tile / AtlasSide * height, width, height);
        atlas.WritePixels(destination, pixels, width * 4, 0);
    }
}
