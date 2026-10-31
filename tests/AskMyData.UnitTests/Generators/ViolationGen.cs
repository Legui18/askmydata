using AskMyData.Domain.Results;
using AskMyData.Infrastructure.Validation;
using FsCheck;
using FsCheck.Fluent;

namespace AskMyData.UnitTests.Generators;

/// <summary>One kind of violation per validator rule 1 to 14.</summary>
public enum ViolationKind
{
    ParseError,
    MultipleStatements,
    NotSelectOnly,
    Comment,
    OpenRowset,
    OpenQuery,
    LinkedServer,
    CrossDatabase,
    SystemObject,
    TableFunction,
    Function,
    SelectStar,
    NonAllowlistTable,
    NonAllowlistColumn,
}

/// <summary>
/// A tagged violation: its kind, where it is injected, the concrete variant, and the Reason_Code it
/// maps to in the rule table. AST-level violations carry an <see cref="Injection"/>; text-level
/// violations carry a <see cref="TextTransform"/> applied to the printed SQL in <see cref="TextStage"/> order.
/// </summary>
public sealed record Violation(
    ViolationKind Kind,
    InjectionPosition Position,
    string Variant,
    string ExpectedReasonCode,
    QueryInjection? Injection = null,
    Func<string, string>? TextTransform = null,
    int TextStage = 0)
{
    public override string ToString() => $"{Kind}@{Position}: {Variant} => {ExpectedReasonCode}";
}

/// <summary>A safe query with one or more injected violations.</summary>
public sealed record ViolationCase(SafeQuery Query, PrintStyle Style, IReadOnlyList<Violation> Violations)
{
    public string Sql
    {
        get
        {
            var injections = Violations.Where(v => v.Injection is not null).Select(v => v.Injection!).ToArray();
            var text = SqlPrinter.Print(Query, Style, injections);
            foreach (var v in Violations.Where(v => v.TextTransform is not null).OrderBy(v => v.TextStage))
            {
                text = v.TextTransform!(text);
            }

            return text;
        }
    }

    /// <summary>The Reason_Code of the violated rule with the lowest order number (Requirement 3.22).</summary>
    public string ExpectedReasonCode =>
        Violations.MinBy(v => ViolationGen.RuleOrder(v.ExpectedReasonCode))!.ExpectedReasonCode;

    public override string ToString() =>
        $"[{string.Join("; ", Violations)}] expected {ExpectedReasonCode}: {Sql}";
}

/// <summary>
/// Injects violations into <see cref="SafeQueryGen"/> queries (Properties 3 and 4). AST-level
/// violations are injected at the top level, inside the wrapping CTE or derived table, or inside an
/// EXISTS subquery; text-level violations (parse errors, extra statements, non-SELECT statements,
/// comments) transform the printed text.
/// </summary>
public static class ViolationGen
{
    private const string SecondStatement = "SELECT o.OrderId FROM dbo.Orders AS o";

    private static readonly ViolationKind[] AllKinds = Enum.GetValues<ViolationKind>();

    /// <summary>A safe query with exactly one violation.</summary>
    public static Gen<ViolationCase> Single { get; } = Cases(1, 1);

    /// <summary>A safe query with two to four violations of distinct kinds.</summary>
    public static Gen<ViolationCase> Many { get; } = Cases(2, 4);

    /// <summary>A safe query with exactly one violation of <paramref name="kind"/>.</summary>
    public static Gen<ViolationCase> Of(ViolationKind kind) =>
        from query in SafeQueryGen.Query
        from style in RewriteGen.Style
        from v in ViolationOf(kind, 0, style.Wrap)
        select new ViolationCase(query, style, [v]);

    /// <summary>The order number of the rule that returns <paramref name="reasonCode"/>.</summary>
    public static int RuleOrder(string reasonCode) =>
        ScriptDomSqlValidator.Rules.Single(r => r.ReasonCode == reasonCode).Order;

    public static string ReasonCodeOf(ViolationKind kind) => kind switch
    {
        ViolationKind.ParseError => ReasonCodes.ParseError,
        ViolationKind.MultipleStatements => ReasonCodes.MultipleStatements,
        ViolationKind.NotSelectOnly => ReasonCodes.NotSelectOnly,
        ViolationKind.Comment => ReasonCodes.CommentNotAllowed,
        ViolationKind.OpenRowset => ReasonCodes.OpenRowsetNotAllowed,
        ViolationKind.OpenQuery => ReasonCodes.OpenQueryNotAllowed,
        ViolationKind.LinkedServer => ReasonCodes.LinkedServerNotAllowed,
        ViolationKind.CrossDatabase => ReasonCodes.CrossDatabaseNotAllowed,
        ViolationKind.SystemObject => ReasonCodes.SystemObjectNotAllowed,
        ViolationKind.TableFunction => ReasonCodes.TableFunctionNotAllowed,
        ViolationKind.Function => ReasonCodes.FunctionNotAllowed,
        ViolationKind.SelectStar => ReasonCodes.SelectStar,
        ViolationKind.NonAllowlistTable => ReasonCodes.TableNotAllowed,
        ViolationKind.NonAllowlistColumn => ReasonCodes.ColumnNotAllowed,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static Gen<ViolationCase> Cases(int min, int max) =>
        from query in SafeQueryGen.Query
        from style in RewriteGen.Style
        from kinds in GenHelpers.Distinct(AllKinds, min, max)
        from violations in GenHelpers.Sequence(kinds.Select((k, i) => ViolationOf(k, i, style.Wrap)))
        select new ViolationCase(query, style, violations);

    private static Gen<Violation> ViolationOf(ViolationKind kind, int index, WrapKind wrap)
    {
        var code = ReasonCodeOf(kind);
        var alias = $"r{index}";
        return kind switch
        {
            ViolationKind.ParseError => Text(kind, code, 3,
                ("empty", _ => string.Empty),
                ("whitespace", _ => " \t\r\n "),
                ("unclosed paren at end", t => t + " ("),
                ("unclosed paren at start", t => "(" + t)),
            ViolationKind.MultipleStatements => Text(kind, code, 2,
                ("semicolon", t => $"{t}; {SecondStatement}"),
                ("no separator", t => $"{t} {SecondStatement}"),
                ("GO batch", t => $"{t}\nGO\n{SecondStatement}"),
                ("stacked DROP", t => $"{t}; DROP TABLE dbo.Orders")),
            ViolationKind.NotSelectOnly => Gen.OneOf(
                Text(kind, code, 0,
                    ("EXEC sp_who", _ => "EXEC sp_who"),
                    ("EXECUTE procedure", _ => "EXECUTE dbo.usp_Report 1"),
                    ("system procedure", _ => "EXEC sys.sp_configure"),
                    ("UPDATE", _ => "UPDATE dbo.Orders SET Status = 'x'"),
                    ("DELETE", _ => "DELETE FROM dbo.Orders"),
                    ("INSERT", _ => "INSERT INTO dbo.Orders (OrderId) VALUES (1)"),
                    ("DROP", _ => "DROP TABLE dbo.Orders"),
                    ("TRUNCATE", _ => "TRUNCATE TABLE dbo.Orders"),
                    ("CREATE", _ => "CREATE TABLE dbo.T (x int)"),
                    ("DECLARE", _ => "DECLARE @x int"),
                    ("SET", _ => "SET NOCOUNT ON")),
                Gen.Constant(new Violation(kind, InjectionPosition.TopLevel, "SELECT INTO", code,
                    new QueryInjection(InjectionPosition.TopLevel, [], [], IntoTarget: "dbo.OrdersCopy")))),
            ViolationKind.Comment =>
                from marker in Gen.Elements("/* injected */", "-- injected\n", "/**/", "--\n")
                from slot in Gen.Choose(0, 10_000)
                select new Violation(kind, InjectionPosition.Text, marker.Trim(), code,
                    TextTransform: t => InsertComment(t, marker, slot), TextStage: 1),
            ViolationKind.OpenRowset => Source(kind, code, wrap,
                $"CROSS JOIN OPENROWSET('SQLNCLI', 'Server=srv;Trusted_Connection=yes;', 'SELECT 1') AS {alias}",
                $"CROSS JOIN OPENROWSET(BULK 'C:\\data.txt', SINGLE_CLOB) AS {alias}",
                $"CROSS JOIN OPENDATASOURCE('SQLNCLI', 'Data Source=srv;Integrated Security=SSPI').Db.dbo.T AS {alias}"),
            ViolationKind.OpenQuery => Source(kind, code, wrap,
                $"CROSS JOIN OPENQUERY(LinkedSrv, 'SELECT 1') AS {alias}"),
            ViolationKind.LinkedServer => Source(kind, code, wrap,
                $"CROSS JOIN LinkedSrv.SalesDb.dbo.Orders AS {alias}",
                $"CROSS JOIN [LinkedSrv].[SalesDb].[dbo].[Customers] AS {alias}"),
            ViolationKind.CrossDatabase => Source(kind, code, wrap,
                $"CROSS JOIN OtherDb.dbo.Orders AS {alias}",
                $"CROSS JOIN master..spt_values AS {alias}"),
            ViolationKind.SystemObject => Gen.OneOf(
                Source(kind, code, wrap,
                    $"CROSS JOIN sys.objects AS {alias}",
                    $"CROSS JOIN Sys.Tables AS {alias}",
                    $"CROSS JOIN INFORMATION_SCHEMA.TABLES AS {alias}",
                    $"CROSS JOIN [information_schema].[columns] AS {alias}"),
                Item(kind, code, wrap, "sys function", (_, _) => $"sys.fn_varbintohexstr(0x01) AS F{index}")),
            ViolationKind.TableFunction => Source(kind, code, wrap,
                $"CROSS APPLY STRING_SPLIT('a,b', ',') AS {alias}",
                $"CROSS APPLY OPENJSON('[]') AS {alias}",
                $"CROSS JOIN dbo.fnRows(1) AS {alias}"),
            ViolationKind.Function => Gen.OneOf(
                Item(kind, code, wrap, "dbo.fnSecret(1)", (_, _) => $"dbo.fnSecret(1) AS F{index}"),
                Item(kind, code, wrap, "[dbo].[fnTax](2, 3)", (_, _) => $"[dbo].[fnTax](2, 3) AS F{index}"),
                Item(kind, code, wrap, "Sales.fnCalc(1)", (_, _) => $"Sales.fnCalc(1) AS F{index}")),
            ViolationKind.SelectStar => Gen.OneOf(
                Item(kind, code, wrap, "*", (_, _) => "*"),
                Item(kind, code, wrap, "t.*", (p, scope) => $"{p.Qualifier(scope[index % scope.Count])}.*")),
            ViolationKind.NonAllowlistTable => Source(kind, code, wrap,
                $"CROSS JOIN dbo.Employees AS {alias}",
                $"CROSS JOIN DatasetVersion AS {alias}",
                $"CROSS JOIN dbo.Secrets AS {alias}",
                $"CROSS JOIN sales.Orders AS {alias}"),
            ViolationKind.NonAllowlistColumn => Gen.OneOf(
                Item(kind, code, wrap, "t.Secret", (p, scope) => $"{p.Qualifier(scope[index % scope.Count])}.{p.Ident("Secret")} AS C{index}"),
                Item(kind, code, wrap, "t.Salary", (p, scope) => $"{p.Qualifier(scope[index % scope.Count])}.{p.Ident("Salary")} AS C{index}")),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>Positions available for AST-level injections under <paramref name="wrap"/>.</summary>
    private static InjectionPosition[] Positions(WrapKind wrap) => wrap switch
    {
        WrapKind.Cte => [InjectionPosition.TopLevel, InjectionPosition.Cte, InjectionPosition.Subquery],
        WrapKind.DerivedTable => [InjectionPosition.TopLevel, InjectionPosition.DerivedTable, InjectionPosition.Subquery],
        _ => [InjectionPosition.TopLevel, InjectionPosition.Subquery],
    };

    private static Gen<Violation> Text(
        ViolationKind kind, string code, int stage, params (string Variant, Func<string, string> Transform)[] variants) =>
        Gen.Elements(variants).Select(v =>
            new Violation(kind, InjectionPosition.Text, v.Variant, code, TextTransform: v.Transform, TextStage: stage));

    private static Gen<Violation> Source(ViolationKind kind, string code, WrapKind wrap, params string[] sources) =>
        from source in Gen.Elements(sources)
        from position in Gen.Elements(Positions(wrap))
        select new Violation(kind, position, source, code,
            new QueryInjection(position, [], [(_, _) => source]));

    private static Gen<Violation> Item(ViolationKind kind, string code, WrapKind wrap, string variant, SqlFragment item) =>
        Gen.Elements(Positions(wrap)).Select(position => new Violation(kind, position, variant, code,
            new QueryInjection(position, [item], [])));

    /// <summary>Inserts <paramref name="marker"/> at a space outside string literals (or at the end).</summary>
    private static string InsertComment(string text, string marker, int slot)
    {
        var spaces = SqlText.SpacesOutsideLiterals(text);
        if (spaces.Count == 0)
        {
            return text + " " + marker;
        }

        var at = spaces[slot % spaces.Count];
        return $"{text[..at]} {marker} {text[(at + 1)..]}";
    }
}
