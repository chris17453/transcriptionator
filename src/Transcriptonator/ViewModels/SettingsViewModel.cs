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
    private string _selectedWhisperModel = "small";

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

    public int MaxThreads { get; } = Environment.ProcessorCount;

    public ObservableCollection<string> WhisperModelSizes { get; } = new()
    {
        "tiny", "base", "small", "medium", "large"
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
}
