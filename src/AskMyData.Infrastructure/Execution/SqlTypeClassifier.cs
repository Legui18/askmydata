using AskMyData.Application.Execution;

namespace AskMyData.Infrastructure.Execution;

/// <summary>Maps SQL Server type names (from <c>GetDataTypeName</c>) to a <see cref="ColumnKind"/>.</summary>
public static class SqlTypeClassifier
{
    public static ColumnKind Classify(string? sqlTypeName)
    {
        if (string.IsNullOrWhiteSpace(sqlTypeName))
        {
            return ColumnKind.Other;
        }

        var name = sqlTypeName.Trim();
        var paren = name.IndexOf('(');
        if (paren >= 0)
        {
            name = name[..paren].Trim();
        }

        // Strip a schema qualifier such as "sys." if present.
        var dot = name.LastIndexOf('.');
        if (dot >= 0)
        {
            name = name[(dot + 1)..];
        }

        return name.ToLowerInvariant() switch
        {
            "tinyint" or "smallint" or "int" or "bigint" or "decimal" or "numeric"
                or "money" or "smallmoney" or "float" or "real" => ColumnKind.Numeric,
            "date" or "time" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => ColumnKind.Temporal,
            "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" => ColumnKind.Text,
            "bit" => ColumnKind.Boolean,
            _ => ColumnKind.Other,
        };
    }
}
