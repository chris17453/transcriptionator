using Transcriptonator.Models;

namespace Transcriptonator.Services;

public record SearchResult(
    TranscriptionChunk Chunk,
    string FileName,
    string SourceFilePath,
    string TranscriptionPath,
    double Similarity);

public interface IVectorSearchService
{
    Task<List<SearchResult>> SearchAsync(string query, int topK, CancellationToken ct = default);
}
