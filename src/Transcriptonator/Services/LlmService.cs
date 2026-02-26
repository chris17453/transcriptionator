using System.Runtime.CompilerServices;
using System.Text;
using LLama;
using LLama.Common;

namespace Transcriptonator.Services;

public class LlmService : ILlmService, IDisposable
{
    private readonly IModelManagerService _modelManager;
    private LLamaWeights? _model;
    private LLamaContext? _context;
    private bool _disposed;

    public bool IsModelLoaded => _model != null;

    public LlmService(IModelManagerService modelManager)
    {
        _modelManager = modelManager;
    }

    public async Task LoadModelAsync(CancellationToken ct = default)
    {
        if (_model != null) return;

        var modelPath = _modelManager.GetLlmModelPath();

        await Task.Run(() =>
        {
            var parameters = new ModelParams(modelPath)
            {
                ContextSize = 4096,
                GpuLayerCount = 0
            };

            _model = LLamaWeights.LoadFromFile(parameters);
            _context = _model.CreateContext(parameters);
        }, ct);
    }

    public async IAsyncEnumerable<string> GenerateAnswerAsync(
        string question,
        List<SearchResult> context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_model == null || _context == null)
        {
            await LoadModelAsync(ct);
        }

        var prompt = BuildRagPrompt(question, context);

        var executor = new InteractiveExecutor(_context!);
        var inferenceParams = new InferenceParams
        {
            MaxTokens = 512,
            AntiPrompts = new[] { "<|end|>", "<|user|>", "\nUser:" }
        };

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, ct))
        {
            yield return token;
        }
    }

    private static string BuildRagPrompt(string question, List<SearchResult> context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<|system|>");
        sb.AppendLine("You are a helpful assistant that answers questions based on transcribed audio content. Use the provided context to answer the question. Always cite the exact source file path when referencing information. If the context doesn't contain relevant information, say so.");
        sb.AppendLine("<|end|>");

        sb.AppendLine("<|user|>");
        sb.AppendLine("Context from transcriptions:");
        sb.AppendLine();

        foreach (var result in context)
        {
            sb.AppendLine($"[Source: {result.SourceFilePath} ({result.FileName}), Relevance: {result.Similarity:P0}]");
            sb.AppendLine(result.Chunk.Text);
            sb.AppendLine();
        }

        sb.AppendLine($"Question: {question}");
        sb.AppendLine("<|end|>");
        sb.AppendLine("<|assistant|>");

        return sb.ToString();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _context?.Dispose();
            _model?.Dispose();
            _disposed = true;
        }
    }
}
