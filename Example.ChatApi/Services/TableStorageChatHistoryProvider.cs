using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Example.ChatApi.Services;

/// <summary>
/// Azure Table Storage implementation of <see cref="ChatHistoryProvider"/>.
/// Stores chat messages with fields broken out into individual columns for
/// readability and queryability, plus a full JSON column for lossless reconstruction.
/// 
/// Key structure: PartitionKey = conversationId, RowKey = {zeroPaddedTimestamp}_{guid}.
/// </summary>
[RequiresUnreferencedCode("Uses JSON serialization which is incompatible with trimming.")]
[RequiresDynamicCode("Uses JSON serialization which is incompatible with NativeAOT.")]
public sealed class TableStorageChatHistoryProvider : ChatHistoryProvider
{
    private readonly TableClient _tableClient;
    private readonly string _conversationId;
    private readonly string _stateKey;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
#if NET9_0_OR_GREATER
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
#endif
    };

    /// <summary>
    /// Gets or sets the maximum number of messages to retrieve.
    /// Default is null (no limit).
    /// </summary>
    public int? MaxMessagesToRetrieve { get; set; }

    /// <inheritdoc />
    public override string StateKey => _stateKey;

    /// <summary>
    /// Creates a new <see cref="TableStorageChatHistoryProvider"/>.
    /// </summary>
    /// <param name="tableClient">A pre-configured <see cref="TableClient"/>.</param>
    /// <param name="conversationId">The conversation ID to scope messages to.</param>
    /// <param name="stateKey">Optional state key override.</param>
    /// <param name="provideOutputMessageFilter">Optional filter applied when retrieving messages.</param>
    /// <param name="storeInputMessageFilter">Optional filter applied before storing messages.</param>
    public TableStorageChatHistoryProvider(
        TableClient tableClient,
        string conversationId,
        string? stateKey = null,
        Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>>? provideOutputMessageFilter = null,
        Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>>? storeInputMessageFilter = null)
        : base(provideOutputMessageFilter, storeInputMessageFilter)
    {
        _tableClient = tableClient ?? throw new ArgumentNullException(nameof(tableClient));
        _conversationId = conversationId ?? throw new ArgumentNullException(nameof(conversationId));
        _stateKey = stateKey ?? GetType().Name;
    }

    /// <summary>
    /// Creates a new <see cref="TableStorageChatHistoryProvider"/> using an endpoint URI and <see cref="DefaultAzureCredential"/>.
    /// </summary>
    public TableStorageChatHistoryProvider(
        Uri tableEndpoint,
        string tableName,
        string conversationId,
        string? stateKey = null,
        Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>>? provideOutputMessageFilter = null,
        Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>>? storeInputMessageFilter = null)
        : this(
            new TableClient(tableEndpoint, tableName, new DefaultAzureCredential()),
            conversationId,
            stateKey,
            provideOutputMessageFilter,
            storeInputMessageFilter)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        // Query all messages for this conversation, ordered by RowKey (timestamp-based).
        var filter = $"PartitionKey eq '{EscapeODataString(_conversationId)}'";

        var messages = new List<ChatMessage>();

        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
            filter: filter,
            cancellationToken: cancellationToken))
        {
            var chatMessage = DeserializeEntity(entity);
            if (chatMessage != null)
            {
                messages.Add(chatMessage);
            }
        }

        // Table Storage returns entities ordered by PartitionKey then RowKey (ascending).
        // Our RowKey format (zeroPaddedTimestamp_guid) ensures chronological order.

        if (MaxMessagesToRetrieve.HasValue && messages.Count > MaxMessagesToRetrieve.Value)
        {
            // Keep only the most recent N messages.
            messages = messages.Skip(messages.Count - MaxMessagesToRetrieve.Value).ToList();
        }

        return messages;
    }

    /// <inheritdoc />
    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        var messageList = context.RequestMessages
            .Concat(context.ResponseMessages ?? [])
            .ToList();

        if (messageList.Count == 0)
        {
            return;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Table Storage batch operations require all entities share the same PartitionKey
        // and are limited to 100 entities per batch.
        const int maxBatchSize = 100;

        for (int i = 0; i < messageList.Count; i += maxBatchSize)
        {
            var batch = new List<TableTransactionAction>();

            foreach (var message in messageList.Skip(i).Take(maxBatchSize))
            {
                var entity = SerializeToEntity(message, timestamp);
                batch.Add(new TableTransactionAction(TableTransactionActionType.Add, entity));
                timestamp++; // Increment to preserve ordering within the same millisecond.
            }

            await _tableClient.SubmitTransactionAsync(batch, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Serializes a <see cref="ChatMessage"/> into a <see cref="TableEntity"/> with broken-out columns.
    /// </summary>
    private TableEntity SerializeToEntity(ChatMessage message, long timestamp)
    {
        var rowKey = $"{timestamp:D20}_{Guid.NewGuid():N}";

        var entity = new TableEntity(_conversationId, rowKey)
        {
            ["Role"] = message.Role.Value,
            ["MessageId"] = message.MessageId,
            ["AuthorName"] = message.AuthorName,
            ["TextContent"] = ExtractTextContent(message),
            ["CreatedAt"] = DateTimeOffset.UtcNow,
            // Full-fidelity JSON for lossless reconstruction (handles tool calls, images, etc.)
            ["MessageJson"] = JsonSerializer.Serialize(message, s_jsonOptions),
        };

        return entity;
    }

    /// <summary>
    /// Deserializes a <see cref="TableEntity"/> back into a <see cref="ChatMessage"/>.
    /// Uses the full-fidelity <c>MessageJson</c> column for lossless reconstruction.
    /// Falls back to broken-out columns if JSON is missing.
    /// </summary>
    private static ChatMessage? DeserializeEntity(TableEntity entity)
    {
        var json = entity.GetString("MessageJson");

        if (!string.IsNullOrEmpty(json))
        {
            return JsonSerializer.Deserialize<ChatMessage>(json, s_jsonOptions);
        }

        // Fallback: reconstruct from broken-out columns.
        var role = entity.GetString("Role");
        var text = entity.GetString("TextContent");

        if (string.IsNullOrEmpty(role))
        {
            return null;
        }

        var msg = new ChatMessage(new ChatRole(role), text);
        msg.MessageId = entity.GetString("MessageId");
        msg.AuthorName = entity.GetString("AuthorName");
        return msg;
    }

    /// <summary>
    /// Extracts the concatenated plain-text content from a <see cref="ChatMessage"/>.
    /// </summary>
    private static string? ExtractTextContent(ChatMessage message)
    {
        if (message.Contents is null || message.Contents.Count == 0)
        {
            return message.Text;
        }

        var textParts = message.Contents
            .OfType<TextContent>()
            .Select(t => t.Text)
            .Where(t => t is not null);

        var joined = string.Join("\n", textParts);
        return string.IsNullOrEmpty(joined) ? null : joined;
    }

    /// <summary>
    /// Escapes a string for use in an OData filter expression to prevent injection.
    /// </summary>
    private static string EscapeODataString(string value)
    {
        return value.Replace("'", "''");
    }
}
