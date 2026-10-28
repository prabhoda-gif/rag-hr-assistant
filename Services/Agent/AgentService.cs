using System.Text.Json;
using System.Text.Json.Nodes;
using OpenAI;
using OpenAI.Chat;

namespace RagApi.Services.Agent;

// =============================================================================
// AgentService — the ReAct loop
//
// LEARNING NOTE — ReAct Pattern (Reason + Act)
// The agent runs in a loop:
//
//   1. Send messages + available tools to LLM
//   2. LLM responds with EITHER:
//      a) A tool_call  → we run the tool, add the result, go back to step 1
//      b) A text reply → this is the final answer, loop ends
//
// Each iteration the LLM can "see" all previous tool calls and their results,
// so it builds up context as it reasons through multi-step problems.
//
// Safety: we cap iterations at MaxIterations to prevent infinite loops.
// This is a standard guard in all production agent implementations.
//
// LEARNING NOTE — OpenAI SDK vs Microsoft.Extensions.AI
// We use the raw OpenAI SDK here (not IChatClient) because:
//   - Tool/function calling requires accessing ToolCalls on the response
//   - The Microsoft.Extensions.AI abstraction doesn't expose ToolCalls yet
//   - Using the native SDK gives us full control over the agentic loop
// =============================================================================

public interface IAgentService
{
    IAsyncEnumerable<AgentStep> RunAsync(string question, string userId);
}

public class AgentService : IAgentService
{
    private const int MaxIterations = 6;  // prevent runaway loops

    private readonly ChatClient            _chatClient;
    private readonly SearchHrDocsTool      _searchTool;
    private readonly GetLeaveBalanceTool   _balanceTool;
    private readonly CalculateDaysTool     _calcTool;

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public AgentService(
        IConfiguration       config,
        IEmbeddingService    embedding,
        IRetrievalService    retrieval)
    {
        string apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey not configured");

        _chatClient  = new OpenAIClient(apiKey).GetChatClient("gpt-4o-mini");
        _searchTool  = new SearchHrDocsTool(embedding, retrieval);
        _balanceTool = new GetLeaveBalanceTool();
        _calcTool    = new CalculateDaysTool();
    }

    public async IAsyncEnumerable<AgentStep> RunAsync(string question, string userId)
    {
        // ── Tool definitions sent to OpenAI ───────────────────────────────────
        var tools = new List<ChatTool>
        {
            BuildTool(SearchHrDocsTool.Definition),
            BuildTool(GetLeaveBalanceTool.Definition),
            BuildTool(CalculateDaysTool.Definition),
        };

        // ── Conversation history for this agent run ───────────────────────────
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("""
                You are an HR assistant for a company. Your ONLY job is to answer questions
                about company HR policies, leave, benefits, and procedures.

                Rules you MUST follow:
                1. ALWAYS use the search_hr_docs tool before answering any HR question.
                2. For leave requests, ALSO call get_leave_balance and calculate_working_days.
                3. If the question is NOT related to HR, leave, benefits, or company policies,
                   respond with: "I can only answer questions about company HR policies and leave.
                   Please ask your question to a general assistant."
                4. Never answer from your own knowledge — only use what the tools return.
                """),
            new UserChatMessage(question)
        };

        // ── ReAct loop ────────────────────────────────────────────────────────
        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            var options = new ChatCompletionOptions();
            foreach (var tool in tools)
                options.Tools.Add(tool);

            var response = await _chatClient.CompleteChatAsync(messages, options);
            var choice   = response.Value;

            // ── Case A: LLM wants to call a tool ──────────────────────────────
            if (choice.FinishReason == ChatFinishReason.ToolCalls)
            {
                // Add the assistant's tool-call message to history
                messages.Add(new AssistantChatMessage(choice));

                foreach (var toolCall in choice.ToolCalls)
                {
                    // Stream a "tool_call" step to the UI
                    yield return new AgentStep
                    {
                        StepType = "tool_call",
                        Content  = $"Calling {toolCall.FunctionName}({toolCall.FunctionArguments})",
                        ToolName = toolCall.FunctionName
                    };

                    // Execute the tool
                    var result = await DispatchToolAsync(toolCall.FunctionName,
                                                         toolCall.FunctionArguments.ToString(),
                                                         userId);

                    // Stream an "observation" step to the UI
                    yield return new AgentStep
                    {
                        StepType = "observation",
                        Content  = result.Output,
                        ToolName = toolCall.FunctionName
                    };

                    // Add tool result to history so LLM sees it next iteration
                    messages.Add(new ToolChatMessage(toolCall.Id, result.Output));
                }

                continue;  // next iteration — LLM will reason about the tool results
            }

            // ── Case B: LLM gave a final text answer ──────────────────────────
            yield return new AgentStep
            {
                StepType = "answer",
                Content  = choice.Content[0].Text
            };
            yield break;
        }

        // Safety: if we hit MaxIterations without a final answer
        yield return new AgentStep
        {
            StepType = "answer",
            Content  = "I was unable to find a complete answer after checking all available sources."
        };
    }

    // ── Route tool name → implementation ─────────────────────────────────────
    private async Task<ToolResult> DispatchToolAsync(
        string toolName, string argumentsJson, string userId)
    {
        try
        {
            var args = JsonNode.Parse(argumentsJson);

            return toolName switch
            {
                "search_hr_docs" =>
                    await _searchTool.ExecuteAsync(
                        args!["query"]!.GetValue<string>(), userId),

                "get_leave_balance" =>
                    await _balanceTool.ExecuteAsync(
                        args!["user_id"]!.GetValue<string>()),

                "calculate_working_days" =>
                    await _calcTool.ExecuteAsync(
                        args!["start_date"]!.GetValue<string>(),
                        args!["end_date"]!.GetValue<string>()),

                _ => new ToolResult
                {
                    ToolName = toolName,
                    Output   = $"Unknown tool: {toolName}",
                    IsError  = true
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolName = toolName,
                Output   = $"Tool execution failed: {ex.Message}",
                IsError  = true
            };
        }
    }

    // ── Convert our AgentTool definition to OpenAI's ChatTool format ──────────
    private static ChatTool BuildTool(AgentTool tool)
    {
        string parametersJson = JsonSerializer.Serialize(tool.Parameters);
        return ChatTool.CreateFunctionTool(
            functionName:        tool.Name,
            functionDescription: tool.Description,
            functionParameters:  BinaryData.FromString(parametersJson));
    }
}
