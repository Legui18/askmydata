using System.ComponentModel.DataAnnotations;
using AskMyData.Api.Mapping;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Api;

/// <summary>Property 15: Question length validation.</summary>
public class AskRequestValidationProperties
{
    private static readonly Gen<char> Visible = Gen.Elements("abcXYZ019 ?,.-_%é漢".ToCharArray());

    private static readonly Gen<string> WhiteSpaceOnly =
        from n in Gen.Choose(0, 1500)
        from c in Gen.Elements(' ', '\t', '\r', '\n', '\u00A0', '\u2003')
        select new string(c, n);

    private static readonly Gen<string> TooLong =
        from n in Gen.Choose(1001, 3000)
        from c in Gen.Elements('a', 'Z', '7', '?')
        select new string(c, n);

    private static readonly Gen<string> InRange =
        from n in Gen.Choose(1, 1000)
        from chars in Gen.ArrayOf(Visible, n)
        from pos in Gen.Choose(0, n - 1)
        select EnsureNonBlank(chars, pos);

    private static string EnsureNonBlank(char[] chars, int pos)
    {
        chars[pos] = 'q';
        return new string(chars);
    }

    private static bool IsValid(string? question, out List<ValidationResult> results)
    {
        var dto = new AskRequestDto { Question = question };
        results = new List<ValidationResult>();
        return Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
    }

    // Feature: askmydata-conversational-analytics, Property 15: Question length validation
    // **Validates: Requirements 5.3**
    [Property(MaxTest = 100)]
    public Property Empty_whitespace_or_overlong_questions_fail_validation_with_a_message() =>
        Prop.ForAll(
            Gen.OneOf(WhiteSpaceOnly, TooLong).ToArbitrary(),
            question =>
            {
                var valid = IsValid(question, out var results);
                valid.Should().BeFalse();
                results.Should().NotBeEmpty();
                results.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.ErrorMessage));
            });

    // Feature: askmydata-conversational-analytics, Property 15: Question length validation
    // **Validates: Requirements 5.3**
    [Property(MaxTest = 100)]
    public Property Questions_of_1_to_1000_characters_with_content_pass_validation() =>
        Prop.ForAll(
            InRange.ToArbitrary(),
            question =>
            {
                IsValid(question, out var results).Should().BeTrue();
                results.Should().BeEmpty();
            });

    // Feature: askmydata-conversational-analytics, Property 15: Question length validation
    // **Validates: Requirements 5.3**
    [Fact]
    public void Missing_question_fails_validation()
    {
        IsValid(null, out var results).Should().BeFalse();
        results.Should().NotBeEmpty();
    }
}
