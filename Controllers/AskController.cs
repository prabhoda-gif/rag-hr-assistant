using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using RagApi.Models;
using RagApi.Services;

namespace RagApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AskController : ControllerBase
{
    private readonly IEmbeddingService     _embedder;
    private readonly IRetrievalService     _retrieval;
    private readonly IAnswerService        _answer;
    private readonly IConversationStore    _conversations;
    private readonly ILogger<AskController> _logger;

    public AskController(
        IEmbeddingService  embedder,
        IRetrievalService  retrieval,
        IAnswerService     answer,
        IConversationStore conversations,
        ILogger<AskController> logger)
    {
        _embedder      = embedder;
        _retrieval     = retrieval;
        _answer        = answer;
        _conversations = conversations;
        _logger        = logger;
    }

    [HttpPost]
    public async Task<ActionResult<AskResponse>> Ask([FromBody] AskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest("Question is required.");

        _logger.LogInformation("[{User}] [{Conv}] {Question}",
            request.UserId, request.ConversationId, request.Question);

        var    history  = _conversations.GetHistory(request.ConversationId);
        // Rewrite follow-up questions into standalone questions before embedding
        string queryText = await _answer.RewriteQuestionAsync(request.Question, history);
        if (queryText != request.Question)
            _logger.LogInformation("Rewritten query: {Query}", queryText);

        // If hybrid retrieval is active, pass the question text for keyword search
        if (_retrieval is HybridRetrievalService hybrid)
            hybrid.SetQuestion(queryText);

        float[] vector  = await _embedder.EmbedAsync(queryText);
        var     chunks  = await _retrieval.RetrieveAsync(vector, request.UserId);

        if (chunks.Count == 0)
        {
            // Still save the question to history even if no answer
            _conversations.AddMessage(request.ConversationId,
                new ChatMessage(ChatRole.User, request.Question));
            _conversations.AddMessage(request.ConversationId,
                new ChatMessage(ChatRole.Assistant, "I don't know."));

            return Ok(new AskResponse
            {
                Answer         = "I don't know — no relevant information found in the documents.",
                Sources        = [],
                ConversationId = request.ConversationId
            });
        }

        string answerText = await _answer.AnswerAsync(request.Question, chunks, history);

        // Save this exchange to conversation history
        _conversations.AddMessage(request.ConversationId,
            new ChatMessage(ChatRole.User, request.Question));
        _conversations.AddMessage(request.ConversationId,
            new ChatMessage(ChatRole.Assistant, answerText));

        return Ok(new AskResponse
        {
            Answer         = answerText,
            Sources        = chunks,
            ConversationId = request.ConversationId
        });
    }

    // Streaming endpoint with conversation memory
    [HttpPost("stream")]
    public async Task AskStreaming([FromBody] AskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            Response.StatusCode = 400;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        var     history   = _conversations.GetHistory(request.ConversationId);
        string  queryText = await _answer.RewriteQuestionAsync(request.Question, history);

        // If hybrid retrieval is active, pass the question text for keyword search
        if (_retrieval is HybridRetrievalService hybrid2)
            hybrid2.SetQuestion(queryText);

        float[] vector    = await _embedder.EmbedAsync(queryText);
        var     chunks    = await _retrieval.RetrieveAsync(vector, request.UserId);

        if (chunks.Count == 0)
        {
            _conversations.AddMessage(request.ConversationId,
                new ChatMessage(ChatRole.User, request.Question));
            _conversations.AddMessage(request.ConversationId,
                new ChatMessage(ChatRole.Assistant, "I don't know."));

            await Response.WriteAsync("data: I don't know — no relevant information found.\n\n");
            await Response.WriteAsync("data: [DONE]\n\n");
            return;
        }

        // Stream the answer token by token
        var fullAnswer = new System.Text.StringBuilder();

        await foreach (var token in _answer.AnswerStreamingAsync(request.Question, chunks, history))
        {
            fullAnswer.Append(token);
            await Response.WriteAsync($"data: {token}\n\n");
            await Response.Body.FlushAsync();
        }

        // Save complete exchange to history
        _conversations.AddMessage(request.ConversationId,
            new ChatMessage(ChatRole.User, request.Question));
        _conversations.AddMessage(request.ConversationId,
            new ChatMessage(ChatRole.Assistant, fullAnswer.ToString()));

        // Send sources and done signal
        var sourcesJson = System.Text.Json.JsonSerializer.Serialize(chunks);
        await Response.WriteAsync($"event: sources\ndata: {sourcesJson}\n\n");
        await Response.WriteAsync("data: [DONE]\n\n");
    }

    // Clear conversation history — called when user starts a new chat
    [HttpDelete("{conversationId}")]
    public IActionResult ClearConversation(string conversationId)
    {
        _conversations.Clear(conversationId);
        return Ok();
    }
}
