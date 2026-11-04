namespace QuietIsland.Core;
public enum SpeechStage { Idle, Listening, Transcribing }
public sealed class SpeechSessionGate
{
    private int stage;
    public SpeechStage Stage => (SpeechStage)Volatile.Read(ref stage);
    public void BeginListening()
    {
        if (Interlocked.CompareExchange(ref stage, (int)SpeechStage.Listening, (int)SpeechStage.Idle) != (int)SpeechStage.Idle)
            throw new InvalidOperationException("Предыдущая диктовка ещё не завершена.");
    }
    public bool BeginTranscription() => Interlocked.CompareExchange(ref stage, (int)SpeechStage.Transcribing, (int)SpeechStage.Listening) == (int)SpeechStage.Listening;
    public void Finish() => Interlocked.Exchange(ref stage, (int)SpeechStage.Idle);
}
