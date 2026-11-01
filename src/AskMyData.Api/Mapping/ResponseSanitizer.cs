namespace AskMyData.Api.Mapping;

/// <summary>
/// Applies the <see cref="ResponseLeakGuard"/> to every free-text member of a mapped response (explanation,
/// warnings, refusal, error messages, SQL, cell values) and adds the leak warning when anything was redacted (R10.3).
/// </summary>
public sealed class ResponseSanitizer(ResponseLeakGuard guard)
{
    public MappedResponse Sanitize(MappedResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Body switch
        {
            QuestionResponseDto dto => response with { Body = SanitizeSuccess(dto) },
            AskProblemDetails problem => response with { Body = SanitizeProblem(problem) },
            _ => response,
        };
    }

    private QuestionResponseDto SanitizeSuccess(QuestionResponseDto dto)
    {
        var any = false;
        string R(string? text)
        {
            var result = guard.Redact(text, out var changed);
            any |= changed;
            return result;
        }

        var columns = dto.Result.Columns.Select(c => c with { Name = R(c.Name) }).ToList();
        var rows = dto.Result.Rows
            .Select(row => row.Select(v => v is string s ? R(s) : v).ToArray())
            .ToList();
        var warnings = dto.Warnings.Select(R).ToList();
        var metricUsage = dto.MetricUsage.Select(R).ToList();
        var chart = dto.ChartSuggestion with
        {
            XColumn = dto.ChartSuggestion.XColumn is null ? null : R(dto.ChartSuggestion.XColumn),
            YColumns = dto.ChartSuggestion.YColumns.Select(R).ToList(),
        };
        var sql = R(dto.Sql);
        var explanation = R(dto.Explanation);

        if (any)
        {
            warnings.Add(ResponseLeakGuard.LeakWarning);
        }

        return dto with
        {
            Sql = sql,
            Explanation = explanation,
            Result = new ResultTableDto(columns, rows),
            ChartSuggestion = chart,
            MetricUsage = metricUsage,
            Warnings = warnings,
        };
    }

    private AskProblemDetails SanitizeProblem(AskProblemDetails problem)
    {
        var any = false;
        string? R(string? text)
        {
            if (text is null)
            {
                return null;
            }

            var result = guard.Redact(text, out var changed);
            any |= changed;
            return result;
        }

        problem.Title = R(problem.Title);
        problem.Detail = R(problem.Detail);
        problem.Refusal = R(problem.Refusal);
        problem.AttemptedSql = R(problem.AttemptedSql);

        if (any)
        {
            problem.Extensions["warnings"] = new[] { ResponseLeakGuard.LeakWarning };
        }

        return problem;
    }
}
