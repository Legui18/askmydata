using AskMyData.Application.Validation;

namespace AskMyData.Infrastructure.Validation;

/// <summary>
/// One validation rule. Rules are stateless and are evaluated in ascending <see cref="Order"/>;
/// the first rule that returns a failure determines the Reason_Code (Requirement 3.22).
/// </summary>
internal interface IValidationRule
{
    /// <summary>Position in the fixed rule table of the design (1 = evaluated first).</summary>
    int Order { get; }

    /// <summary>Reason_Code returned when this rule fails.</summary>
    string ReasonCode { get; }

    /// <summary>Returns <c>null</c> when the rule passes, otherwise a failing result.</summary>
    ValidationResult? Check(ValidationContext context);
}
