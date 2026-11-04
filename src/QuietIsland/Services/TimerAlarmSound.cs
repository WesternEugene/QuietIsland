using System.Media;
using System.Text;

namespace QuietIsland.Services;

/// <summary>A self-contained repeating alarm, independent of the Windows notification sound.</summary>
internal sealed class TimerAlarmSound : IDisposable
{
    private MemoryStream? wave;
    private SoundPlayer? player;

    public void Start()
    {
        if (player != null) return;
        wave = CreateWave(); player = new SoundPlayer(wave);
        try { player.Load(); player.PlayLooping(); }
        catch { Stop(); throw; }
    }
    public void Stop()
    {
        player?.Stop(); player?.Dispose(); player = null;
        wave?.Dispose(); wave = null;
    }
    public void Dispose() => Stop();

    private static MemoryStream CreateWave()
    {
        const int rate = 22050, sampleCount = rate * 2;
        var stream = new MemoryStream(44 + sampleCount * 2);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + sampleCount * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(sampleCount * 2);
            for (var i = 0; i < sampleCount; i++)
            {
                var time = i / (double)rate; var beat = (int)(time / .3); var phase = time - beat * .3;
                var envelope = beat < 3 && phase < .18 ? Math.Min(1, phase / .012) * Math.Min(1, (.18 - phase) / .025) : 0;
                var frequency = beat == 1 ? 660 : 880;
                writer.Write((short)(Math.Sin(2 * Math.PI * frequency * time) * envelope * short.MaxValue * .24));
            }
        }
        stream.Position = 0; return stream;
    }
}
