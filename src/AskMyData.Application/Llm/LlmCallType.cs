namespace AskMyData.Application.Llm;

/// <summary>Kind of ILlmClient call within one request: the initial call or the single Repair_Call.</summary>
public enum LlmCallType
{
    Initial,
    Repair,
}
