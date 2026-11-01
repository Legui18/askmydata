using System.Text.Json;
using AskMyData.Api.Mapping;
using AskMyData.Application.Ask;
using AskMyData.Application.Charts;
using AskMyData.Application.Execution;
using AskMyData.Domain.Results;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace AskMyData.UnitTests.Api;

/// <summary>Property 14: Outcome to HTTP status mapping.</summary>
public class OutcomeHttpMapperProperties
{
    private const string CorrelationId = "corr-1";

    private static readonly OutcomeHttpMapper Mapper = new(NullLogger<OutcomeHttpMapper>.Instance);

    private static readonly Gen<string> Id =
        Gen.Elements("c1", "conv-42", "7f3a9b", "abc-def-ghi", "z");

    private static readonly Gen<string> Text =
        Gen.Elements("I cannot modify data.", "No such data.", "x", "Reason: out of scope", new string('r', 300));

    private static readonly Gen<UsageSummary> Usage =
        from p in Gen.Choose(0, 5000)
        from c in Gen.Choose(0, 5000)
        from ms in Gen.Choose(0, 60000)
        select new UsageSummary(p, c, 0.001m * (p + c), ms);

    private static readonly Gen<QueryResult> Result =
        from rowCount in Gen.Choose(0, 6)
        from truncated in Gen.Elements(true, false)
        select new QueryResult(
            new List<ResultColumn>
            {
                new("Name", "nvarchar", ColumnKind.Numeric),
                new("Total", "decimal", ColumnKind.Numeric),
            },
            Enumerable.Range(0, rowCount).Select(i => new object?[] { "n" + i, (decimal)i }).ToList(),
            truncated,
            TimeSpan.FromMilliseconds(5));

    private static readonly Gen<ChartSuggestion> Chart =
        Gen.Elements(
            new ChartSuggestion(ChartType.Bar, "Name", new[] { "Total" }),
            new ChartSuggestion(ChartType.Line, "Name", new[] { "Total" }),
            new ChartSuggestion(ChartType.TableOnly, null, Array.Empty<string>()));

    private static readonly Gen<AskOutcome> Succeeded =
        from id in Id
        from usage in Usage
        from result in Result
        from chart in Chart
        from explanation in Text
        select (AskOutcome)new AskSucceeded(
            id, usage, "SELECT 1", result, chart, explanation, new[] { "Revenue" }, new[] { "w" });

    private static readonly Gen<AskOutcome> ValidationFailed =
        from id in Id
        from usage in Usage
        from code in Gen.Elements(ReasonCodes.ParseError, "NOT_SELECT", "BLOCKED_OBJECT")
        select (AskOutcome)new AskValidationFailed(id, usage, code, "detail", "DROP TABLE x");

    private static readonly Gen<AskOutcome> Refused =
        from id in Id
        from usage in Usage
        from text in Text
        select (AskOutcome)new AskRefused(id, usage, text);

    private static readonly Gen<AskOutcome> GenerationInvalid =
        from id in Id
        from usage in Usage
        select (AskOutcome)new AskGenerationInvalid(id, usage);

    private static readonly Gen<AskOutcome> LlmUnavailable =
        from id in Id
        from usage in Usage
        from timedOut in Gen.Elements(true, false)
        select (AskOutcome)new AskLlmUnavailable(id, usage, timedOut);

    private static readonly Gen<AskOutcome> ExecTimeout =
        from id in Id
        from usage in Usage
        select (AskOutcome)new AskExecutionFailed(id, usage, ExecutionErrorCategory.Timeout);

    private static readonly Gen<AskOutcome> ExecError =
        from id in Id
        from usage in Usage
        select (AskOutcome)new AskExecutionFailed(id, usage, ExecutionErrorCategory.ExecutionError);

    private static readonly Gen<AskOutcome> Deadline =
        from id in Id
        from usage in Usage
        select (AskOutcome)new AskDeadlineExceeded(id, usage);

    private static readonly Gen<AskOutcome> NotFound =
        Id.Select(id => (AskOutcome)new AskConversationNotFound(id));

    private static readonly Gen<AskOutcome> Aborted =
        from id in Id
        from usage in Usage
        select (AskOutcome)new AskClientAborted(id, usage);

    private static string Json(object? body) => JsonSerializer.Serialize(body, body!.GetType());

    // Feature: askmydata-conversational-analytics, Property 14: Outcome to HTTP status mapping
    // **Validates: Requirements 5.2, 5.4, 5.5, 5.8, 5.9, 5.12, 5.13, 5.14**
    [Property(MaxTest = 100)]
    public Property Successful_outcome_maps_to_200_with_every_question_response_field() =>
        Prop.ForAll(Succeeded.ToArbitrary(), outcome =>
        {
            var s = (AskSucceeded)outcome;
            var mapped = Mapper.Map(s, CorrelationId);

            mapped.StatusCode.Should().Be(200);
            mapped.WritesResponse.Should().BeTrue();
            var body = mapped.Body.Should().BeOfType<QuestionResponseDto>().Subject;
            body.ConversationId.Should().Be(s.ConversationId);
            body.Sql.Should().Be(s.Sql);
            body.Result.Columns.Should().HaveCount(s.Result.Columns.Count);
            body.Result.Rows.Should().HaveCount(s.Result.Rows.Count);
            body.Truncated.Should().Be(s.Result.Truncated);
            body.ChartSuggestion.Type.Should().Be(s.Chart.Type.ToWireString());
            body.Explanation.Should().Be(s.Explanation);
            body.MetricUsage.Should().Equal(s.MetricUsage);
            body.Warnings.Should().Equal(s.Warnings);
            body.Usage.TotalTokens.Should().Be(s.Usage.PromptTokens + s.Usage.CompletionTokens);
            body.Usage.ElapsedMs.Should().Be(s.Usage.ElapsedMs);
            if (s.Result.Rows.Count == 0)
            {
                body.Result.Rows.Should().BeEmpty();
            }

            return true;
        });

    // Feature: askmydata-conversational-analytics, Property 14: Outcome to HTTP status mapping
    // **Validates: Requirements 5.4, 5.12, 5.13**
    [Property(MaxTest = 100)]
    public Property Rejections_map_to_422_with_their_reason_code() =>
        Prop.ForAll(
            Gen.OneOf(ValidationFailed, Refused, GenerationInvalid).ToArbitrary(),
            outcome =>
            {
                var mapped = Mapper.Map(outcome, CorrelationId);
                mapped.StatusCode.Should().Be(422);
                var problem = mapped.Body.Should().BeOfType<AskProblemDetails>().Subject;
                problem.CorrelationId.Should().Be(CorrelationId);

                switch (outcome)
                {
                    case AskValidationFailed v:
                        problem.ReasonCode.Should().Be(v.ReasonCode);
                        break;
                    case AskRefused r:
                        problem.ReasonCode.Should().Be(ReasonCodes.Refused);
                        problem.Refusal.Should().Be(r.RefusalText);
                        break;
                    case AskGenerationInvalid:
                        problem.ReasonCode.Should().Be(ReasonCodes.GenerationInvalid);
                        break;
                }

                return true;
            });

    // Feature: askmydata-conversational-analytics, Property 14: Outcome to HTTP status mapping
    // **Validates: Requirements 5.5, 5.8, 5.14**
    [Property(MaxTest = 100)]
    public Property Llm_failure_maps_to_503_and_timeouts_map_to_504() =>
        Prop.ForAll(
            Gen.OneOf(LlmUnavailable, ExecTimeout, Deadline).ToArbitrary(),
            outcome =>
            {
                var mapped = Mapper.Map(outcome, CorrelationId);
                var problem = mapped.Body.Should().BeOfType<AskProblemDetails>().Subject;

                switch (outcome)
                {
                    case AskLlmUnavailable l:
                        mapped.StatusCode.Should().Be(503);
                        problem.ReasonCode.Should().Be(l.TimedOut ? "LLM_TIMEOUT" : "LLM_UNAVAILABLE");
                        break;
                    case AskExecutionFailed:
                        mapped.StatusCode.Should().Be(504);
                        problem.ReasonCode.Should().Be("TIMEOUT");
                        break;
                    case AskDeadlineExceeded:
                        mapped.StatusCode.Should().Be(504);
                        problem.ReasonCode.Should().Be("DEADLINE_EXCEEDED");
                        break;
                }

                return true;
            });

    // Feature: askmydata-conversational-analytics, Property 14: Outcome to HTTP status mapping
    // **Validates: Requirements 5.9**
    [Property(MaxTest = 100)]
    public Property Execution_error_maps_to_502_and_body_never_contains_sql_server_text() =>
        Prop.ForAll(
            ExecError.ToArbitrary(),
            Gen.Elements(
                "Invalid object name 'dbo.Secret'.",
                "Msg 208, Level 16, State 1, Line 3",
                "Login failed for user 'sa'.",
                "SQLSERVER-ERR-9f2c41").ToArbitrary(),
            (outcome, sqlServerText) =>
            {
                // The outcome carries no server text; the leak check guards the serialized body regardless.
                var mapped = Mapper.Map(outcome, CorrelationId);
                mapped.StatusCode.Should().Be(502);
                var problem = mapped.Body.Should().BeOfType<AskProblemDetails>().Subject;
                problem.ReasonCode.Should().Be("EXECUTION_ERROR");
                problem.Detail.Should().Be(OutcomeHttpMapper.ExecutionErrorMessage);
                Json(mapped.Body).Should().NotContain(sqlServerText);
                return true;
            });

    // Feature: askmydata-conversational-analytics, Property 14: Outcome to HTTP status mapping
    // **Validates: Requirements 5.2**
    [Property(MaxTest = 100)]
    public Property Unknown_conversation_maps_to_404_naming_the_id() =>
        Prop.ForAll(NotFound.ToArbitrary(), outcome =>
        {
            var requested = ((AskConversationNotFound)outcome).RequestedId;
            var mapped = Mapper.Map(outcome, CorrelationId);
            mapped.StatusCode.Should().Be(404);
            var problem = mapped.Body.Should().BeOfType<AskProblemDetails>().Subject;
            problem.Detail.Should().Contain(requested);
            return true;
        });

    // Feature: askmydata-conversational-analytics, Property 14: Outcome to HTTP status mapping
    // **Validates: Requirements 5.2**
    [Property(MaxTest = 100)]
    public Property Client_abort_writes_nothing_and_reports_499() =>
        Prop.ForAll(Aborted.ToArbitrary(), outcome =>
        {
            var mapped = Mapper.Map(outcome, CorrelationId);
            mapped.StatusCode.Should().Be(499);
            mapped.Body.Should().BeNull();
            mapped.WritesResponse.Should().BeFalse();
            return true;
        });
}
