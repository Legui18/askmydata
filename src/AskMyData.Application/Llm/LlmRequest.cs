namespace AskMyData.Application.Llm;

/// <summary>
/// Everything the System sends to an ILlmClient for one call (R9.1).
/// </summary>
/// <param name="SystemPrompt">Rules, Metric definitions, Allowlist schema and, on a Repair_Call, the repair block.</param>
/// <param name="UntrustedSection">Nonce-delimited question, history and, on a Repair_Call, the failing SQL.</param>
/// <param name="CallType">Initial call or Repair_Call.</param>
/// <param name="Repair">Repair information; <c>null</c> on the initial call.</param>
public sealed record LlmRequest(
    string SystemPrompt,
    string UntrustedSection,
    LlmCallType CallType,
    RepairInfo? Repair);
