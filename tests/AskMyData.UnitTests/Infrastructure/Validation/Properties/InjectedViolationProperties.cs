using AskMyData.Application.Validation;
using AskMyData.Infrastructure.Validation;
using AskMyData.UnitTests.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Infrastructure.Validation.Properties;

public sealed class InjectedViolationProperties
{
    // Feature: askmydata-conversational-analytics, Property 3: Injected violations are rejected with their Reason_Code
    /// <summary>**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10, 3.14, 3.15, 3.16, 3.17, 3.23**</summary>
    [Property(MaxTest = 500)]
    public Property Safe_query_with_one_injected_violation_fails_with_that_violations_reason_code()
    {
        var validator = new ScriptDomSqlValidator(DatasetModel.Layer);

        return Prop.ForAll(ViolationGen.Single.ToArbitrary(), violationCase =>
        {
            var violation = violationCase.Violations.Single();
            var sql = violationCase.Sql;
            var result = validator.Validate(sql, []);

            return (!result.Passed && result.ReasonCode == violation.ExpectedReasonCode)
                .Label($"{violation}: got {Describe(result)}: {sql}")
                .Classify(true, violation.Kind.ToString());
        });
    }

    private static string Describe(ValidationResult result) =>
        result.Passed ? "PASS" : $"{result.ReasonCode} ({result.Detail})";
}
