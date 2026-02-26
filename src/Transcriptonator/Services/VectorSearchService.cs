using System.Numerics.Tensors;
using Microsoft.EntityFrameworkCore;
using Transcriptonator.Data;
using Transcriptonator.Models;

namespace Transcriptonator.Services;

public class VectorSearchService : IVectorSearchService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IDbContextFactory<TranscriptonatorDbContext> _dbFactory;

    public VectorSearchService(IEmbeddingService embeddingService, IDbContextFactory<TranscriptonatorDbContext> dbFactory)
    {
        _embeddingService = embeddingService;
        _dbFactory = dbFactory;
    }

    public async Task<List<SearchResult>> SearchAsync(string query, int topK, CancellationToken ct = default)
    {
        var queryEmbedding = _embeddingService.GenerateEmbedding(query);

        using var db = await _dbFactory.CreateDbContextAsync(ct);
        var chunks = await db.TranscriptionChunks
            .Include(c => c.ProcessedFile)
            .ToListAsync(ct);

        var results = new List<SearchResult>();

        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();

            if (chunk.Embedding.Length == 0) continue;

            var chunkEmbedding = new float[chunk.Embedding.Length / sizeof(float)];
            Buffer.BlockCopy(chunk.Embedding, 0, chunkEmbedding, 0, chunk.Embedding.Length);

            var similarity = TensorPrimitives.CosineSimilarity(queryEmbedding, chunkEmbedding);
            var fileName = Path.GetFileName(chunk.ProcessedFile.FilePath);

            results.Add(new SearchResult(
                chunk,
                fileName,
                chunk.ProcessedFile.FilePath,
                chunk.ProcessedFile.TranscriptionPath,
                similarity));
        }

        return results
            .OrderByDescending(r => r.Similarity)
            .Take(topK)
            .ToList();
    }
}
