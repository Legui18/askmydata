namespace AskMyData.Application.Ask;

/// <summary>A Question_Request handed to the Orchestrator.</summary>
/// <param name="Question">The natural-language question.</param>
/// <param name="ConversationId">Optional Conversation_Id of an existing Conversation.</param>
/// <param name="CorrelationId">Correlation_Id of the request.</param>
public sealed record AskCommand(string Question, string? ConversationId, string CorrelationId);
