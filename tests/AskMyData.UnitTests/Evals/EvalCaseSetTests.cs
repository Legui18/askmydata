using AskMyData.Domain.Semantic;
using AskMyData.Evals.Cases;
using AskMyData.Infrastructure.Validation;
using FluentAssertions;

namespace AskMyData.UnitTests.Evals;

/// <summary>
/// Asserts the composition of the shipped Eval_Case set (Requirements 6.1 to 6.5, 14.6).
/// </summary>
public class EvalCaseSetTests
{
    private static readonly SemanticLayer Layer = SemanticLayerCatalog.Create();

    private static readonly Lazy<IReadOnlyList<EvalCase>> Cases = new(() =>
        EvalCaseLoader.LoadDirectoryAsync(CasesDirectory()).GetAwaiter().GetResult());

    private static string CasesDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AskMyData.sln")))
                return Path.Combine(dir.FullName, "tests", "AskMyData.Evals", "Cases");
        }

        throw new DirectoryNotFoundException("Repository root (AskMyData.sln) not found.");
    }

    [Fact]
    public void CaseSet_HasAtLeastThirtyCases()
    {
        Cases.Value.Count.Should().BeGreaterThanOrEqualTo(30);
    }

    [Fact]
    public void CaseSet_HasAtLeastTenAdversarialCases()
    {
        Cases.Value.Count(c => c.IsAdversarial).Should().BeGreaterThanOrEqualTo(10);
    }

    [Theory]
    [InlineData("instruction-override")]
    [InlineData("stacked-statements")]
    [InlineData("comment-obfuscation")]
    [InlineData("non-allowlist-table")]
    [InlineData("data-modification")]
    public void CaseSet_CoversEveryRequiredAttackKind(string attack)
    {
        Cases.Value.Should().Contain(c => c.IsAdversarial && c.Attack == attack);
    }

    [Fact]
    public void CaseSet_CoversEveryDeclaredAttackKind()
    {
        var covered = Cases.Value.Where(c => c.IsAdversarial).Select(c => c.Attack).ToHashSet();

        covered.Should().Contain(EvalAttacks.All);
    }

    [Fact]
    public void CaseSet_HasAtLeastOneCasePerSemanticLayerMetric()
    {
        var covered = Cases.Value
            .Where(c => c.Category == EvalCategories.Metric && c.Metric is not null)
            .Select(c => c.Metric!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        covered.Should().Contain(Layer.Metrics.Select(m => m.Name));
    }

    [Fact]
    public void CaseSet_MetricReferencesNameDefinedMetrics()
    {
        var names = Layer.Metrics.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Cases.Value.Where(c => c.Metric is not null).Select(c => c.Metric!).Should().OnlyContain(m => names.Contains(m));
    }

    [Fact]
    public void CaseSet_HasMultiTurnCasesWithPriorTurns()
    {
        var multiTurn = Cases.Value.Where(c => c.Category == EvalCategories.MultiTurn).ToList();

        multiTurn.Should().NotBeEmpty();
        multiTurn.Should().OnlyContain(c => c.PriorTurns.Count > 0);
    }

    [Fact]
    public void CaseSet_EveryFileMatchesTheSchema()
    {
        var files = Directory.EnumerateFiles(CasesDirectory(), "*.json").ToList();
        files.Should().NotBeEmpty();

        foreach (var file in files)
        {
            var act = () => EvalCaseLoader.Parse(File.ReadAllText(file));

            act.Should().NotThrow($"{Path.GetFileName(file)} must match the case schema");
        }
    }

    [Fact]
    public void CaseSet_AdversarialCasesExpectBlockedAndHaveNoReferenceSql()
    {
        var adversarial = Cases.Value.Where(c => c.IsAdversarial).ToList();

        adversarial.Should().OnlyContain(c => c.Expected.Kind == EvalExpectedKinds.Blocked);
        adversarial.Should().OnlyContain(c => c.ReferenceSql == null);
    }

    [Fact]
    public void CaseSet_EveryNonAdversarialCaseHasReferenceSqlAndRowsExpectation()
    {
        var others = Cases.Value.Where(c => !c.IsAdversarial).ToList();

        others.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.ReferenceSql));
        others.Should().OnlyContain(c => c.Expected.Kind == EvalExpectedKinds.Rows);
    }

    [Fact]
    public void CaseSet_EveryReferenceSqlUsesOnlyAllowlistTablesAndColumns()
    {
        var validator = new ScriptDomSqlValidator(Layer);

        foreach (var c in Cases.Value.Where(c => !string.IsNullOrWhiteSpace(c.ReferenceSql)))
        {
            var result = validator.Validate(c.ReferenceSql!, []);

            result.Passed.Should().BeTrue($"referenceSql of '{c.Id}' must pass validation (got {result.ReasonCode}: {result.Detail})");
        }
    }
}
