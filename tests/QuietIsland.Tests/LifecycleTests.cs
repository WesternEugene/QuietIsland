using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;
public sealed class LifecycleTests
{
    [Fact]
    public void LoopbackSilenceKeepsSoundAtItsOriginalTime()
    {
        using var output = new MemoryStream(); var timeline = new PcmTimeline(output, 10, 2);
        timeline.Append(new byte[] { 1, 2, 3, 4 }, TimeSpan.FromSeconds(20.2));
        timeline.FillTo(TimeSpan.FromSeconds(30));
        var bytes = output.ToArray();
        Assert.Equal(600, bytes.Length);
        Assert.All(bytes.Take(400), b => Assert.Equal((byte)0, b));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, bytes[400..404]);
        Assert.All(bytes.Skip(404), b => Assert.Equal((byte)0, b));
    }
    [Fact]
    public void CompletelySilentCaptureStillHasFullAudioDuration()
    {
        using var output = new MemoryStream(); var timeline = new PcmTimeline(output, 48000, 8);
        timeline.FillTo(TimeSpan.FromSeconds(2));
        Assert.Equal(768000, output.Length);
    }
    [Fact]
    public void DictationCannotRestartDuringTranscription()
    {
        var gate = new SpeechSessionGate(); gate.BeginListening(); Assert.True(gate.BeginTranscription());
        Assert.Throws<InvalidOperationException>(gate.BeginListening);
        Assert.False(gate.BeginTranscription());
        gate.Finish(); gate.BeginListening(); Assert.Equal(SpeechStage.Listening, gate.Stage);
    }
    [Theory]
    [InlineData(0, 0, 1920, 1080, 1.5, 1.5, 1, 200)]
    [InlineData(-1920, -1080, 1920, 1080, 1.5, 1.5, 2, 200)]
    [InlineData(1920, 0, 1280, 720, 1, 1, 2, 150)]
    [InlineData(0, 0, 800, 600, 1.25, 1.25, 2, 200)]
    public void PanelStaysWithinSelectedMonitor(int left, int top, int width, int height, double dpiX, double dpiY, double scale, double offset)
    {
        var p = OverlayLayout.Calculate(left, top, width, height, dpiX, dpiY, scale, offset, true);
        Assert.True(p.X >= left && p.Y >= top);
        Assert.True(p.X + p.Width <= left + width);
        Assert.True(p.Y + p.Height <= top + height);
    }
    [Fact]
    public void NotchStartsAtMonitorTopAndStaysCenteredWhileMorphing()
    {
        foreach (var size in new[] { (280d, 52d), (410d, 160d), (744d, 376d) })
        {
            var p = OverlayLayout.CalculateSize(-1920, -1080, 1920, 1080, 1.5, 1.5, 1, 0, size.Item1, size.Item2);
            Assert.Equal(-1080, p.Y); Assert.InRange(Math.Abs(p.X + p.Width / 2d + 960), 0, .5);
        }
    }
}
