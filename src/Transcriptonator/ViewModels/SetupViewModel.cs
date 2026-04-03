using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Transcriptonator.Services;

namespace Transcriptonator.ViewModels;

public partial class SetupViewModel : ViewModelBase
{
    private readonly IModelManagerService _modelManager;
    private readonly IConfigService _configService;
    private readonly Action _onSetupComplete;

    [ObservableProperty]
    private double _whisperProgress;

    [ObservableProperty]
    private double _onnxProgress;

    [ObservableProperty]
    private double _llmProgress;

    [ObservableProperty]
    private string _statusText = "Checking models...";

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _whisperStatus = "Pending";

    [ObservableProperty]
    private string _onnxStatus = "Pending";

    [ObservableProperty]
    private string _llmStatus = "Pending";

    [ObservableProperty]
    private bool _canRetry;

    public SetupViewModel(IModelManagerService modelManager, IConfigService configService, Action onSetupComplete)
    {
        _modelManager = modelManager;
        _configService = configService;
        _onSetupComplete = onSetupComplete;
    }

    [RelayCommand]
    private async Task StartDownloadAsync(CancellationToken ct)
    {
        IsDownloading = true;
        CanRetry = false;
        var config = _configService.Load();

        try
        {
            // Download Whisper model
            if (!_modelManager.IsWhisperModelDownloaded(config.WhisperModelSize))
            {
                WhisperStatus = "Downloading...";
                StatusText = $"Downloading Whisper {config.WhisperModelSize} model...";
                var whisperProgress = new Progress<double>(p => WhisperProgress = p);
                await _modelManager.DownloadWhisperModelAsync(config.WhisperModelSize, whisperProgress, ct);
            }
            WhisperProgress = 1.0;
            WhisperStatus = "Ready";

            // Download ONNX embedding model
            if (!_modelManager.IsOnnxModelDownloaded())
            {
                OnnxStatus = "Downloading...";
                StatusText = "Downloading embedding model...";
                var onnxProgress = new Progress<double>(p => OnnxProgress = p);
                await _modelManager.DownloadOnnxModelAsync(onnxProgress, ct);
            }
            OnnxProgress = 1.0;
            OnnxStatus = "Ready";

            // Download LLM
            if (!_modelManager.IsLlmModelDownloaded())
            {
                LlmStatus = "Downloading...";
                StatusText = "Downloading Phi-3 LLM...";
                var llmProgress = new Progress<double>(p => LlmProgress = p);
                await _modelManager.DownloadLlmModelAsync(llmProgress, ct);
            }
            LlmProgress = 1.0;
            LlmStatus = "Ready";

            StatusText = "All models ready!";
            await Task.Delay(500, ct);
            _onSetupComplete();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Download cancelled.";
            MarkInProgressAsFailed();
            CanRetry = true;
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
            MarkInProgressAsFailed();
            CanRetry = true;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void Skip()
    {
        StartDownloadCommand.Cancel();
        _onSetupComplete();
    }

    private void MarkInProgressAsFailed()
    {
        if (WhisperStatus == "Downloading...") WhisperStatus = "Failed";
        if (OnnxStatus == "Downloading...") OnnxStatus = "Failed";
        if (LlmStatus == "Downloading...") LlmStatus = "Failed";
    }
}
