using Transcriptonator.Models;

namespace Transcriptonator.Tests;

public class AppConfigTests
{
    [Fact]
    public void Defaults_AreReasonable()
    {
        var config = new AppConfig();

        Assert.Equal("tiny.en", config.WhisperModelSize);
        Assert.True(config.ThreadCount >= 1);
        Assert.Equal(500, config.ChunkSize);
        Assert.Equal(100, config.ChunkOverlap);
        Assert.Equal(5, config.RagTopK);
        Assert.Contains("Transcriptonator", config.OutputDirectory);
    }

    [Fact]
    public void ThreadCount_DefaultsToHalfProcessorCount()
    {
        var config = new AppConfig();
        var expected = Math.Max(1, Environment.ProcessorCount / 2);
        Assert.Equal(expected, config.ThreadCount);
    }
}
