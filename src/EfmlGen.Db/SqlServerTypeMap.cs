using EfmlGen.Core;

namespace EfmlGen.Db;

/// <summary>
/// Map SQL Server native store type (vd "nvarchar(250)", "bigint", "datetime2(7)", "decimal(18,2)")
/// về EfType + sql-type + length/precision/scale.
///
/// SqlType chỉ được set khi khác mặc định của EF SQL Server provider — nếu để null
/// thì ContextEmitter sẽ bỏ qua `.HasColumnType(...)`. Mục đích: gen code gọn và
/// trùng với output của Devart Entity Developer trên SQL Server.
/// </summary>
internal static class SqlServerTypeMap
{
    public sealed record TypeInfo(
        EfType EfType,
        string? SqlType,
        int? Length,
        int? Precision,
        int? Scale,
        bool Unicode);

    public static TypeInfo Map(string storeType, bool forceDateTime = false)
    {
        if (string.IsNullOrEmpty(storeType)) return new TypeInfo(EfType.String, null, null, null, null, true);

        var lower = storeType.Trim().ToLowerInvariant();

        int? a = null, b = null;
        bool isMax = false;
        var lparen = lower.IndexOf('(');
        var baseName = lower;
        if (lparen > 0)
        {
            var rparen = lower.IndexOf(')', lparen);
            if (rparen > lparen)
            {
                baseName = lower[..lparen].Trim();
                var args = lower[(lparen + 1)..rparen];
                var parts = args.Split(',');
                var first = parts[0].Trim();
                if (string.Equals(first, "max", System.StringComparison.OrdinalIgnoreCase))
                    isMax = true;
                else if (int.TryParse(first, out var p0)) a = p0;
                if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var p1)) b = p1;
            }
        }

        return baseName switch
        {
            // Integers
            "bigint" => new(EfType.Int64, null, null, null, null, false),
            "int" => new(EfType.Int32, null, null, null, null, false),
            "smallint" => new(EfType.Int16, null, null, null, null, false),
            "tinyint" => new(EfType.Byte, null, null, null, null, false),
            "bit" => new(EfType.Boolean, null, null, null, null, false),

            // Floating / decimal
            "real" => new(EfType.Single, null, null, null, null, false),
            "float" => new(EfType.Double, null, null, null, null, false),
            "decimal" or "numeric" => new(EfType.Decimal, null, null, a, b, false),
            "money" => new(EfType.Decimal, "money", null, null, null, false),
            "smallmoney" => new(EfType.Decimal, "smallmoney", null, null, null, false),

            // Unicode strings
            "nvarchar" => new(EfType.String, isMax ? "nvarchar(max)" : null, isMax ? null : a, null, null, true),
            "nchar" => new(EfType.String, null, a, null, null, true),
            "ntext" => new(EfType.String, "ntext", null, null, null, true),

            // Non-unicode strings
            "varchar" => new(EfType.String, isMax ? "varchar(max)" : null, isMax ? null : a, null, null, false),
            "char" => new(EfType.String, null, a, null, null, false),
            "text" => new(EfType.String, "text", null, null, null, false),

            // XML / json (SQL 2025 has json; treat as string with explicit sql-type)
            "xml" => new(EfType.String, "xml", null, null, null, true),
            "json" => new(EfType.String, "json", null, null, null, true),

            // GUID
            "uniqueidentifier" => new(EfType.Guid, null, null, null, null, false),

            // Date / time
            "datetime" => new(EfType.DateTime, "datetime", null, null, null, false),
            "datetime2" => new(EfType.DateTime, null, null, b ?? a, null, false),
            "smalldatetime" => new(EfType.DateTime, "smalldatetime", null, null, null, false),
            "date" => new(EfType.DateTime, "date", null, null, null, false),
            "time" => new(EfType.TimeSpan, null, null, b ?? a, null, false),
            "datetimeoffset"
                => new(forceDateTime ? EfType.DateTime : EfType.DateTimeOffset, null, null, b ?? a, null, false),

            // Binary
            "varbinary" => new(EfType.Blob, isMax ? "varbinary(max)" : null, isMax ? null : a, null, null, false),
            "binary" => new(EfType.Blob, "binary", a, null, null, false),
            "image" => new(EfType.Blob, "image", null, null, null, false),
            "rowversion" or "timestamp" => new(EfType.Blob, "rowversion", null, null, null, false),

            // Unknown → preserve store-type, treat as unicode string
            _ => new(EfType.String, baseName, null, null, null, true)
        };
    }
}
