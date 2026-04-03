using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Transcriptonator.Helpers;

public static class AudioConverter
{
    public static readonly string[] SupportedExtensions =
        { ".mp3", ".wav", ".aiff", ".aif", ".wma", ".m4a", ".ogg", ".flac", ".voc" };

    public static bool IsSupported(string filePath)
        => SupportedExtensions.Contains(
            Path.GetExtension(filePath).ToLowerInvariant());

    /// <summary>
    /// Converts an audio file to a 16kHz mono WAV file suitable for Whisper.
    /// Uses ffmpeg on Linux (NAudio's Mp3FileReader requires Windows ACM codecs).
    /// Falls back to NAudio on Windows.
    /// Returns the path to the temporary WAV file.
    /// </summary>
    public static async Task<string> ConvertToWavAsync(string audioPath, CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "transcriptonator");
        Directory.CreateDirectory(tempDir);

        // Copy source to temp so we never touch the original
        var tempId = Guid.NewGuid().ToString("N");
        var ext = Path.GetExtension(audioPath);
        var tempCopy = Path.Combine(tempDir, $"{tempId}{ext}");
        var wavPath = Path.Combine(tempDir, $"{tempId}.wav");

        await using (var src = File.OpenRead(audioPath))
        await using (var dst = File.Create(tempCopy))
        {
            await src.CopyToAsync(dst, ct);
        }

        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                await ConvertWithFfmpegAsync(tempCopy, wavPath, ct);
            }
            else
            {
                await ConvertWithNAudioAsync(tempCopy, wavPath, ct);
            }
        }
        finally
        {
            try { File.Delete(tempCopy); } catch { }
        }

        return wavPath;
    }

    /// <summary>
    /// Gets the duration of an audio file in seconds.
    /// </summary>
    public static double GetDurationSeconds(string audioPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetDurationWithFfprobe(audioPath);
        }

        using var reader = CreateReader(audioPath);
        return reader.TotalTime.TotalSeconds;
    }

    private static async Task ConvertWithFfmpegAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            ArgumentList = { "-i", inputPath, "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", "-y", outputPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg. Is it installed?");

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"ffmpeg failed (exit {process.ExitCode}): {stderr}");
        }
    }

    private static double GetDurationWithFfprobe(string audioPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            ArgumentList = { "-v", "quiet", "-show_entries", "format=duration", "-of", "csv=p=0", audioPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return 0;

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        return double.TryParse(output, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var duration) ? duration : 0;
    }

    private static async Task ConvertWithNAudioAsync(string audioPath, string wavPath, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            using var reader = CreateReader(audioPath);
            ISampleProvider sampleProvider = reader.ToSampleProvider();

            if (sampleProvider.WaveFormat.Channels > 1)
            {
                sampleProvider = new StereoToMonoSampleProvider(sampleProvider);
            }

            if (sampleProvider.WaveFormat.SampleRate != 16000)
            {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 16000);
            }

            WaveFileWriter.CreateWaveFile16(wavPath, sampleProvider);
        }, ct);
    }

    private static WaveStream CreateReader(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".wav" => new WaveFileReader(path),
            ".aiff" or ".aif" => new AiffFileReader(path),
            ".mp3" => new Mp3FileReader(path),
            _ => new AudioFileReader(path),
        };
    }
}
