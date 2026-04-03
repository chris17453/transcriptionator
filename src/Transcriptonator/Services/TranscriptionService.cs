using System.Diagnostics;
using System.Text;
using Transcriptonator.Helpers;
using Whisper.net;

namespace Transcriptonator.Services;

public class TranscriptionService : ITranscriptionService, IDisposable
{
    private readonly IModelManagerService _modelManager;
    private readonly IConfigService _configService;
    private readonly string _logPath;

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string? _loadedModelPath;
    private int _loadedThreadCount;

    public TranscriptionService(IModelManagerService modelManager, IConfigService configService)
    {
        _modelManager = modelManager;
        _configService = configService;
        _logPath = Path.Combine(configService.AppDataDirectory, "transcription.log");
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        Console.WriteLine(line);
        try { File.AppendAllText(_logPath, line + Environment.NewLine); } catch { }
    }

    private WhisperProcessor GetOrCreateProcessor(string modelPath, int threadCount)
    {
        if (_processor != null && _loadedModelPath == modelPath && _loadedThreadCount == threadCount)
        {
            Log("Reusing cached processor");
            return _processor;
        }

        // Must dispose processor before factory
        _processor?.Dispose();
        _factory?.Dispose();

        Log($"Loading WhisperFactory from {Path.GetFileName(modelPath)}...");
        _factory = WhisperFactory.FromPath(modelPath);
        _loadedModelPath = modelPath;

        var runtimeInfo = WhisperFactory.GetRuntimeInfo();
        Log($"Runtime info: {runtimeInfo}");

        Log("Building processor...");
        _processor = _factory.CreateBuilder()
            .WithLanguage("auto")
            .WithThreads(threadCount)
            .WithNoSpeechThreshold(0.6f)
            .Build();
        _loadedThreadCount = threadCount;
        Log($"Processor built (greedy, auto language, {threadCount} threads)");

        return _processor;
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

        Log($"=== Starting transcription: {Path.GetFileName(audioPath)} ===");
        Log($"Model size: {whisperModelSize}");

        try
        {
            progress?.Report(0.05);

            Log("Converting audio to WAV...");
            wavPath = await AudioConverter.ConvertToWavAsync(audioPath, ct);
            var wavSize = new FileInfo(wavPath).Length;
            Log($"WAV conversion complete: {wavPath} ({wavSize / 1024.0 / 1024.0:F1} MB)");
            progress?.Report(0.15);

            var modelPath = _modelManager.GetWhisperModelPath(whisperModelSize);
            Log($"Whisper model path: {modelPath}");
            if (!File.Exists(modelPath))
            {
                Log("ERROR: Model file not found!");
                throw new FileNotFoundException($"Whisper model not found at: {modelPath}");
            }

            var config = _configService.Load();
            var threadCount = Math.Min(config.ThreadCount, 8);
            Log($"Thread count: {threadCount} (config: {config.ThreadCount})");

            var processor = GetOrCreateProcessor(modelPath, threadCount);

            progress?.Report(0.20);

            var sb = new StringBuilder();
            var segments = new List<SegmentData>();

            Log("Starting Whisper processing...");
            using var wavStream = File.OpenRead(wavPath);
            await foreach (var segment in processor.ProcessAsync(wavStream, ct))
            {
                segments.Add(segment);
                sb.AppendLine($"[{segment.Start:hh\\:mm\\:ss} --> {segment.End:hh\\:mm\\:ss}] {segment.Text.Trim()}");
            }
            Log($"Processing complete: {segments.Count} segments extracted");

            progress?.Report(0.90);

            Directory.CreateDirectory(outputDirectory);
            var outputFileName = Path.GetFileNameWithoutExtension(audioPath) + ".txt";
            var outputPath = Path.Combine(outputDirectory, outputFileName);

            var counter = 1;
            while (File.Exists(outputPath))
            {
                outputFileName = $"{Path.GetFileNameWithoutExtension(audioPath)}_{counter++}.txt";
                outputPath = Path.Combine(outputDirectory, outputFileName);
            }

            await File.WriteAllTextAsync(outputPath, sb.ToString(), ct);
            progress?.Report(1.0);

            sw.Stop();
            Log($"Transcription saved to: {outputPath} ({sw.Elapsed.TotalSeconds:F1}s)");
            return new TranscriptionResult(sb.ToString(), outputPath, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.GetType().Name}: {ex.Message}");
            Log($"Stack trace: {ex.StackTrace}");
            throw;
        }
        finally
        {
            if (wavPath != null && File.Exists(wavPath))
            {
                try { File.Delete(wavPath); } catch { }
            }
        }
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _processor = null;
        _factory?.Dispose();
        _factory = null;
    }
}
