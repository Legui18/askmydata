using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace AskMyData.Api.Mapping;

/// <summary>Rejects strings that are empty or consist only of white-space (R5.3).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class NotWhiteSpaceAttribute : ValidationAttribute
{
    public NotWhiteSpaceAttribute()
        : base("The {0} field must contain at least one non-white-space character.")
    {
    }

    public override bool IsValid(object? value) => value is string text && !string.IsNullOrWhiteSpace(text);
}

/// <summary>Question_Request body of <c>POST /api/ask</c>.</summary>
public sealed class AskRequestDto
{
    public const int MaxQuestionLength = 1000;

    /// <summary>Natural-language question, 1 to 1000 characters, not white-space only.</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(MaxQuestionLength, MinimumLength = 1)]
    [NotWhiteSpace]
    public string? Question { get; init; }

    /// <summary>Optional id of an existing Conversation.</summary>
    public string? ConversationId { get; init; }
}

/// <summary>Result column description.</summary>
public sealed record ResultColumnDto(string Name, string Type);

/// <summary>Result table: columns and rows (one value per column).</summary>
public sealed record ResultTableDto(IReadOnlyList<ResultColumnDto> Columns, IReadOnlyList<object?[]> Rows);

/// <summary>Chart_Suggestion: <c>bar</c>, <c>line</c> or <c>table-only</c> plus the columns to plot.</summary>
public sealed record ChartSuggestionDto(string Type, string? XColumn, IReadOnlyList<string> YColumns);

/// <summary>Usage_Summary (R7.10).</summary>
public sealed record UsageDto(int PromptTokens, int CompletionTokens, int TotalTokens, decimal EstimatedCostUsd, long ElapsedMs);

/// <summary>Question_Response returned with HTTP 200 (R5.2).</summary>
public sealed record QuestionResponseDto(
    string ConversationId,
    string Sql,
    ResultTableDto Result,
    bool Truncated,
    ChartSuggestionDto ChartSuggestion,
    string Explanation,
    IReadOnlyList<string> MetricUsage,
    IReadOnlyList<string> Warnings,
    UsageDto Usage);

/// <summary>
/// RFC 7807 problem details with the AskMyData extensions. Optional members are omitted when null.
/// </summary>
public sealed class AskProblemDetails : ProblemDetails
{
    [JsonPropertyName("reasonCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasonCode { get; set; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public new string? Detail
    {
        get => base.Detail;
        set => base.Detail = value;
    }

    [JsonPropertyName("attemptedSql")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AttemptedSql { get; set; }

    [JsonPropertyName("refusal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Refusal { get; set; }

    [JsonPropertyName("conversationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConversationId { get; set; }

    [JsonPropertyName("correlationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UsageDto? Usage { get; set; }
}
