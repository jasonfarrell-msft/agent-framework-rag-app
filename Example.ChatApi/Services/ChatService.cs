using System.Diagnostics.CodeAnalysis;
using System.Text;
using Azure.AI.OpenAI;
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
    private readonly CosmosClient _cosmosClient;
    private readonly string _databaseId;
    private readonly string _containerId;

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

        var credential = new DefaultAzureCredential();

        var openAiEndpoint = configuration["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is not configured.");
        var deploymentName = configuration["AzureOpenAI:DeploymentName"]
            ?? throw new InvalidOperationException("AzureOpenAI:DeploymentName is not configured.");
        var cosmosEndpoint = configuration["CosmosDb:Endpoint"]
            ?? throw new InvalidOperationException("CosmosDb:Endpoint is not configured.");
        _databaseId = configuration["CosmosDb:DatabaseId"]
            ?? throw new InvalidOperationException("CosmosDb:DatabaseId is not configured.");
        _containerId = configuration["CosmosDb:ContainerId"]
            ?? throw new InvalidOperationException("CosmosDb:ContainerId is not configured.");

        _chatClient = new AzureOpenAIClient(new Uri(openAiEndpoint), credential)
            .GetChatClient(deploymentName);

        _cosmosClient = new CosmosClient(cosmosEndpoint, credential);

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

        // Create per-request CosmosChatHistoryProvider keyed to this conversationId.
        // The shared CosmosClient is reused across requests (ownsClient: false).
        var chatHistoryProvider = new CosmosChatHistoryProvider(
            _cosmosClient,
            _databaseId,
            _containerId,
            _ => new CosmosChatHistoryProvider.State(conversationId),
            ownsClient: false,
            storeInputMessageFilter: messages => messages.Where(m =>
                m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.AIContextProvider &&
                m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.ChatHistory));

        // Create a search-context provider that injects RAG results without persisting them.
        var searchContextProvider = new SearchResultsContextProvider(searchResults);

        // Create a lightweight per-request agent (ChatClient and CosmosClient are shared singletons).
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
