using Example.ChatApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<ChatService>();

var app = builder.Build();

app.UseCors();

app.MapPost("/api/chat", async (ChatRequest request, ChatService chatService) =>
{
    var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();

    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] POST /api/chat: conversation={conversationId[..8]}, query='{request.Query}'");

    var answer = await chatService.GetResponseAsync(conversationId, request.Query);

    return Results.Ok(new ChatResponse(answer, conversationId));
});

app.Run();

public record ChatRequest(string Query, string? ConversationId);
public record ChatResponse(string Message, string ConversationId);
