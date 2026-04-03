using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Transcriptonator.Services;

namespace Transcriptonator.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IModelManagerService _modelManager;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private string _selectedWhisperModel = "tiny.en";

    [ObservableProperty]
    private int _threadCount = 4;

    [ObservableProperty]
    private string _configDirectoryPath = string.Empty;

    [ObservableProperty]
    private string _whisperModelStatus = "Unknown";

    [ObservableProperty]
    private string _onnxModelStatus = "Unknown";

    [ObservableProperty]
    private string _llmModelStatus = "Unknown";

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private double _whisperDownloadProgress;

    [ObservableProperty]
    private bool _isDownloadingWhisper;

    [ObservableProperty]
    private double _onnxDownloadProgress;

    [ObservableProperty]
    private bool _isDownloadingOnnx;

    [ObservableProperty]
    private double _llmDownloadProgress;

    [ObservableProperty]
    private bool _isDownloadingLlm;

    public int MaxThreads { get; } = Environment.ProcessorCount;

    public string AppVersion { get; } =
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "unknown";

    public ObservableCollection<string> WhisperModelSizes { get; } = new()
    {
        "tiny.en", "base.en", "small.en", "medium.en", "tiny", "base", "small", "medium", "large"
    };

    public SettingsViewModel(IConfigService configService, IModelManagerService modelManager)
    {
        _configService = configService;
        _modelManager = modelManager;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var config = _configService.Load();
        OutputDirectory = config.OutputDirectory;
        SelectedWhisperModel = config.WhisperModelSize;
        ThreadCount = config.ThreadCount;
        ConfigDirectoryPath = _configService.ConfigDirectory;
        RefreshModelStatus();
    }

    partial void OnSelectedWhisperModelChanged(string value)
    {
        RefreshModelStatus();
    }

    private void RefreshModelStatus()
    {
        WhisperModelStatus = _modelManager.IsWhisperModelDownloaded(SelectedWhisperModel)
            ? "Downloaded" : "Not downloaded";
        OnnxModelStatus = _modelManager.IsOnnxModelDownloaded()
            ? "Downloaded" : "Not downloaded";
        LlmModelStatus = _modelManager.IsLlmModelDownloaded()
            ? "Downloaded" : "Not downloaded";
    }

    public Func<Task>? BrowseOutputDirectoryInteraction { get; set; }

    [RelayCommand]
    private async Task BrowseOutputDirectoryAsync()
    {
        if (BrowseOutputDirectoryInteraction != null)
            await BrowseOutputDirectoryInteraction();
    }

    [RelayCommand]
    private void Save()
    {
        var config = _configService.Load();
        config.OutputDirectory = OutputDirectory;
        config.WhisperModelSize = SelectedWhisperModel;
        config.ThreadCount = ThreadCount;
        _configService.Save(config);
        StatusText = "Settings saved.";
        RefreshModelStatus();
    }

    [RelayCommand]
    private async Task DownloadWhisperModelAsync(CancellationToken ct)
    {
        IsDownloadingWhisper = true;
        WhisperDownloadProgress = 0;
        StatusText = string.Empty;
        try
        {
            var progress = new Progress<double>(p => WhisperDownloadProgress = p);
            await _modelManager.DownloadWhisperModelAsync(SelectedWhisperModel, progress, ct);
            StatusText = "Whisper model downloaded.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Download cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloadingWhisper = false;
            RefreshModelStatus();
        }
    }

    [RelayCommand]
    private async Task DownloadOnnxModelAsync(CancellationToken ct)
    {
        IsDownloadingOnnx = true;
        OnnxDownloadProgress = 0;
        StatusText = string.Empty;
        try
        {
            var progress = new Progress<double>(p => OnnxDownloadProgress = p);
            await _modelManager.DownloadOnnxModelAsync(progress, ct);
            StatusText = "Embedding model downloaded.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Download cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloadingOnnx = false;
            RefreshModelStatus();
        }
    }

    [RelayCommand]
    private async Task DownloadLlmModelAsync(CancellationToken ct)
    {
        IsDownloadingLlm = true;
        LlmDownloadProgress = 0;
        StatusText = string.Empty;
        try
        {
            var progress = new Progress<double>(p => LlmDownloadProgress = p);
            await _modelManager.DownloadLlmModelAsync(progress, ct);
            StatusText = "LLM downloaded.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Download cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloadingLlm = false;
            RefreshModelStatus();
        }
    }
}
