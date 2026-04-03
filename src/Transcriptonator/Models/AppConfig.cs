namespace Transcriptonator.Models;

public class AppConfig
{
    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Transcriptonator");

    public string WhisperModelSize { get; set; } = "tiny.en";
    public int ThreadCount { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public string? PlaudToken { get; set; }
    public int ChunkSize { get; set; } = 500;
    public int ChunkOverlap { get; set; } = 100;
    public int RagTopK { get; set; } = 5;
    public int MaxConcurrentPlaudDownloads { get; set; } = 3;
    public string LastImportPath { get; set; } = string.Empty;
}
