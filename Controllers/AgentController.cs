using Microsoft.AspNetCore.Mvc;
using RagApi.Services.Agent;
using System.Text;
using System.Text.Json;

namespace RagApi.Controllers;

// =============================================================================
// AgentController — POST /api/agent/chat
//
// LEARNING NOTE — Same SSE streaming pattern as AskController
// The agent runs asynchronously and emits steps one at a time.
// Each step (tool_call, observation, answer) is sent as an SSE event.
// The React UI reads these events and renders them live as the agent works.
//
// Event format:
//   data: {"stepType":"tool_call","toolName":"search_hr_docs","content":"..."}
//   data: {"stepType":"observation","toolName":"search_hr_docs","content":"..."}
//   data: {"stepType":"answer","content":"..."}
//   data: [DONE]
// =============================================================================

[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private readonly IAgentService _agent;

    public AgentController(IAgentService agent) => _agent = agent;

    [HttpPost("chat")]
    public async Task Chat([FromBody] AgentRequest request)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        await foreach (var step in _agent.RunAsync(request.Question, request.UserId))
        {
            var json = JsonSerializer.Serialize(step, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var line = Encoding.UTF8.GetBytes($"data: {json}\n\n");
            await Response.Body.WriteAsync(line);
            await Response.Body.FlushAsync();
        }

        var done = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
        await Response.Body.WriteAsync(done);
        await Response.Body.FlushAsync();
    }
}

public record AgentRequest(string Question, string UserId);
