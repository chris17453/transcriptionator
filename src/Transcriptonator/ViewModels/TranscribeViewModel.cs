using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Transcriptonator.Helpers;
using Transcriptonator.Models;
using Transcriptonator.Services;

namespace Transcriptonator.ViewModels;

public partial class TranscribeViewModel : ViewModelBase
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly IFileTrackingService _fileTrackingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IConfigService _configService;
    [ObservableProperty]
    private string _inputPath = string.Empty;

    [ObservableProperty]
    private double _currentFileProgress;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isTranscribing;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _skippedCount;

    [ObservableProperty]
    private int _failedCount;

    public ObservableCollection<Mp3FileItem> Files { get; } = new();

    public TranscribeViewModel(
        ITranscriptionService transcriptionService,
        IFileTrackingService fileTrackingService,
        IEmbeddingService embeddingService,
        IConfigService configService)
    {
        _transcriptionService = transcriptionService;
        _fileTrackingService = fileTrackingService;
        _embeddingService = embeddingService;
        _configService = configService;
    }

    // Set by View code-behind to wire platform file dialogs
    public Func<Task>? BrowseFileInteraction { get; set; }
    public Func<Task>? BrowseFolderInteraction { get; set; }

    [RelayCommand]
    private async Task BrowseFileAsync()
    {
        if (BrowseFileInteraction != null)
            await BrowseFileInteraction();
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (BrowseFolderInteraction != null)
            await BrowseFolderInteraction();
    }

    public void LoadFiles(string path)
    {
        Files.Clear();
        CompletedCount = 0;
        SkippedCount = 0;
        FailedCount = 0;
        InputPath = path;

        IEnumerable<string> audioFiles;

        if (File.Exists(path) && AudioConverter.IsSupported(path))
        {
            audioFiles = new[] { path };
        }
        else if (Directory.Exists(path))
        {
            audioFiles = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(AudioConverter.IsSupported);
        }
        else
        {
            return;
        }

        foreach (var file in audioFiles.OrderBy(f => f))
        {
            var info = new FileInfo(file);
            Files.Add(new Mp3FileItem
            {
                FilePath = file,
                FileName = info.Name,
                FileSizeBytes = info.Length,
                Status = "Pending"
            });
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task StartTranscriptionAsync(CancellationToken ct)
    {
        if (Files.Count == 0) return;

        IsTranscribing = true;
        CompletedCount = 0;
        SkippedCount = 0;
        FailedCount = 0;
        var config = _configService.Load();

        try
        {
            for (int i = 0; i < Files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var item = Files[i];
                OverallProgress = (double)i / Files.Count;

                // Check if already processed
                var hash = await FileHasher.ComputeSha256Async(item.FilePath, ct);
                if (await _fileTrackingService.IsFileProcessedAsync(item.FilePath, hash, ct))
                {
                    item.Status = "Skipped";
                    SkippedCount++;
                    continue;
                }

                item.Status = "Transcribing...";
                StatusText = $"Transcribing: {item.FileName}";
                CurrentFileProgress = 0;

                try
                {
                    var progress = new Progress<double>(p => CurrentFileProgress = p);
                    var audioDuration = AudioConverter.GetDurationSeconds(item.FilePath);

                    var result = await _transcriptionService.TranscribeAsync(
                        item.FilePath, config.OutputDirectory, config.WhisperModelSize, progress, ct);

                    var processedFile = new ProcessedFile
                    {
                        FilePath = item.FilePath,
                        FileHash = hash,
                        FileSizeBytes = item.FileSizeBytes,
                        TranscriptionPath = result.OutputPath,
                        TranscribedAtUtc = DateTime.UtcNow,
                        WhisperModel = config.WhisperModelSize,
                        AudioDurationSeconds = audioDuration,
                        TranscriptionDurationSeconds = result.DurationSeconds
                    };

                    var saved = await _fileTrackingService.RecordTranscriptionAsync(processedFile, ct);

                    // Generate and store embeddings
                    StatusText = $"Embedding: {item.FileName}";
                    await _embeddingService.StoreChunksAsync(saved, result.Text, ct);

                    item.Status = "Done";
                    CompletedCount++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    item.Status = $"Error: {ex.Message}";
                    FailedCount++;
                }
            }

            OverallProgress = 1.0;
            StatusText = $"Complete: {CompletedCount} transcribed, {SkippedCount} skipped, {FailedCount} failed";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Transcription cancelled.";
        }
        finally
        {
            IsTranscribing = false;
        }
    }
}

public partial class Mp3FileItem : ObservableObject
{
    public string FilePath { get; set; } = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    public long FileSizeBytes { get; set; }

    public string FileSizeDisplay => FileSizeBytes switch
    {
        < 1024 => $"{FileSizeBytes} B",
        < 1048576 => $"{FileSizeBytes / 1024.0:F1} KB",
        _ => $"{FileSizeBytes / 1048576.0:F1} MB"
    };

    [ObservableProperty]
    private string _status = "Pending";
}
