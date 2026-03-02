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
    /// Supports MP3, WAV, AIFF, and other formats via NAudio.
    /// Returns the path to the temporary WAV file.
    /// </summary>
    public static async Task<string> ConvertToWavAsync(string audioPath, CancellationToken ct = default)
    {
        var wavPath = Path.Combine(Path.GetTempPath(), $"transcriptonator_{Guid.NewGuid():N}.wav");

        await Task.Run(() =>
        {
            using var reader = CreateReader(audioPath);
            ISampleProvider sampleProvider = reader.ToSampleProvider();

            // Convert to mono if stereo
            if (sampleProvider.WaveFormat.Channels > 1)
            {
                sampleProvider = new StereoToMonoSampleProvider(sampleProvider);
            }

            // Resample to 16kHz using WDL resampler (cross-platform, no MediaFoundation)
            if (sampleProvider.WaveFormat.SampleRate != 16000)
            {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 16000);
            }

            // Write as 16-bit PCM WAV
            WaveFileWriter.CreateWaveFile16(wavPath, sampleProvider);
        }, ct);

        return wavPath;
    }

    /// <summary>
    /// Gets the duration of an audio file in seconds.
    /// </summary>
    public static double GetDurationSeconds(string audioPath)
    {
        using var reader = CreateReader(audioPath);
        return reader.TotalTime.TotalSeconds;
    }

    private static WaveStream CreateReader(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".wav" => new WaveFileReader(path),
            ".aiff" or ".aif" => new AiffFileReader(path),
            // Mp3FileReader handles MP3; AudioFileReader handles anything
            // the platform supports (WMA/M4A/OGG/FLAC via MediaFoundation on Windows)
            ".mp3" => new Mp3FileReader(path),
            _ => new AudioFileReader(path),
        };
    }
}
