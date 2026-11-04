namespace QuietIsland.Core;

/// <summary>Keeps discontinuous loopback buffers on the wall-clock timeline, including complete silence.</summary>
public sealed class PcmTimeline
{
    private readonly Stream destination;
    private readonly int sampleRate, blockAlign;
    private readonly byte[] silence;
    private long framesWritten;
    public PcmTimeline(Stream destination, int sampleRate, int blockAlign)
    {
        if (sampleRate <= 0 || blockAlign is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        this.destination = destination; this.sampleRate = sampleRate; this.blockAlign = blockAlign;
        silence = new byte[Math.Max(blockAlign, 16384 / blockAlign * blockAlign)];
    }
    public void Append(ReadOnlySpan<byte> buffer, TimeSpan endTimestamp)
    {
        var count = buffer.Length / blockAlign * blockAlign; if (count == 0) return;
        var packetFrames = count / blockAlign;
        var startFrame = Math.Max(0, FrameAt(endTimestamp) - packetFrames);
        // Callback scheduling jitter is not a silent gap. Real gaps retain their wall-clock position.
        if (startFrame - framesWritten > sampleRate * .03) PadTo(startFrame);
        destination.Write(buffer[..count]); framesWritten += packetFrames;
    }
    public void FillTo(TimeSpan timestamp) => PadTo(FrameAt(timestamp));
    private long FrameAt(TimeSpan timestamp) => (long)Math.Max(0, Math.Round(timestamp.TotalSeconds * sampleRate));
    private void PadTo(long frames)
    {
        while (framesWritten < frames)
        {
            var n = (int)Math.Min(frames - framesWritten, silence.Length / blockAlign);
            destination.Write(silence, 0, n * blockAlign); framesWritten += n;
        }
    }
}
