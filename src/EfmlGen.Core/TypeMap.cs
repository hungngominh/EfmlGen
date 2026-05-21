using System;
using System.Collections.Generic;

namespace EfmlGen.Core;

public static class TypeMap
{
    public static string ToCSharp(EfType type) => type switch
    {
        EfType.Int16 => "short",
        EfType.Int32 => "int",
        EfType.Int64 => "long",
        EfType.String => "string",
        EfType.Boolean => "bool",
        EfType.DateTime => "DateTime",
        EfType.DateTimeOffset => "DateTimeOffset",
        EfType.TimeSpan => "TimeSpan",
        EfType.Decimal => "decimal",
        EfType.Double => "double",
        EfType.Single => "float",
        EfType.Byte => "byte",
        EfType.Guid => "Guid",
        EfType.Blob => "byte[]",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static string CSharpTypeWithNullability(EfProperty p)
    {
        var baseType = ToCSharp(p.Type);
        if (!p.IsNullable) return baseType;
        return p.Type switch
        {
            EfType.String => baseType,
            EfType.Blob => baseType,
            _ => baseType + "?"
        };
    }

    private static readonly Dictionary<string, EfType> _fromString = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Int16"] = EfType.Int16,
        ["Int32"] = EfType.Int32,
        ["Int64"] = EfType.Int64,
        ["String"] = EfType.String,
        ["Boolean"] = EfType.Boolean,
        ["DateTime"] = EfType.DateTime,
        ["DateTimeOffset"] = EfType.DateTimeOffset,
        ["TimeSpan"] = EfType.TimeSpan,
        ["Decimal"] = EfType.Decimal,
        ["Double"] = EfType.Double,
        ["Single"] = EfType.Single,
        ["Byte"] = EfType.Byte,
        ["Guid"] = EfType.Guid,
        ["Blob"] = EfType.Blob,
        ["VarBinary"] = EfType.Blob,
        ["Clob"] = EfType.String,
        ["Time"] = EfType.TimeSpan,
    };

    public static EfType Parse(string s) =>
        _fromString.TryGetValue(s, out var t)
            ? t
            : throw new ArgumentException(
                $"Unknown efml type: '{s}'. Valid types: {string.Join(", ", _fromString.Keys)}.");

    public static string ToEfml(EfType type) => type.ToString();
}
