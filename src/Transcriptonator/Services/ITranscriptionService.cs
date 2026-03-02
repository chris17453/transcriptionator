namespace Transcriptonator.Services;

public record TranscriptionResult(string Text, string OutputPath, double DurationSeconds);

public interface ITranscriptionService
{
    Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        string outputDirectory,
        string whisperModelSize,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}
