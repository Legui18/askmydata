using AskMyData.Application.Validation;
using AskMyData.Infrastructure.Validation;
using AskMyData.UnitTests.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Infrastructure.Validation.Properties;

public sealed class RuleOrderProperties
{
    // Feature: askmydata-conversational-analytics, Property 4: First failing rule determines the Reason_Code
    /// <summary>**Validates: Requirements 3.22**</summary>
    [Property(MaxTest = 500)]
    public Property Safe_query_with_several_violations_fails_with_the_lowest_order_rule()
    {
        var validator = new ScriptDomSqlValidator(DatasetModel.Layer);

        return Prop.ForAll(ViolationGen.Many.ToArbitrary(), violationCase =>
        {
            var expected = violationCase.ExpectedReasonCode;
            var result = validator.Validate(violationCase.Sql, []);

            return (!result.Passed && result.ReasonCode == expected)
                .Label($"{violationCase}: got {Describe(result)}")
                .Collect(expected);
        });
    }

    private static string Describe(ValidationResult result) =>
        result.Passed ? "PASS" : $"{result.ReasonCode} ({result.Detail})";
}
