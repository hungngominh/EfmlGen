namespace EfmlGen.Core;

public static class DefaultLiterals
{
    /// <summary>
    /// Trả về literal C# cho default value của property (dùng trong constructor).
    /// Quy ước match Entity Developer: chỉ sinh assignment nếu có default trong efml column.
    /// </summary>
    public static string? CSharpInitializer(EfProperty p)
    {
        var def = p.Column.Default;
        if (!string.IsNullOrEmpty(def))
        {
            var fromDef = FromDefaultLiteral(p.Type, def);
            if (fromDef != null) return fromDef;
        }

        // Fallback: NOT NULL + ValidateRequired value-type → emit type's zero default
        // (matches Entity Developer behavior on required, non-nullable primitives).
        if (p.Column.NotNull && p.ValidateRequired)
        {
            return ZeroLiteral(p.Type);
        }

        return null;
    }

    private static string? FromDefaultLiteral(EfType type, string def) => type switch
    {
        EfType.Boolean => def.Equals("true", System.StringComparison.OrdinalIgnoreCase) ? "true" : "false",
        EfType.Int16 or EfType.Int32 or EfType.Int64 or EfType.Byte
            => int.TryParse(def, out var i) ? i.ToString() : null,
        EfType.Decimal => decimal.TryParse(def, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m"
            : null,
        EfType.Double => double.TryParse(def, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) + "d"
            : null,
        EfType.Single => float.TryParse(def, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) + "f"
            : null,
        // String / Guid / DateTime / Blob → skip (default thường là SQL function như uuid_generate_v7())
        _ => null
    };

    private static string? ZeroLiteral(EfType type) => type switch
    {
        EfType.Boolean => "false",
        EfType.Int16 or EfType.Int32 or EfType.Int64 or EfType.Byte => "0",
        EfType.Decimal => "0m",
        EfType.Double => "0d",
        EfType.Single => "0f",
        // Skip reference types (String/Blob) and Guid/DateTime/DateTimeOffset/TimeSpan
        // — Entity Developer doesn't emit zero literals for these.
        _ => null
    };
}
