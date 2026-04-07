using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using OpenAI;
using OpenAI.Chat;

namespace Example.ChatApi.Services;

public class ChatService
{
    private readonly DefaultAzureCredential _credential;
    private readonly string _foundryEndpoint;
    private readonly string _modelName;
    private readonly TableClient _tableClient;
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _conversations = new();

    private static readonly string[] s_tokenScopes = ["https://ai.azure.com/.default"];
    private AccessToken _cachedToken;
    private ChatClient? _chatClient;
    private readonly object _clientLock = new();

    private const string SystemPrompt =
        "You are a helpful conversational assistant. Provide clear and concise responses. Format responses in Markdown when appropriate.";

    public ChatService(IConfiguration configuration)
    {
        _credential = new DefaultAzureCredential();

        _foundryEndpoint = configuration["Foundry:Endpoint"]
            ?? throw new InvalidOperationException("Foundry:Endpoint is not configured.");
        _modelName = configuration["Foundry:ModelName"]
            ?? throw new InvalidOperationException("Foundry:ModelName is not configured.");

        // Warm up the client
        _chatClient = CreateChatClient();

        var tableEndpoint = configuration["TableStorage:Endpoint"]
            ?? throw new InvalidOperationException("TableStorage:Endpoint is not configured.");
        var tableName = configuration["TableStorage:TableName"]
            ?? throw new InvalidOperationException("TableStorage:TableName is not configured.");

        _tableClient = new TableClient(new Uri(tableEndpoint), tableName, _credential);
        _tableClient.CreateIfNotExists();

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ChatService: Initialized with Foundry endpoint and Table Storage history.");
    }

    private ChatClient GetChatClient()
    {
        lock (_clientLock)
        {
            // Refresh client if token expires within 5 minutes
            if (_chatClient == null || _cachedToken.ExpiresOn < DateTimeOffset.UtcNow.AddMinutes(5))
            {
                _chatClient = CreateChatClient();
            }
            return _chatClient;
        }
    }

    private ChatClient CreateChatClient()
    {
        _cachedToken = _credential.GetToken(new TokenRequestContext(s_tokenScopes), default);
        var apiCredential = new System.ClientModel.ApiKeyCredential(_cachedToken.Token);
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri($"{_foundryEndpoint.TrimEnd('/')}/openai/v1")
        };
        return new OpenAIClient(apiCredential, options).GetChatClient(_modelName);
    }

    public async Task<string> GetResponseAsync(string conversationId, string userMessage)
    {
        // Load history from Table Storage on first access for this conversation
        var history = _conversations.GetOrAdd(conversationId, id => LoadHistory(id));

        lock (history)
        {
            history.Add(new UserChatMessage(userMessage));
        }

        ChatMessage[] snapshot;
        lock (history)
        {
            snapshot = [.. history];
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Chat [{conversationId[..8]}]: Sending {snapshot.Length} messages...");

        ChatCompletion completion = await GetChatClient().CompleteChatAsync(snapshot);
        var assistantText = completion.Content[0].Text;

        lock (history)
        {
            history.Add(new AssistantChatMessage(assistantText));
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Chat [{conversationId[..8]}]: Response complete ({assistantText.Length} chars).");

        // Persist the new user + assistant turn to Table Storage
        await PersistTurnAsync(conversationId, userMessage, assistantText);

        return assistantText;
    }

    private List<ChatMessage> LoadHistory(string conversationId)
    {
        var messages = new List<ChatMessage> { new SystemChatMessage(SystemPrompt) };

        try
        {
            var filter = $"PartitionKey eq '{conversationId}'";
            foreach (var entity in _tableClient.Query<TableEntity>(filter))
            {
                var role = entity.GetString("Role");
                var text = entity.GetString("Content");
                if (role == "user")
                    messages.Add(new UserChatMessage(text));
                else if (role == "assistant")
                    messages.Add(new AssistantChatMessage(text));
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Chat [{conversationId[..8]}]: Loaded {messages.Count - 1} messages from history.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Chat [{conversationId[..8]}]: Failed to load history: {ex.Message}");
        }

        return messages;
    }

    private async Task PersistTurnAsync(string conversationId, string userMessage, string assistantMessage)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var userEntity = new TableEntity(conversationId, $"{timestamp:D20}_user")
            {
                ["Role"] = "user",
                ["Content"] = userMessage,
                ["CreatedAt"] = DateTimeOffset.UtcNow
            };

            var assistantEntity = new TableEntity(conversationId, $"{timestamp + 1:D20}_assistant")
            {
                ["Role"] = "assistant",
                ["Content"] = assistantMessage,
                ["CreatedAt"] = DateTimeOffset.UtcNow
            };

            var batch = new List<TableTransactionAction>
            {
                new(TableTransactionActionType.Add, userEntity),
                new(TableTransactionActionType.Add, assistantEntity)
            };

            await _tableClient.SubmitTransactionAsync(batch);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Chat [{conversationId[..8]}]: Failed to persist history: {ex.Message}");
        }
    }
}
