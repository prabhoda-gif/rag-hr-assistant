namespace RagApi.Services.Agent;

// =============================================================================
// AgentTool — describes a tool the agent can call
//
// LEARNING NOTE — OpenAI Function Calling
// When we send tools to the OpenAI API, each tool needs:
//   - name:        what the LLM calls it (e.g. "search_hr_docs")
//   - description: plain English so the LLM knows WHEN to use it
//   - parameters:  JSON Schema so the LLM knows WHAT arguments to pass
//
// The LLM reads these descriptions and decides which tool (if any) to call.
// This is the "brain" of the agent — it reasons about which tool fits the question.
// =============================================================================

public class AgentTool
{
    public string Name        { get; init; } = "";
    public string Description { get; init; } = "";
    public object Parameters  { get; init; } = new { };
}

// Result returned after a tool executes
public class ToolResult
{
    public string ToolName { get; init; } = "";
    public string Output   { get; init; } = "";
    public bool   IsError  { get; init; }
}

// One step in the agent's reasoning loop — shown in the UI
public class AgentStep
{
    public string StepType { get; init; } = ""; // "thought", "tool_call", "observation", "answer"
    public string Content  { get; init; } = "";
    public string? ToolName { get; init; }
}
