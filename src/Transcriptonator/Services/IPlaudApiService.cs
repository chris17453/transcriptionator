namespace Transcriptonator.Services;

public interface IPlaudApiService
{
    bool IsAuthenticated { get; }
    Action<string>? LogCallback { get; set; }
    void SetAuthToken(string token);
    void ClearAuthToken();
    Task<List<PlaudRecordingDto>> ListRecordingsAsync(CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(string fileId, CancellationToken ct = default);
    Task DownloadFileAsync(string url, string destPath, IProgress<double>? progress = null, CancellationToken ct = default);
}

public class PlaudAuthException : Exception
{
    public PlaudAuthException() : base("PLAUD token is invalid or expired. Please provide a new token.") { }
}

public class PlaudRecordingDto
{
    public string FileId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public double Duration { get; set; }
    public string? TagName { get; set; }
    public long? FileSize { get; set; }
}
