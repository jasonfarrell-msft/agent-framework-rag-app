using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

namespace Example.ChatApi.Services;

public class SearchService
{
    private readonly SearchClient _client;

    public SearchService(IConfiguration configuration)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SearchService: Initializing with DefaultAzureCredential...");

        var searchEndpoint = configuration["AzureSearch:Endpoint"]
            ?? throw new InvalidOperationException("AzureSearch:Endpoint is not configured.");
        var indexName = configuration["AzureSearch:IndexName"]
            ?? throw new InvalidOperationException("AzureSearch:IndexName is not configured.");

        var credential = new DefaultAzureCredential();
        _client = new SearchClient(new Uri(searchEndpoint), indexName, credential);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SearchService: Initialized.");
    }

    public async Task<List<SearchDocument>> HybridSearchAsync(string query, int top = 5)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Search: Building query for '{query}' (top={top})...");
        var options = new SearchOptions
        {
            Size = top,
            QueryType = SearchQueryType.Simple,
            Select = { "document_title", "content_text" },
            VectorSearch = new()
            {
                Queries =
                {
                    new VectorizableTextQuery(query)
                    {
                        KNearestNeighborsCount = top,
                        Fields = { "content_embedding" }
                    }
                }
            }
        };

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Search: Executing SearchAsync...");
        var response = await _client.SearchAsync<SearchDocument>(query, options);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Search: SearchAsync returned. Iterating results...");
        var results = new List<SearchDocument>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            results.Add(result.Document);
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Search: Got {results.Count} result(s).");
        return results;
    }
}
