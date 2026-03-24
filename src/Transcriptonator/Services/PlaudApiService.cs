using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Transcriptonator.Services;

public class PlaudApiService : IPlaudApiService
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _downloadClient;
    private string _baseUrl = "https://api.plaud.ai";
    private const int MaxRetryAttempts = 3;

    public bool IsAuthenticated { get; private set; }
    public Action<string>? LogCallback { get; set; }

    private void Log(string message) => LogCallback?.Invoke(message);

    public PlaudApiService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(30);
        _httpClient.DefaultRequestHeaders.Add("App-Platform", "web");
        _httpClient.DefaultRequestHeaders.Add("Edit-From", "web");

        // Separate client for downloading files — presigned cloud storage URLs
        // reject requests that carry extra Authorization or custom headers.
        _downloadClient = new HttpClient();
        _downloadClient.Timeout = TimeSpan.FromMinutes(30);
    }

    public void SetAuthToken(string token)
    {
        // Token from localStorage is "bearer <jwt>" or just the jwt
        var jwt = token.Trim();
        if (jwt.StartsWith("bearer ", StringComparison.OrdinalIgnoreCase))
            jwt = jwt["bearer ".Length..];

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        IsAuthenticated = true;
    }

    public async Task LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var loginUrl = $"{_baseUrl}/auth/access-token";
        Log($"POST {loginUrl} (user: {email})");

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = email,
            ["password"] = password
        });

        using var response = await _httpClient.PostAsync(loginUrl, content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        Log($"Login response: HTTP {(int)response.StatusCode} {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            Log($"Login response body: {Truncate(json, 500)}");
            throw new HttpRequestException($"Login failed: HTTP {(int)response.StatusCode} {response.StatusCode}");
        }

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("status", out var status) && status.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : "Login failed";
            Log($"Login API error: {msg}");
            throw new InvalidOperationException(msg);
        }

        var accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        var tokenType = root.TryGetProperty("token_type", out var tt) ? tt.GetString() ?? "bearer" : "bearer";

        if (string.IsNullOrEmpty(accessToken))
        {
            Log($"No access_token in response: {Truncate(json, 300)}");
            throw new InvalidOperationException("No access token in response.");
        }

        Log($"Login OK, token length: {accessToken.Length}");
        SetAuthToken($"{tokenType} {accessToken}");
    }

    public async Task<List<PlaudRecordingDto>> ListRecordingsAsync(CancellationToken ct = default)
    {
        var recordings = new List<PlaudRecordingDto>();
        int skip = 0;
        const int limit = 500;

        Log("Fetching recording list...");

        while (true)
        {
            var url = $"{_baseUrl}/file/simple/web?skip={skip}&limit={limit}&is_trash=2&sort_by=start_time&is_desc=true";
            Log($"GET {url}");
            var json = await SendWithRetryAsync(url, ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data_file_list", out var list))
            {
                Log($"No 'data_file_list' in response: {Truncate(json, 300)}");
                break;
            }

            var items = list.EnumerateArray().ToList();
            Log($"Page returned {items.Count} items (skip={skip})");
            if (items.Count == 0) break;

            foreach (var item in items)
            {
                var dto = new PlaudRecordingDto
                {
                    FileId = item.GetProperty("id").GetString() ?? string.Empty,
                    Title = item.TryGetProperty("filename", out var fn) ? fn.GetString() ?? string.Empty : string.Empty,
                    Duration = item.TryGetProperty("duration", out var dur) ? dur.GetDouble() / 1000.0 : 0,
                    FileSize = item.TryGetProperty("filesize", out var fs) ? fs.GetInt64() : null,
                };

                if (item.TryGetProperty("start_time", out var st) && st.ValueKind == JsonValueKind.Number)
                {
                    var epochMs = st.GetInt64();
                    dto.StartTime = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime;
                }

                recordings.Add(dto);
            }

            skip += items.Count;
            if (items.Count < limit) break;
        }

        Log($"Total recordings fetched: {recordings.Count}");
        return recordings;
    }

    public async Task<string> GetDownloadUrlAsync(string fileId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/file/temp-url/{fileId}";
        Log($"GET {url}");
        var json = await SendWithRetryAsync(url, ct);
        var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        var downloadUrl = root.TryGetProperty("temp_url", out var tu) ? tu.GetString() : null;

        if (downloadUrl == null)
        {
            Log($"No 'temp_url' in response: {Truncate(json, 500)}");
            throw new InvalidOperationException("No download URL returned from PLAUD API.");
        }

        // Log the URL domain/path (not query params which contain auth tokens)
        var dlUri = new Uri(downloadUrl);
        Log($"Got download URL: {dlUri.Scheme}://{dlUri.Host}{dlUri.AbsolutePath} (query length: {dlUri.Query.Length})");
        return downloadUrl;
    }

    public async Task DownloadFileAsync(string url, string destPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dlUri = new Uri(url);
        Log($"Downloading from {dlUri.Host}{dlUri.AbsolutePath}...");

        await WithRetryAsync(async () =>
        {
            var dir = Path.GetDirectoryName(destPath);
            if (dir != null) Directory.CreateDirectory(dir);

            var tempPath = destPath + ".tmp";
            try
            {
                Log($"GET {dlUri.Scheme}://{dlUri.Host}{dlUri.AbsolutePath} (presigned URL)");
                using var response = await _downloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

                Log($"Download response: HTTP {(int)response.StatusCode} {response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                {
                    string body;
                    try { body = await response.Content.ReadAsStringAsync(ct); }
                    catch { body = "(could not read body)"; }
                    Log($"Download error response headers:");
                    foreach (var h in response.Headers)
                        Log($"  {h.Key}: {string.Join(", ", h.Value)}");
                    foreach (var h in response.Content.Headers)
                        Log($"  {h.Key}: {string.Join(", ", h.Value)}");
                    Log($"Download error response body: {Truncate(body, 1000)}");
                    response.EnsureSuccessStatusCode(); // throw with status code
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                Log($"Content-Length: {(totalBytes >= 0 ? totalBytes.ToString() : "unknown")}");
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
                        progress?.Report((double)totalRead / totalBytes);
                }

                fileStream.Close();
                Log($"Downloaded {totalRead} bytes to {tempPath}");

                if (new FileInfo(tempPath).Length == 0)
                    throw new IOException("Downloaded file is empty.");

                File.Move(tempPath, destPath, overwrite: true);
                progress?.Report(1.0);
                Log($"Saved to {destPath}");
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
        }, progress, ct);
    }

    private async Task<string> SendWithRetryAsync(string url, CancellationToken ct)
    {
        string result = string.Empty;

        await WithRetryAsync(async () =>
        {
            result = await SendWithRedirectAsync(url, ct);
        }, null, ct);

        return result;
    }

    private async Task<string> SendWithRedirectAsync(string url, CancellationToken ct)
    {
        Log($"API GET {url}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        Log($"API response: HTTP {(int)response.StatusCode} {response.StatusCode} ({json.Length} chars)");

        // Check for regional redirect
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("status", out var status) && status.GetInt32() == -302 &&
            root.TryGetProperty("payload", out var payload) &&
            payload.TryGetProperty("data", out var data) &&
            data.TryGetProperty("domains", out var domains) &&
            domains.TryGetProperty("api", out var apiDomain))
        {
            var newDomain = apiDomain.GetString();
            if (!string.IsNullOrEmpty(newDomain))
            {
                _baseUrl = newDomain.TrimEnd('/');
                if (!_baseUrl.StartsWith("http"))
                    _baseUrl = "https://" + _baseUrl;

                var uri = new Uri(url);
                var newUrl = _baseUrl + uri.PathAndQuery;
                Log($"Regional redirect: {_baseUrl} -> retrying as {newUrl}");
                return await SendWithRedirectAsync(newUrl, ct);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            Log($"API error response headers:");
            foreach (var h in response.Headers)
                Log($"  {h.Key}: {string.Join(", ", h.Value)}");
            Log($"API error body: {Truncate(json, 1000)}");
            throw new HttpRequestException($"PLAUD API returned {(int)response.StatusCode} {response.StatusCode}: {Truncate(json, 500)}");
        }

        return json;
    }

    private async Task WithRetryAsync(Func<Task> action, IProgress<double>? progress, CancellationToken ct)
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
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                Log($"Retry {attempt}/{MaxRetryAttempts} after {ex.GetType().Name}: {ex.Message} (waiting {delay.TotalSeconds}s)");
                progress?.Report(0);
                await Task.Delay(delay, ct);
            }
        }
    }

    private static bool IsTransient(Exception ex)
    {
        if (ex is HttpRequestException hrex)
        {
            if (hrex.StatusCode is HttpStatusCode.TooManyRequests or
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout or
                HttpStatusCode.InternalServerError)
                return true;
            if (hrex.StatusCode == null) return true;
        }
        if (ex is IOException) return true;
        if (ex is TaskCanceledException { InnerException: TimeoutException }) return true;
        return false;
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...(truncated)";
}
