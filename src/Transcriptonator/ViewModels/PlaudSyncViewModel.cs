using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Transcriptonator.Data;
using Transcriptonator.Models;
using Transcriptonator.Services;

namespace Transcriptonator.ViewModels;

public partial class PlaudSyncViewModel : ViewModelBase
{
    private readonly IPlaudApiService _plaudApi;
    private readonly IDbContextFactory<TranscriptonatorDbContext> _dbFactory;
    private readonly IConfigService _configService;
    private readonly TokenListenerService _tokenListener;
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _statusText = "Log in to sync your PLAUD recordings.";

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _downloadedCount;

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private int _failedCount;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private int _maxConcurrentDownloads = 3;

    [ObservableProperty]
    private bool _hasRecordings;

    [ObservableProperty]
    private string _debugLogText = string.Empty;

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private bool _isLoggingIn;

    // Email/password login
    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    // Token paste fallback
    [ObservableProperty]
    private string _manualToken = string.Empty;

    public ObservableCollection<PlaudRecordingItem> Recordings { get; } = new();

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        DebugLogText += line;
    }

    public PlaudSyncViewModel(
        IPlaudApiService plaudApi,
        IDbContextFactory<TranscriptonatorDbContext> dbFactory,
        IConfigService configService,
        TokenListenerService tokenListener)
    {
        _plaudApi = plaudApi;
        _dbFactory = dbFactory;
        _configService = configService;
        _tokenListener = tokenListener;

        var config = configService.Load();
        _maxConcurrentDownloads = config.MaxConcurrentPlaudDownloads;

        _tokenListener.TokenReceived += OnTokenReceived;
        _tokenListener.Start();
        Log($"Token listener started on port {TokenListenerService.Port}");

    }

    private void OnTokenReceived(string token)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _plaudApi.SetAuthToken(token);
            IsLoggedIn = true;
            Log($"Token received from browser extension ({token.Length} chars)");
            StatusText = "Logged in via browser extension! Click Sync Recordings.";
        });
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            StatusText = "Enter your email and password.";
            return;
        }

        IsLoggingIn = true;
        try
        {
            Log($"Logging in as {Email}...");
            StatusText = "Logging in...";
            await _plaudApi.LoginAsync(Email.Trim(), Password.Trim());
            IsLoggedIn = true;
            Log("Login successful");
            StatusText = "Logged in. Click Sync Recordings to fetch your recordings.";
        }
        catch (Exception ex)
        {
            Log($"LOGIN ERROR: {ex.Message}");
            StatusText = $"Login failed: {ex.Message}";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    [ObservableProperty]
    private bool _isExtensionInstalled;

    private static string ExtensionDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Transcriptonator", "ChromeExtension");

    public Func<string, Task>? CopyToClipboardInteraction { get; set; }

    [RelayCommand]
    private async Task CopyExtensionsUrlAsync()
    {
        if (CopyToClipboardInteraction != null)
        {
            await CopyToClipboardInteraction("chrome://extensions");
            EnsureExtensionFilesCopied();
            Log("Copied chrome://extensions to clipboard");
            StatusText = "Copied! Paste in Chrome address bar, hit Enter, enable Developer Mode.";
        }
    }

    [RelayCommand]
    private async Task CopyExtensionPathAsync()
    {
        EnsureExtensionFilesCopied();
        if (CopyToClipboardInteraction != null)
        {
            await CopyToClipboardInteraction(ExtensionDir);
            Log($"Extension path copied: {ExtensionDir}");
            StatusText = "Path copied! Click 'Load unpacked' in Chrome, paste in location bar, click Open.";
        }
    }

    [RelayCommand]
    private void ExtensionInstallDone()
    {
        IsExtensionInstalled = true;
        Log("Extension marked as installed");
        StatusText = "Extension ready! Click 'Open PLAUD Login' to sign in.";
    }

    private void EnsureExtensionFilesCopied()
    {
        if (Directory.Exists(ExtensionDir) && File.Exists(Path.Combine(ExtensionDir, "manifest.json")))
            return;

        var sourceDir = Path.Combine(AppContext.BaseDirectory, "ChromeExtension");
        if (!Directory.Exists(sourceDir))
        {
            Log($"Extension source not found at: {sourceDir}");
            return;
        }

        if (Directory.Exists(ExtensionDir))
            Directory.Delete(ExtensionDir, true);
        Directory.CreateDirectory(ExtensionDir);

        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(ExtensionDir, Path.GetFileName(file)));

        Log($"Extension files copied to: {ExtensionDir}");
    }

    [RelayCommand]
    private void UninstallExtension()
    {
        try
        {
            var destDir = ExtensionDir;
            if (Directory.Exists(destDir))
            {
                Directory.Delete(destDir, true);
                Log("Extension files removed");
            }
            IsExtensionInstalled = false;
            StatusText = "Extension files removed. Go to chrome://extensions in Chrome to remove it there too.";
        }
        catch (Exception ex)
        {
            Log($"Uninstall extension error: {ex.Message}");
            StatusText = $"Failed to uninstall: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://web.plaud.ai/login",
                UseShellExecute = true
            });
            StatusText = "Log in with Google, then click the Transcriptonator extension icon to send the token.";
        }
        catch (Exception ex)
        {
            Log($"Failed to open browser: {ex.Message}");
        }
    }

    [RelayCommand]
    private void UseManualToken()
    {
        if (string.IsNullOrWhiteSpace(ManualToken))
        {
            StatusText = "Paste a token first.";
            return;
        }

        _plaudApi.SetAuthToken(ManualToken.Trim());
        IsLoggedIn = true;
        Log($"Token set ({ManualToken.Trim().Length} chars)");
        StatusText = "Token set. Click Sync Recordings to fetch your recordings.";
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (IsSyncing) return;

        if (!_plaudApi.IsAuthenticated)
        {
            StatusText = "Please log in first.";
            return;
        }

        IsSyncing = true;
        Log("Sync started");

        try
        {
            Log("Fetching recordings from PLAUD API...");
            StatusText = "Fetching recordings from PLAUD...";

            var dtos = await _plaudApi.ListRecordingsAsync();
            Log($"API returned {dtos.Count} recordings");

            await using var db = await _dbFactory.CreateDbContextAsync();
            var existingIds = await db.PlaudRecordings
                .Select(r => r.PlaudFileId)
                .ToListAsync();
            var existingSet = existingIds.ToHashSet();

            int newCount = 0;
            foreach (var dto in dtos)
            {
                if (!existingSet.Contains(dto.FileId))
                {
                    db.PlaudRecordings.Add(new PlaudRecording
                    {
                        PlaudFileId = dto.FileId,
                        Title = dto.Title,
                        RecordedAtUtc = dto.StartTime,
                        DurationSeconds = dto.Duration,
                        FolderName = dto.TagName,
                        FileSizeBytes = dto.FileSize,
                        IsDownloaded = false,
                    });
                    newCount++;
                }
            }

            if (newCount > 0)
                await db.SaveChangesAsync();

            var allRecordings = await db.PlaudRecordings
                .OrderByDescending(r => r.RecordedAtUtc)
                .ToListAsync();

            Recordings.Clear();
            foreach (var rec in allRecordings)
            {
                Recordings.Add(new PlaudRecordingItem
                {
                    Id = rec.Id,
                    PlaudFileId = rec.PlaudFileId,
                    Title = rec.Title,
                    RecordedAtUtc = rec.RecordedAtUtc,
                    DurationSeconds = rec.DurationSeconds,
                    FolderName = rec.FolderName,
                    FileSizeBytes = rec.FileSizeBytes,
                    IsDownloaded = rec.IsDownloaded,
                    Status = rec.IsDownloaded ? "Downloaded" : "Pending",
                });
            }

            UpdateCounts();
            Log($"Sync complete: {allRecordings.Count} total, {newCount} new");
            StatusText = $"Synced: {allRecordings.Count} recordings ({newCount} new).";
        }
        catch (Exception ex)
        {
            Log($"SYNC ERROR: {ex.GetType().Name}: {ex.Message}");
            StatusText = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAllAsync()
    {
        if (!_plaudApi.IsAuthenticated || IsDownloading) return;

        IsDownloading = true;
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;
        FailedCount = 0;

        var config = _configService.Load();
        var downloadDir = Path.Combine(config.OutputDirectory, "PLAUD");
        Directory.CreateDirectory(downloadDir);

        var pending = Recordings.Where(r => !r.IsDownloaded && r.Status != "Downloading...").ToList();
        if (pending.Count == 0)
        {
            Log("No pending recordings to download");
            StatusText = "All recordings already downloaded.";
            IsDownloading = false;
            return;
        }

        Log($"Starting download of {pending.Count} recordings (max {MaxConcurrentDownloads} concurrent) to {downloadDir}");
        StatusText = $"Downloading {pending.Count} recordings...";
        var semaphore = new SemaphoreSlim(MaxConcurrentDownloads);
        int completed = 0;

        var tasks = pending.Select(async item =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                item.Status = "Downloading...";
                item.Progress = 0;

                Log($"[{item.Title}] Fetching download URL for {item.PlaudFileId}...");
                var downloadUrl = await _plaudApi.GetDownloadUrlAsync(item.PlaudFileId, ct);
                Log($"[{item.Title}] Got URL, starting download...");

                var safeTitle = string.Join("_", item.Title.Split(Path.GetInvalidFileNameChars()));
                if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = item.PlaudFileId;
                var destPath = Path.Combine(downloadDir, safeTitle + ".mp3");

                if (File.Exists(destPath))
                {
                    var baseName = Path.GetFileNameWithoutExtension(destPath);
                    var ext = Path.GetExtension(destPath);
                    int counter = 1;
                    while (File.Exists(destPath))
                    {
                        destPath = Path.Combine(downloadDir, $"{baseName}_{counter++}{ext}");
                    }
                }

                var progress = new Progress<double>(p => item.Progress = p);
                await _plaudApi.DownloadFileAsync(downloadUrl, destPath, progress, ct);

                await using var db = await _dbFactory.CreateDbContextAsync();
                var recording = await db.PlaudRecordings.FirstAsync(r => r.Id == item.Id, ct);
                recording.IsDownloaded = true;
                recording.LocalFilePath = destPath;
                recording.DownloadedAtUtc = DateTime.UtcNow;
                recording.FileSizeBytes = new FileInfo(destPath).Length;
                await db.SaveChangesAsync(ct);

                item.IsDownloaded = true;
                item.Status = "Downloaded";
                item.Progress = 1.0;

                var done = Interlocked.Increment(ref completed);
                OverallProgress = (double)done / pending.Count;
                UpdateCounts();
                Log($"[{item.Title}] Downloaded ({item.SizeDisplay}) -> {destPath}");
            }
            catch (OperationCanceledException)
            {
                item.Status = "Cancelled";
                Log($"[{item.Title}] Cancelled");
            }
            catch (Exception ex)
            {
                item.Status = $"Error: {ex.Message}";
                Interlocked.Increment(ref completed);
                FailedCount++;
                OverallProgress = (double)completed / pending.Count;
                Log($"[{item.Title}] DOWNLOAD ERROR: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    Log($"[{item.Title}]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        try
        {
            await Task.WhenAll(tasks);
            UpdateCounts();
            Log($"Download complete: {DownloadedCount} downloaded, {FailedCount} failed");
            StatusText = $"Done: {DownloadedCount} downloaded, {FailedCount} failed.";
        }
        catch (OperationCanceledException)
        {
            Log("Download batch cancelled");
            StatusText = "Download cancelled.";
        }
        finally
        {
            IsDownloading = false;
            _downloadCts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _downloadCts?.Cancel();
    }

    private void UpdateCounts()
    {
        TotalCount = Recordings.Count;
        DownloadedCount = Recordings.Count(r => r.IsDownloaded);
        PendingCount = Recordings.Count(r => !r.IsDownloaded && !r.Status.StartsWith("Error"));
        FailedCount = Recordings.Count(r => r.Status.StartsWith("Error"));
        HasRecordings = Recordings.Count > 0;
    }
}

public partial class PlaudRecordingItem : ObservableObject
{
    public int Id { get; set; }
    public string PlaudFileId { get; set; } = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    public DateTime RecordedAtUtc { get; set; }
    public double DurationSeconds { get; set; }
    public string? FolderName { get; set; }
    public long? FileSizeBytes { get; set; }

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private string _status = "Pending";

    [ObservableProperty]
    private double _progress;

    public string DateDisplay => RecordedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string DurationDisplay
    {
        get
        {
            var ts = TimeSpan.FromSeconds(DurationSeconds);
            return ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"m\:ss");
        }
    }

    public string SizeDisplay => FileSizeBytes switch
    {
        null => "-",
        < 1024 => $"{FileSizeBytes} B",
        < 1048576 => $"{FileSizeBytes / 1024.0:F1} KB",
        _ => $"{FileSizeBytes / 1048576.0:F1} MB"
    };
}
