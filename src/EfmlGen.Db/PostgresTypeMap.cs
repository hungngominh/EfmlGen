using EfmlGen.Core;

namespace EfmlGen.Db;

/// <summary>
/// Map Postgres native store type (vd "character varying(250)", "bigint", "timestamp with time zone")
/// về EfType + sql-type (Postgres internal alias) + length/precision/scale.
/// </summary>
internal static class PostgresTypeMap
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

        // Extract parens (length / precision,scale)
        int? a = null, b = null;
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
                if (int.TryParse(parts[0].Trim(), out var p0)) a = p0;
                if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var p1)) b = p1;
            }
        }

        return baseName switch
        {
            "bigint" or "int8" => new(EfType.Int64, null, null, null, null, false),
            "integer" or "int" or "int4" => new(EfType.Int32, "int4", null, null, null, false),
            "smallint" or "int2" => new(EfType.Int16, null, null, null, null, false),
            "boolean" or "bool" => new(EfType.Boolean, "bool", null, null, null, false),
            "real" or "float4" => new(EfType.Single, null, null, null, null, false),
            "double precision" or "float8" => new(EfType.Double, null, null, null, null, false),
            "numeric" or "decimal" => new(EfType.Decimal, "numeric", null, a, b, false),
            "text" => new(EfType.String, null, null, null, null, true),
            "character varying" or "varchar" => new(EfType.String, null, a, null, null, true),
            "character" or "char" or "bpchar" => new(EfType.String, null, a, null, null, true),
            "uuid" => new(EfType.Guid, "uuid", null, null, null, false),
            "timestamp with time zone" or "timestamptz"
                => new(forceDateTime ? EfType.DateTime : EfType.DateTimeOffset, null, null, null, null, false),
            "timestamp" or "timestamp without time zone" => new(EfType.DateTime, null, null, null, null, false),
            "date" => new(EfType.DateTime, null, null, null, null, false),
            "time" or "time without time zone" => new(EfType.TimeSpan, null, null, null, null, false),
            "time with time zone" or "timetz"
                => new(forceDateTime ? EfType.TimeSpan : EfType.DateTimeOffset, null, null, null, null, false),
            "bytea" => new(EfType.Blob, null, null, null, null, false),
            "json" or "jsonb" => new(EfType.String, baseName, null, null, null, true),
            _ => new(EfType.String, baseName, null, null, null, true)
        };
    }
}
