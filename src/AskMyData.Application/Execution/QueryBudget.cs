namespace AskMyData.Application.Execution;

/// <summary>
/// Time budget for one SQL command, already computed as min(command timeout, remaining Request_Deadline)
/// by the Orchestrator (R4.2, R4.9).
/// </summary>
/// <param name="CommandTimeout">Maximum duration of the command.</param>
public sealed record QueryBudget(TimeSpan CommandTimeout);
