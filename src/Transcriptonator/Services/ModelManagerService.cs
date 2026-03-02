using Whisper.net.Ggml;

namespace Transcriptonator.Services;

public class ModelManagerService : IModelManagerService
{
    private readonly IConfigService _config;
    private readonly HttpClient _httpClient;

    private const string OnnxModelUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx";
    private const string OnnxVocabUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt";
    private const string LlmModelUrl = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-gguf/resolve/main/Phi-3-mini-4k-instruct-q4.gguf";

    private const int MaxRetryAttempts = 3;

    public ModelManagerService(IConfigService config)
    {
        _config = config;
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        _httpClient = new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromHours(2);
    }

    public bool IsWhisperModelDownloaded(string modelSize)
        => File.Exists(GetWhisperModelPath(modelSize));

    public bool IsOnnxModelDownloaded()
        => File.Exists(GetOnnxModelPath()) && File.Exists(GetOnnxVocabPath());

    public bool IsLlmModelDownloaded()
        => File.Exists(GetLlmModelPath());

    public bool AreAllModelsReady(string whisperModelSize)
        => IsWhisperModelDownloaded(whisperModelSize) && IsOnnxModelDownloaded() && IsLlmModelDownloaded();

    public string GetWhisperModelPath(string modelSize)
        => Path.Combine(_config.WhisperModelsDirectory, $"ggml-{modelSize}.bin");

    public string GetOnnxModelPath()
        => Path.Combine(_config.OnnxModelsDirectory, "model.onnx");

    public string GetOnnxVocabPath()
        => Path.Combine(_config.OnnxModelsDirectory, "vocab.txt");

    public string GetLlmModelPath()
        => Path.Combine(_config.LlmModelsDirectory, "phi-3-mini-4k-instruct-q4.gguf");

    public async Task DownloadWhisperModelAsync(string modelSize, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var ggmlType = modelSize.ToLowerInvariant() switch
        {
            "tiny" => GgmlType.Tiny,
            "base" => GgmlType.Base,
            "small" => GgmlType.Small,
            "medium" => GgmlType.Medium,
            "large" => GgmlType.LargeV3,
            _ => GgmlType.Small
        };

        var modelPath = GetWhisperModelPath(modelSize);
        if (File.Exists(modelPath)) return;

        var estimatedSize = ggmlType switch
        {
            GgmlType.Tiny => 75_000_000L,
            GgmlType.Base => 142_000_000L,
            GgmlType.Small => 466_000_000L,
            GgmlType.Medium => 1_500_000_000L,
            _ => 466_000_000L
        };

        await WithRetryAsync(async () =>
        {
            var tempPath = modelPath + ".tmp";
            try
            {
                var downloader = new WhisperGgmlDownloader(_httpClient);
                using var modelStream = await downloader.GetGgmlModelAsync(ggmlType, cancellationToken: ct);
                using var fileStream = File.Create(tempPath);

                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await modelStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;
                    progress?.Report(Math.Min(1.0, (double)totalRead / estimatedSize));
                }

                fileStream.Close();

                if (new FileInfo(tempPath).Length == 0)
                    throw new IOException("Downloaded file is empty.");

                File.Move(tempPath, modelPath, overwrite: true);
                progress?.Report(1.0);
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }
        }, progress, ct);
    }

    public async Task DownloadOnnxModelAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (IsOnnxModelDownloaded()) return;

        await DownloadFileAsync(OnnxModelUrl, GetOnnxModelPath(), progress, ct, 0.9);
        await DownloadFileAsync(OnnxVocabUrl, GetOnnxVocabPath(), null, ct);
        progress?.Report(1.0);
    }

    public async Task DownloadLlmModelAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (IsLlmModelDownloaded()) return;
        await DownloadFileAsync(LlmModelUrl, GetLlmModelPath(), progress, ct);
    }

    private async Task DownloadFileAsync(string url, string outputPath, IProgress<double>? progress, CancellationToken ct, double progressScale = 1.0)
    {
        if (File.Exists(outputPath)) return;

        await WithRetryAsync(async () =>
        {
            var tempPath = outputPath + ".tmp";
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = File.Create(tempPath);

                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        progress?.Report((double)totalRead / totalBytes * progressScale);
                    }
                }

                fileStream.Close();

                if (new FileInfo(tempPath).Length == 0)
                    throw new IOException("Downloaded file is empty.");

                File.Move(tempPath, outputPath, overwrite: true);
                progress?.Report(progressScale);
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }
        }, progress, ct);
    }

    private static async Task WithRetryAsync(Func<Task> action, IProgress<double>? progress, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (attempt <= MaxRetryAttempts && IsTransient(ex) && !ct.IsCancellationRequested)
            {
                progress?.Report(0);
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(delay, ct);
            }
        }
    }

    private static bool IsTransient(Exception ex)
    {
        if (ex is HttpRequestException) return true;
        if (ex is IOException) return true;
        if (ex is TaskCanceledException { InnerException: TimeoutException }) return true;
        return false;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
