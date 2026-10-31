using AskMyData.Domain.Results;
using AskMyData.Infrastructure.Validation;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.UnitTests.Generators;

/// <summary>
/// Sample-based checks that the validator generators produce what Properties 1 to 6 assume:
/// safe queries parse and pass, injected violations map to their Reason_Code, and Metric
/// embeddings and mutations behave as intended.
/// </summary>
public sealed class ValidatorGeneratorSanityTests
{
    private const int Count = 300;
    private const int Size = 50;

    private readonly ScriptDomSqlValidator _validator = new(DatasetModel.Layer);

    /// <summary><paramref name="count"/> values; FsCheck's Gen.Sample takes the sample count first, then the size.</summary>
    private static T[] Sample<T>(Gen<T> gen, int count = Count) => Gen.Sample(gen, count, Size).ToArray();

    [Fact]
    public void Join_edges_reference_allowlist_columns()
    {
        foreach (var edge in DatasetModel.Edges)
        {
            DatasetModel.ColumnsOf(edge.FromTable).Should().Contain(c => c.Name == edge.FromColumn);
            DatasetModel.ColumnsOf(edge.ToTable).Should().Contain(c => c.Name == edge.ToColumn);
        }
    }

    [Fact]
    public void Safe_queries_parse_with_TSql160Parser()
    {
        var failures = new List<string>();
        foreach (var query in Sample(SafeQueryGen.Query))
        {
            var sql = query.ToString();
            var parser = new TSql160Parser(initialQuotedIdentifiers: true);
            using var reader = new StringReader(sql);
            parser.Parse(reader, out var errors);
            if (errors.Count > 0)
            {
                failures.Add($"{sql} -> {errors[0].Message}");
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Safe_queries_pass_the_validator_with_and_without_rewrites()
    {
        var rewritten = SafeQueryGen.Query.SelectMany(q => RewriteGen.For(q).Select(r => (q, r)));
        var failures = new List<string>();
        foreach (var (query, rewrite) in Sample(rewritten))
        {
            Check(query.ToString());
            Check(rewrite.Sql);
        }

        failures.Should().BeEmpty();

        void Check(string sql)
        {
            var result = _validator.Validate(sql, []);
            if (!result.Passed)
            {
                failures.Add($"{result.ReasonCode} ({result.Detail}): {sql}");
            }
        }
    }

    [Fact]
    public void Every_violation_kind_yields_its_reason_code()
    {
        var failures = new List<string>();
        foreach (var kind in Enum.GetValues<ViolationKind>())
        {
            foreach (var c in Sample(ViolationGen.Of(kind), 60))
            {
                Expect(c, failures);
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Single_and_multiple_violations_yield_the_lowest_order_reason_code()
    {
        var failures = new List<string>();
        foreach (var c in Sample(ViolationGen.Single).Concat(Sample(ViolationGen.Many)))
        {
            Expect(c, failures);
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Embedded_metrics_pass_when_declared_with_and_without_rewrites()
    {
        var gen = MetricQueryGen.Query.SelectMany(m => RewriteGen.For(m.Query).Select(r => (m, r)));
        var failures = new List<string>();
        foreach (var (metric, rewrite) in Sample(gen))
        {
            Check(metric.Query.ToString(), metric.Metric.Name);
            Check(rewrite.Sql, metric.Metric.Name);
        }

        failures.Should().BeEmpty();

        void Check(string sql, string name)
        {
            var result = _validator.Validate(sql, [name]);
            if (!result.Passed)
            {
                failures.Add($"{name}: {result.ReasonCode} ({result.Detail}): {sql}");
            }
        }
    }

    [Fact]
    public void Mutated_metrics_fail_with_case_b_naming_the_metric()
    {
        var gen = MetricMutationGen.Mutation.SelectMany(m => RewriteGen.For(m.Query).Select(r => (m, r)));
        var failures = new List<string>();
        foreach (var (mutation, rewrite) in Sample(gen))
        {
            foreach (var sql in new[] { mutation.Query.ToString(), rewrite.Sql })
            {
                var result = _validator.Validate(sql, [mutation.Metric.Name]);
                if (result.ReasonCode != ReasonCodes.MetricDefinitionMismatch
                    || result.MetricName != mutation.Metric.Name
                    || result.Detail?.Contains("case (b)", StringComparison.Ordinal) != true)
                {
                    failures.Add($"{mutation.Metric.Name} {mutation.Kind}: {result.ReasonCode} ({result.Detail}): {sql}");
                }
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Mutations_cover_every_kind()
    {
        Sample(MetricMutationGen.Mutation, 1000).Select(m => m.Kind).Distinct()
            .Should().BeEquivalentTo(Enum.GetValues<MetricMutationKind>());
    }

    private void Expect(ViolationCase c, List<string> failures)
    {
        var sql = c.Sql;
        var result = _validator.Validate(sql, []);
        if (result.ReasonCode != c.ExpectedReasonCode)
        {
            failures.Add($"[{string.Join("; ", c.Violations)}] expected {c.ExpectedReasonCode}, got {result.ReasonCode ?? "PASS"} ({result.Detail}): {sql}");
        }
    }
}
