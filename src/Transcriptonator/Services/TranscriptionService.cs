using System.Diagnostics;
using System.Text;
using Transcriptonator.Helpers;
using Whisper.net;

namespace Transcriptonator.Services;

public class TranscriptionService : ITranscriptionService
{
    private readonly IModelManagerService _modelManager;
    private readonly IConfigService _configService;

    public TranscriptionService(IModelManagerService modelManager, IConfigService configService)
    {
        _modelManager = modelManager;
        _configService = configService;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        string outputDirectory,
        string whisperModelSize,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        string? wavPath = null;

        try
        {
            progress?.Report(0.05);

            // Convert audio to 16kHz mono WAV for Whisper
            wavPath = await AudioConverter.ConvertToWavAsync(audioPath, ct);
            progress?.Report(0.15);

            // Load Whisper model and process
            var modelPath = _modelManager.GetWhisperModelPath(whisperModelSize);
            var config = _configService.Load();

            using var whisperFactory = WhisperFactory.FromPath(modelPath);
            using var processor = whisperFactory.CreateBuilder()
                .WithLanguage("auto")
                .WithThreads(config.ThreadCount)
                .Build();

            progress?.Report(0.20);

            var sb = new StringBuilder();
            var segments = new List<SegmentData>();

            using var wavStream = File.OpenRead(wavPath);
            await foreach (var segment in processor.ProcessAsync(wavStream, ct))
            {
                segments.Add(segment);
                sb.AppendLine($"[{segment.Start:hh\\:mm\\:ss} --> {segment.End:hh\\:mm\\:ss}] {segment.Text.Trim()}");
            }

            progress?.Report(0.90);

            // Write output file
            Directory.CreateDirectory(outputDirectory);
            var outputFileName = Path.GetFileNameWithoutExtension(audioPath) + ".txt";
            var outputPath = Path.Combine(outputDirectory, outputFileName);

            // Handle duplicate filenames
            var counter = 1;
            while (File.Exists(outputPath))
            {
                outputFileName = $"{Path.GetFileNameWithoutExtension(audioPath)}_{counter++}.txt";
                outputPath = Path.Combine(outputDirectory, outputFileName);
            }

            await File.WriteAllTextAsync(outputPath, sb.ToString(), ct);
            progress?.Report(1.0);

            sw.Stop();
            return new TranscriptionResult(sb.ToString(), outputPath, sw.Elapsed.TotalSeconds);
        }
        finally
        {
            // Clean up temp WAV file
            if (wavPath != null && File.Exists(wavPath))
            {
                try { File.Delete(wavPath); } catch { }
            }
        }
    }
}
