namespace RagApi.Services.Agent;

// =============================================================================
// CalculateDaysTool — counts working days between two dates
//
// LEARNING NOTE — Why include a calculation tool?
// This demonstrates that agent tools don't have to call external systems.
// A tool can be pure logic. The LLM can't reliably do date arithmetic,
// but it CAN call a tool that does — and trust the result.
//
// Example question that needs this tool:
//   "I want to take leave from Dec 20 to Jan 3. How many working days is that?"
// The agent will:
//   1. Call calculate_days to count the working days
//   2. Call get_leave_balance to check if the user has enough
//   3. Call search_hr_docs to check the leave application rules
//   4. Synthesize a final answer
// =============================================================================

public class CalculateDaysTool
{
    public static AgentTool Definition => new()
    {
        Name        = "calculate_working_days",
        Description = "Calculate the number of working days (Monday–Friday, excluding weekends) between two dates. Use this when the user wants to know how many leave days a date range covers.",
        Parameters  = new
        {
            type = "object",
            properties = new
            {
                start_date = new
                {
                    type        = "string",
                    description = "Start date in YYYY-MM-DD format (inclusive)"
                },
                end_date = new
                {
                    type        = "string",
                    description = "End date in YYYY-MM-DD format (inclusive)"
                }
            },
            required = new[] { "start_date", "end_date" }
        }
    };

    public Task<ToolResult> ExecuteAsync(string startDate, string endDate)
    {
        try
        {
            if (!DateOnly.TryParse(startDate, out var start) ||
                !DateOnly.TryParse(endDate,   out var end))
            {
                return Task.FromResult(new ToolResult
                {
                    ToolName = "calculate_working_days",
                    Output   = "Invalid date format. Please use YYYY-MM-DD.",
                    IsError  = true
                });
            }

            if (end < start)
                return Task.FromResult(new ToolResult
                {
                    ToolName = "calculate_working_days",
                    Output   = "End date must be on or after start date.",
                    IsError  = true
                });

            int workingDays = 0;
            var current = start;
            while (current <= end)
            {
                if (current.DayOfWeek != DayOfWeek.Saturday &&
                    current.DayOfWeek != DayOfWeek.Sunday)
                    workingDays++;
                current = current.AddDays(1);
            }

            int totalDays = end.DayNumber - start.DayNumber + 1;

            return Task.FromResult(new ToolResult
            {
                ToolName = "calculate_working_days",
                Output   = $"From {start:dd MMM yyyy} to {end:dd MMM yyyy}:\n" +
                           $"  Total calendar days: {totalDays}\n" +
                           $"  Working days (Mon–Fri): {workingDays}\n" +
                           $"  Weekend days: {totalDays - workingDays}"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ToolResult
            {
                ToolName = "calculate_working_days",
                Output   = $"Calculation failed: {ex.Message}",
                IsError  = true
            });
        }
    }
}
