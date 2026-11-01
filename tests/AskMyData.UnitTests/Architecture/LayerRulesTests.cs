using System.Reflection;
using AskMyData.Api.Controllers;
using AskMyData.Api.Mapping;
using AskMyData.Application.Ask;
using AskMyData.Application.Conversations;
using AskMyData.Application.Execution;
using AskMyData.Application.Llm;
using AskMyData.Application.Validation;
using AskMyData.Domain.Semantic;
using AskMyData.Infrastructure.Conversations;
using FluentAssertions;
using NetArchTest.Rules;

namespace AskMyData.UnitTests.Architecture;

/// <summary>
/// Architecture tests that enforce the hexagonal layer rules.
/// Validates: Requirements 1.5, 2.4, 5.7, 11.3, 11.4, 12.1, 12.2.
/// </summary>
public sealed class LayerRulesTests
{
    private const string DomainNs = "AskMyData.Domain";
    private const string ApplicationNs = "AskMyData.Application";
    private const string InfrastructureNs = "AskMyData.Infrastructure";
    private const string ApiNs = "AskMyData.Api";

    private static readonly Assembly Domain = typeof(SemanticLayer).Assembly;
    private static readonly Assembly Application = typeof(ILlmClient).Assembly;
    private static readonly Assembly Infrastructure = typeof(InMemoryConversationStore).Assembly;
    private static readonly Assembly Api = typeof(AskController).Assembly;

    private static readonly string[] BclPrefixes = ["System", "mscorlib", "netstandard"];

    private static IEnumerable<string> ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!);

    private static bool IsBcl(string name) =>
        BclPrefixes.Any(p => name == p || name.StartsWith(p + ".", StringComparison.Ordinal));

    [Fact]
    public void Domain_references_only_the_BCL()
    {
        var offenders = ReferencedNames(Domain).Where(n => !IsBcl(n)).ToList();

        offenders.Should().BeEmpty("Domain depends on nothing (R1.5)");
    }

    [Fact]
    public void Domain_types_do_not_depend_on_other_layers()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNs, InfrastructureNs, ApiNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_references_only_Domain_and_Microsoft_Extensions_abstractions()
    {
        var offenders = ReferencedNames(Application)
            .Where(n => !IsBcl(n)
                && n != DomainNs
                && !n.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty("Application depends only on Domain and Microsoft.Extensions.* (R11.3)");
    }

    [Fact]
    public void Application_Microsoft_Extensions_references_are_abstractions_or_options()
    {
        var extensions = ReferencedNames(Application)
            .Where(n => n.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal));

        extensions.Should().OnlyContain(n => n.EndsWith(".Abstractions", StringComparison.Ordinal)
            || n == "Microsoft.Extensions.Options");
    }

    [Fact]
    public void Application_has_no_vendor_or_outer_layer_references()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Google",
                "Microsoft.Data.SqlClient",
                "System.Data.SqlClient",
                "Microsoft.SqlServer.TransactSql.ScriptDom",
                "Dapper",
                InfrastructureNs,
                ApiNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));

        ReferencedNames(Application).Should().NotContain(n =>
            n.StartsWith("Google", StringComparison.Ordinal)
            || n.StartsWith("Microsoft.Data.SqlClient", StringComparison.Ordinal)
            || n.StartsWith("Microsoft.SqlServer.TransactSql.ScriptDom", StringComparison.Ordinal)
            || n.StartsWith("Dapper", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_ILlmClient_implementation_lives_in_Infrastructure()
    {
        var implementations = new[] { Domain, Application, Infrastructure, Api }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ILlmClient).IsAssignableFrom(t))
            .ToList();

        implementations.Should().NotBeEmpty();
        implementations.Should().OnlyContain(t => t.Assembly == Infrastructure);
        implementations.Should().OnlyContain(t => t.Namespace!.StartsWith(InfrastructureNs, StringComparison.Ordinal));
    }

    [Fact]
    public void IConversationStore_is_in_Application_and_InMemoryConversationStore_in_Infrastructure()
    {
        typeof(IConversationStore).Assembly.Should().BeSameAs(Application);
        typeof(IConversationStore).IsInterface.Should().BeTrue();
        typeof(InMemoryConversationStore).Assembly.Should().BeSameAs(Infrastructure);
        typeof(IConversationStore).IsAssignableFrom(typeof(InMemoryConversationStore)).Should().BeTrue();
    }

    [Fact]
    public void Controllers_do_not_depend_on_infrastructure_domain_or_ports()
    {
        var result = Types.InAssembly(Api)
            .That().HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOnAny(
                InfrastructureNs,
                DomainNs,
                typeof(ILlmClient).FullName!,
                typeof(IQueryExecutor).FullName!,
                typeof(ISqlValidator).FullName!,
                typeof(IConversationStore).FullName!,
                "Microsoft.Data.SqlClient",
                "Google")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Controller_constructors_take_only_handler_mapper_and_leak_guard()
    {
        var allowed = new HashSet<Type>
        {
            typeof(AskQuestionHandler),
            typeof(OutcomeHttpMapper),
            typeof(ResponseSanitizer),
            typeof(ResponseLeakGuard),
        };

        var controllers = Api.GetTypes().Where(t => t.Name.EndsWith("Controller", StringComparison.Ordinal)).ToList();
        controllers.Should().NotBeEmpty();

        var parameterTypes = controllers
            .SelectMany(c => c.GetConstructors())
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);

        parameterTypes.Should().OnlyContain(t => allowed.Contains(t));
    }

    [Fact]
    public void Controller_actions_use_only_DTOs_from_Api_Mapping()
    {
        var actionTypes = typeof(AskController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType))
            .Where(t => t != typeof(CancellationToken))
            .ToList();

        actionTypes.Should().Contain(typeof(AskRequestDto));
        actionTypes.Should().OnlyContain(t => t.Namespace == typeof(AskRequestDto).Namespace);
    }

    [Fact]
    public void ValidatedSql_has_no_public_constructor()
    {
        typeof(ValidatedSql).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty();
        typeof(ValidatedSql).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Should().OnlyContain(c => c.IsAssembly, "the constructor is internal to Application (R2.4)");
    }

    [Fact]
    public void IQueryExecutor_ExecuteAsync_takes_ValidatedSql()
    {
        var method = typeof(IQueryExecutor).GetMethod(nameof(IQueryExecutor.ExecuteAsync));

        method.Should().NotBeNull();
        method!.GetParameters().Select(p => p.ParameterType).Should().Contain(typeof(ValidatedSql));
        method.GetParameters().Should().NotContain(p => p.ParameterType == typeof(string));
        typeof(IQueryExecutor).GetMethods().Should().HaveCount(1);
    }
}
