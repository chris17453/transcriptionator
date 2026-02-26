using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Transcriptonator.Services;

namespace Transcriptonator.ViewModels;

public partial class SearchViewModel : ViewModelBase
{
    private readonly IVectorSearchService _vectorSearchService;
    private readonly ILlmService _llmService;
    private readonly IConfigService _configService;

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private string _llmAnswer = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _isLoadingLlm;

    [ObservableProperty]
    private string _statusText = "Enter a query to search transcriptions";

    [ObservableProperty]
    private SearchResultItem? _selectedResult;

    [ObservableProperty]
    private string _sourceTranscriptionText = string.Empty;

    [ObservableProperty]
    private string _sourceHeaderText = string.Empty;

    public ObservableCollection<SearchResultItem> SearchResults { get; } = new();

    public SearchViewModel(IVectorSearchService vectorSearchService, ILlmService llmService, IConfigService configService)
    {
        _vectorSearchService = vectorSearchService;
        _llmService = llmService;
        _configService = configService;
    }

    partial void OnSelectedResultChanged(SearchResultItem? value)
    {
        if (value == null)
        {
            SourceTranscriptionText = string.Empty;
            SourceHeaderText = string.Empty;
            return;
        }

        SourceHeaderText = $"Source: {value.SourceFilePath}";

        try
        {
            if (File.Exists(value.TranscriptionPath))
            {
                SourceTranscriptionText = File.ReadAllText(value.TranscriptionPath);
            }
            else
            {
                SourceTranscriptionText = "(Transcription file not found at: " + value.TranscriptionPath + ")";
            }
        }
        catch (Exception ex)
        {
            SourceTranscriptionText = $"Error reading transcription: {ex.Message}";
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SearchAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(QueryText)) return;

        IsSearching = true;
        SearchResults.Clear();
        LlmAnswer = string.Empty;
        SelectedResult = null;
        StatusText = "Searching...";

        try
        {
            var config = _configService.Load();
            var results = await _vectorSearchService.SearchAsync(QueryText, config.RagTopK, ct);

            foreach (var result in results)
            {
                SearchResults.Add(new SearchResultItem
                {
                    FileName = result.FileName,
                    SourceFilePath = result.SourceFilePath,
                    TranscriptionPath = result.TranscriptionPath,
                    Similarity = result.Similarity,
                    ChunkText = result.Chunk.Text,
                    TextPreview = result.Chunk.Text.Length > 200
                        ? result.Chunk.Text[..200] + "..."
                        : result.Chunk.Text
                });
            }

            if (results.Count == 0)
            {
                StatusText = "No results found.";
                IsSearching = false;
                return;
            }

            // Auto-select first result to show its source
            SelectedResult = SearchResults[0];

            // Load LLM if needed
            if (!_llmService.IsModelLoaded)
            {
                IsLoadingLlm = true;
                StatusText = "Loading LLM (first time may take a moment)...";
                await _llmService.LoadModelAsync(ct);
                IsLoadingLlm = false;
            }

            // Stream LLM answer
            StatusText = "Generating answer...";
            var sb = new StringBuilder();
            await foreach (var token in _llmService.GenerateAnswerAsync(QueryText, results, ct))
            {
                sb.Append(token);
                LlmAnswer = sb.ToString();
            }

            StatusText = $"Found {results.Count} relevant chunks";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Search cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
            IsLoadingLlm = false;
        }
    }
}

public class SearchResultItem
{
    public string FileName { get; set; } = string.Empty;
    public string SourceFilePath { get; set; } = string.Empty;
    public string TranscriptionPath { get; set; } = string.Empty;
    public double Similarity { get; set; }
    public string SimilarityDisplay => $"{Similarity:P0}";
    public string ChunkText { get; set; } = string.Empty;
    public string TextPreview { get; set; } = string.Empty;
}
