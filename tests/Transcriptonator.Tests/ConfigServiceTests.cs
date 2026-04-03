using Transcriptonator.Services;

namespace Transcriptonator.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void Constructor_SetsDirectoryPaths()
    {
        var service = new ConfigService();

        Assert.False(string.IsNullOrEmpty(service.AppDataDirectory));
        Assert.False(string.IsNullOrEmpty(service.ConfigDirectory));
        Assert.False(string.IsNullOrEmpty(service.ModelsDirectory));
        Assert.False(string.IsNullOrEmpty(service.DatabasePath));
        Assert.Contains("Transcriptonator", service.AppDataDirectory);
    }

    [Fact]
    public void Load_ReturnsDefaultsOnFirstRun()
    {
        var service = new ConfigService();
        var config = service.Load();

        Assert.NotNull(config);
        Assert.Equal("tiny.en", config.WhisperModelSize);
        Assert.True(config.ThreadCount >= 1);
        Assert.Equal(500, config.ChunkSize);
        Assert.Equal(100, config.ChunkOverlap);
        Assert.Equal(5, config.RagTopK);
    }

    [Fact]
    public void Save_And_Load_RoundTrips()
    {
        var service = new ConfigService();
        var config = service.Load();

        config.WhisperModelSize = "medium";
        config.ThreadCount = 8;
        config.RagTopK = 10;
        service.Save(config);

        var reloaded = service.Load();
        Assert.Equal("medium", reloaded.WhisperModelSize);
        Assert.Equal(8, reloaded.ThreadCount);
        Assert.Equal(10, reloaded.RagTopK);

        // Restore defaults so we don't pollute the real config
        service.Save(new Models.AppConfig());
    }

    [Fact]
    public void DirectoryPaths_AreConsistent()
    {
        var service = new ConfigService();

        Assert.StartsWith(service.AppDataDirectory, service.ConfigDirectory);
        Assert.StartsWith(service.AppDataDirectory, service.ModelsDirectory);
        Assert.StartsWith(service.ModelsDirectory, service.WhisperModelsDirectory);
        Assert.StartsWith(service.ModelsDirectory, service.OnnxModelsDirectory);
        Assert.StartsWith(service.ModelsDirectory, service.LlmModelsDirectory);
        Assert.StartsWith(service.AppDataDirectory, service.DatabasePath);
    }
}
