namespace RagApi.Services.Agent;

// =============================================================================
// GetLeaveBalanceTool — returns the remaining leave days for a user
//
// LEARNING NOTE — Simulated Tool / Stub
// In a real system this would query an HR database (SAP, Workday, etc.).
// For this learning project we return realistic fake data so the agent
// demo works end-to-end without needing an HR system integration.
//
// This is a common interview technique: stub external dependencies to show
// the architecture works, note clearly that it's a stub, explain how
// you'd replace it with a real integration.
// =============================================================================

public class GetLeaveBalanceTool
{
    // Simulated leave balances — keyed by userId
    private static readonly Dictionary<string, LeaveBalance> _balances = new()
    {
        ["alice"] = new LeaveBalance { Annual = 12, Sick = 8, Casual = 3 },
        ["bob"]   = new LeaveBalance { Annual = 5,  Sick = 10, Casual = 1 },
        ["carol"] = new LeaveBalance { Annual = 20, Sick = 12, Casual = 5 },
    };

    public static AgentTool Definition => new()
    {
        Name        = "get_leave_balance",
        Description = "Get the remaining leave balance for an employee. Returns how many annual, sick, and casual leave days they have left this year.",
        Parameters  = new
        {
            type = "object",
            properties = new
            {
                user_id = new
                {
                    type        = "string",
                    description = "The employee's user ID"
                }
            },
            required = new[] { "user_id" }
        }
    };

    public Task<ToolResult> ExecuteAsync(string userId)
    {
        if (_balances.TryGetValue(userId.ToLowerInvariant(), out var balance))
        {
            var output = $"Leave balance for {userId}:\n" +
                         $"  Annual leave:  {balance.Annual} days remaining\n" +
                         $"  Sick leave:    {balance.Sick} days remaining\n" +
                         $"  Casual leave:  {balance.Casual} days remaining";

            return Task.FromResult(new ToolResult { ToolName = "get_leave_balance", Output = output });
        }

        return Task.FromResult(new ToolResult
        {
            ToolName = "get_leave_balance",
            Output   = $"No leave balance found for user '{userId}'. Using default: 15 annual, 10 sick, 5 casual days."
        });
    }

    private record LeaveBalance
    {
        public int Annual  { get; init; }
        public int Sick    { get; init; }
        public int Casual  { get; init; }
    }
}
