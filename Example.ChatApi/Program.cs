using Example.ChatApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Allow CORS from the frontend dev server and deployed SWA
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                  "http://localhost:5173",
                  "https://app-pseg-main-eus2-mx01.azurewebsites.net")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<SearchService>();
builder.Services.AddSingleton<ChatService>();

var app = builder.Build();

app.UseCors();

app.MapPost("/api/chat", async (ChatRequest request, SearchService searchService, ChatService chatService) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] POST /api/chat: query='{request.Query}'");

    var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();

    // Perform hybrid search against Azure AI Search
    var searchResults = await searchService.HybridSearchAsync(request.Query);
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Search complete. Got {searchResults.Count} results.");

    // Build search result context for the LLM
    var searchContext = searchResults.Select(doc =>
    {
        var title = doc.TryGetValue("document_title", out var t) ? t?.ToString() ?? "Untitled" : "Untitled";
        var content = doc.TryGetValue("content_text", out var c) ? c?.ToString() ?? "" : "";
        return new SearchResultContext(title, content);
    }).ToList();

    // Get the full agent response
    var answer = await chatService.GetResponseAsync(conversationId, request.Query, searchContext);
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Response complete ({answer.Length} chars).");

    return Results.Ok(new ChatResponse(answer, conversationId));
});

app.Run();

public record ChatRequest(string Query, string? ConversationId);
public record ChatResponse(string Message, string ConversationId);
