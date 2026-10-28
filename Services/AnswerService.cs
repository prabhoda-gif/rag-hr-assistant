using Microsoft.Extensions.AI;
using OpenAI;
using RagApi.Models;

namespace RagApi.Services;

public interface IAnswerService
{
    // Rewrites a follow-up question into a standalone question using history
    Task<string> RewriteQuestionAsync(string question, List<ChatMessage> history);

    Task<string> AnswerAsync(
        string question, List<ChunkResult> chunks, List<ChatMessage> history);

    IAsyncEnumerable<string> AnswerStreamingAsync(
        string question, List<ChunkResult> chunks, List<ChatMessage> history);
}

public class AnswerService : IAnswerService
{
    private readonly IChatClient _chatClient;

    public AnswerService(IConfiguration config)
    {
        string apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey not configured");

        _chatClient = new OpenAIClient(apiKey)
            .GetChatClient("gpt-4o-mini")
            .AsIChatClient();
    }

    public async Task<string> RewriteQuestionAsync(string question, List<ChatMessage> history)
    {
        // If there's no history, the question is already standalone — no rewrite needed
        if (history.Count == 0) return question;

        // Build a short conversation summary for the rewrite prompt
        var historyText = new System.Text.StringBuilder();
        foreach (var msg in history.TakeLast(4))  // last 2 exchanges is enough
            historyText.AppendLine($"{msg.Role}: {msg.Text}");

        var rewriteMessages = new List<ChatMessage>
        {
            new(ChatRole.System, """
                Given the conversation history and a follow-up question,
                rewrite the follow-up into a single standalone question
                that can be understood without the history.
                Return ONLY the rewritten question, nothing else.
                If the question is already standalone, return it unchanged.
                """),
            new(ChatRole.User, $"""
                Conversation history:
                {historyText}

                Follow-up question: {question}
                """)
        };

        var response = await _chatClient.GetResponseAsync(rewriteMessages);
        string rewritten = response.Text?.Trim() ?? question;
        return rewritten;
    }

    public async Task<string> AnswerAsync(
        string question, List<ChunkResult> chunks, List<ChatMessage> history)
    {
        var messages = BuildMessages(question, chunks, history);
        var response = await _chatClient.GetResponseAsync(messages);
        return response.Text ?? string.Empty;
    }

    public async IAsyncEnumerable<string> AnswerStreamingAsync(
        string question, List<ChunkResult> chunks, List<ChatMessage> history)
    {
        var messages = BuildMessages(question, chunks, history);

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages))
        {
            if (update.Text is not null)
                yield return update.Text;
        }
    }

    private static List<ChatMessage> BuildMessages(
        string question, List<ChunkResult> chunks, List<ChatMessage> history)
    {
        // Build the context block from retrieved chunks
        var context = new System.Text.StringBuilder();
        foreach (var chunk in chunks)
        {
            context.AppendLine($"--- [{chunk.FileName}, chunk {chunk.ChunkIndex}] ---");
            context.AppendLine(chunk.ChunkText);
            context.AppendLine();
        }

        // System prompt is always first — the guardrail
        string system = $"""
            You are a helpful assistant answering questions about company HR policies.
            Answer ONLY using the context provided below.
            If the answer is not clearly stated in the context, say "I don't know."
            Keep your answer concise and factual.
            You may use the conversation history to understand follow-up questions.

            Context:
            {context}
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, system)
        };

        // Inject conversation history AFTER system prompt
        // This gives the LLM memory of previous exchanges
        messages.AddRange(history);

        // Add the current question last
        messages.Add(new ChatMessage(ChatRole.User, question));

        return messages;
    }
}
