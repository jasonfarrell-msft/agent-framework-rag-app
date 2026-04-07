using System.Diagnostics.CodeAnalysis;
using System.Text;
using Azure.AI.OpenAI;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Example.ChatApi.Services;

public class ChatService : IDisposable
{
    private readonly ChatClient _chatClient;
    private readonly IConfiguration _configuration;

    // Cosmos DB (optional, used when ChatHistoryProvider is "Cosmos")
    private readonly CosmosClient? _cosmosClient;
    private readonly string? _cosmosDatabaseId;
    private readonly string? _cosmosContainerId;

    // Table Storage (optional, used when ChatHistoryProvider is "TableStorage")
    private readonly TableClient? _tableClient;

    private readonly string _chatHistoryProviderType;

    public const string SystemPrompt = """
        You are a helpful assistant that specializes in communicating with electrical field workers. You present clean and concise responses and cite where you got your responses.

        You only consider information available in the results and do not use any other information.

        Format your entire response in valid Markdown.

        For inline citations, use HTML superscript tags like <sup>[1](#)</sup>, <sup>[2](#)</sup>, etc. Do NOT use caret (^) notation.

        At the end of your response, list all unique source documents as a numbered Markdown list with clickable links. Use this format:
        1. [Document Title](https://myfiles.com/files/<document_title>)

        The document title comes from the "document_title" field.
        The url will be 'https://myfiles.com/files/<document title>'
        """;

    public ChatService(IConfiguration configuration)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ChatService: Initializing Agent Framework...");

        _configuration = configuration;
        var credential = new DefaultAzureCredential();

        var openAiEndpoint = configuration["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is not configured.");
        var deploymentName = configuration["AzureOpenAI:DeploymentName"]
            ?? throw new InvalidOperationException("AzureOpenAI:DeploymentName is not configured.");

        _chatClient = new AzureOpenAIClient(new Uri(openAiEndpoint), credential)
            .GetChatClient(deploymentName);

        _chatHistoryProviderType = configuration["ChatHistory:Provider"] ?? "Cosmos";

        if (_chatHistoryProviderType.Equals("TableStorage", StringComparison.OrdinalIgnoreCase))
        {
            var tableEndpoint = configuration["TableStorage:Endpoint"]
                ?? throw new InvalidOperationException("TableStorage:Endpoint is not configured.");
            var tableName = configuration["TableStorage:TableName"]
                ?? throw new InvalidOperationException("TableStorage:TableName is not configured.");

            _tableClient = new TableClient(new Uri(tableEndpoint), tableName, credential);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ChatService: Using Table Storage chat history.");
        }
        else
        {
            var cosmosEndpoint = configuration["CosmosDb:Endpoint"]
                ?? throw new InvalidOperationException("CosmosDb:Endpoint is not configured.");
            _cosmosDatabaseId = configuration["CosmosDb:DatabaseId"]
                ?? throw new InvalidOperationException("CosmosDb:DatabaseId is not configured.");
            _cosmosContainerId = configuration["CosmosDb:ContainerId"]
                ?? throw new InvalidOperationException("CosmosDb:ContainerId is not configured.");

            _cosmosClient = new CosmosClient(cosmosEndpoint, credential);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ChatService: Using Cosmos DB chat history.");
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ChatService: Initialized.");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Not using trimming")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Not using NativeAOT")]
    public async Task<string> GetResponseAsync(
        string conversationId,
        string userQuery,
        IReadOnlyList<SearchResultContext> searchResults)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Chat: Setting up agent for conversation '{conversationId}'...");

        Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>> storeFilter = messages => messages.Where(m =>
            m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.AIContextProvider &&
            m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.ChatHistory);

        ChatHistoryProvider chatHistoryProvider = _chatHistoryProviderType.Equals("TableStorage", StringComparison.OrdinalIgnoreCase)
            ? new TableStorageChatHistoryProvider(
                _tableClient!,
                conversationId,
                storeInputMessageFilter: storeFilter)
            : new CosmosChatHistoryProvider(
                _cosmosClient!,
                _cosmosDatabaseId!,
                _cosmosContainerId!,
                _ => new CosmosChatHistoryProvider.State(conversationId),
                ownsClient: false,
                storeInputMessageFilter: storeFilter);

        // Create a search-context provider that injects RAG results without persisting them.
        var searchContextProvider = new SearchResultsContextProvider(searchResults);

        // Create a lightweight per-request agent (ChatClient and shared clients are singletons).
        AIAgent agent = _chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "TechManualAssistant",
            ChatOptions = new ChatOptions { Instructions = SystemPrompt },
            ChatHistoryProvider = chatHistoryProvider,
            AIContextProviders = [searchContextProvider]
        });

        var session = await agent.CreateSessionAsync();

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Chat: Running agent...");

        var response = await agent.RunAsync(userQuery, session);

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Chat: Response complete.");

        return response.Text;
    }

    public void Dispose()
    {
        _cosmosClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public record SearchResultContext(string DocumentTitle, string ContentText);

/// <summary>
/// Injects search results into the agent context as ephemeral messages.
/// These messages are excluded from chat history storage via the storeInputMessageFilter.
/// </summary>
internal sealed class SearchResultsContextProvider : MessageAIContextProvider
{
    private readonly IReadOnlyList<SearchResultContext> _searchResults;

    public SearchResultsContextProvider(IReadOnlyList<SearchResultContext> searchResults)
    {
        _searchResults = searchResults;
    }

    protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Search Results\n");
        for (var i = 0; i < _searchResults.Count; i++)
        {
            var r = _searchResults[i];
            sb.AppendLine($"### Source {i + 1}: {r.DocumentTitle}");
            sb.AppendLine(r.ContentText);
            sb.AppendLine();
        }

        IEnumerable<ChatMessage> messages = [new ChatMessage(ChatRole.User, sb.ToString())];
        return new ValueTask<IEnumerable<ChatMessage>>(messages);
    }
}
